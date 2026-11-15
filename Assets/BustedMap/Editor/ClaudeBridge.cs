using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 파일 기반 Unity 원격 명령 브리지.
    /// &lt;프로젝트&gt;/ClaudeBridge/inbox/*.json 에 명령을 넣으면 에디터가 실행하고
    /// &lt;프로젝트&gt;/ClaudeBridge/outbox/ 에 결과(json)와 스크린샷(png)을 쓴다.
    /// 메뉴 Busted ▸ Claude Bridge ▸ 켜기/끄기 로 제어 (기본 켜짐).
    /// </summary>
    [InitializeOnLoad]
    public static class ClaudeBridge
    {
        [Serializable]
        public class Cmd
        {
            public string id;
            public string op;
            public string path;        // 메뉴 경로 / 에셋 경로 / 오브젝트 경로 / 씬 경로
            public string filter;      // find_assets 필터
            public string name;
            public string parent;
            public string json;        // set_settings 용 JSON
            public string view = "iso";// screenshot: iso | top
            public int floor;          // screenshot: 0 = 전체
            public int width = 1600, height = 1000;
            public int depth = 2;      // hierarchy 깊이
            public float[] position, rotation, scale;
            public string target;      // screenshot 프레이밍 대상 / inspect 대상
            public string type, method, arg; // invoke: 정적 메서드 호출
        }

        [Serializable]
        class Result { public string id; public string op; public bool ok; public string message; public string time; }

        const string PrefKey = "Busted.ClaudeBridge.Enabled";
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ClaudeBridge"));
        static readonly string Inbox = Path.Combine(Root, "inbox");
        static readonly string Outbox = Path.Combine(Root, "outbox");
        static double nextPoll;
        static readonly List<string> logBuffer = new List<string>();

        static ClaudeBridge()
        {
            Directory.CreateDirectory(Inbox);
            Directory.CreateDirectory(Outbox);
            EditorApplication.update += Poll;
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (type == LogType.Log) return;
                lock (logBuffer)
                {
                    logBuffer.Add($"[{type}] {msg}");
                    if (logBuffer.Count > 200) logBuffer.RemoveAt(0);
                }
            };
            File.WriteAllText(Path.Combine(Root, "heartbeat.txt"), $"loaded {DateTime.Now:yyyy-MM-dd HH:mm:ss} Unity {Application.unityVersion}");
        }

        static bool Enabled => EditorPrefs.GetBool(PrefKey, true);

        [MenuItem("Busted/Claude Bridge/켜기")] static void On() { EditorPrefs.SetBool(PrefKey, true); Debug.Log("[ClaudeBridge] 켜짐"); }
        [MenuItem("Busted/Claude Bridge/끄기")] static void Off() { EditorPrefs.SetBool(PrefKey, false); Debug.Log("[ClaudeBridge] 꺼짐"); }

        static void Poll()
        {
            if (!Enabled || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            File.WriteAllText(Path.Combine(Root, "heartbeat.txt"), $"alive {DateTime.Now:yyyy-MM-dd HH:mm:ss} Unity {Application.unityVersion} scene={EditorSceneManager.GetActiveScene().path}");

            string[] files;
            try { files = Directory.GetFiles(Inbox, "*.json"); } catch { return; }
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files)
            {
                Cmd cmd = null;
                var res = new Result { time = DateTime.Now.ToString("HH:mm:ss") };
                try
                {
                    cmd = JsonUtility.FromJson<Cmd>(File.ReadAllText(f, Encoding.UTF8));
                    File.Delete(f);
                    res.id = cmd.id ?? Path.GetFileNameWithoutExtension(f);
                    res.op = cmd.op;
                    res.message = Execute(cmd, res.id);
                    res.ok = true;
                }
                catch (Exception e)
                {
                    try { File.Delete(f); } catch { }
                    res.id = res.id ?? Path.GetFileNameWithoutExtension(f);
                    res.ok = false;
                    res.message = e.ToString();
                }
                File.WriteAllText(Path.Combine(Outbox, res.id + ".json"), JsonUtility.ToJson(res, true), Encoding.UTF8);
            }
        }

        // ─────────────────────────── 명령 ───────────────────────────
        static string Execute(Cmd c, string id)
        {
            switch (c.op)
            {
                case "ping": return $"pong — Unity {Application.unityVersion}, scene {EditorSceneManager.GetActiveScene().path}";
                case "menu":
                    if (!EditorApplication.ExecuteMenuItem(c.path)) throw new Exception("메뉴 실행 실패: " + c.path);
                    return "실행: " + c.path;
                case "build_map":
                {
                    var s = Settings();
                    BustedMapBuilder.Build(s);
                    return "맵 생성 완료";
                }
                case "set_settings":
                {
                    var s = Settings();
                    JsonUtility.FromJsonOverwrite(c.json, s);
                    EditorUtility.SetDirty(s);
                    AssetDatabase.SaveAssets();
                    return "설정 변경: " + JsonUtility.ToJson(s);
                }
                case "get_settings": return JsonUtility.ToJson(Settings(), true);
                case "screenshot": return Screenshot(c, id);
                case "hierarchy": return Hierarchy(c.depth);
                case "find_assets":
                {
                    var sb = new StringBuilder();
                    var guids = AssetDatabase.FindAssets(c.filter ?? "");
                    for (int i = 0; i < guids.Length && i < 500; i++) sb.AppendLine(AssetDatabase.GUIDToAssetPath(guids[i]));
                    return $"{guids.Length}개\n{sb}";
                }
                case "instantiate":
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.path) ?? throw new Exception("프리팹 없음: " + c.path);
                    var parent = string.IsNullOrEmpty(c.parent) ? null : FindOrCreate(c.parent);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent != null ? parent.transform : null);
                    if (!string.IsNullOrEmpty(c.name)) go.name = c.name;
                    ApplyTransform(go.transform, c);
                    Undo.RegisterCreatedObjectUndo(go, "Claude instantiate");
                    MarkDirty();
                    return "생성: " + PathOf(go.transform);
                }
                case "set_transform":
                {
                    var go = GameObject.Find(c.path) ?? throw new Exception("오브젝트 없음: " + c.path);
                    Undo.RecordObject(go.transform, "Claude transform");
                    ApplyTransform(go.transform, c);
                    MarkDirty();
                    return "변경: " + c.path;
                }
                case "delete":
                {
                    var go = GameObject.Find(c.path) ?? throw new Exception("오브젝트 없음: " + c.path);
                    Undo.DestroyObjectImmediate(go);
                    MarkDirty();
                    return "삭제: " + c.path;
                }
                case "save_scene":
                    EditorSceneManager.SaveOpenScenes();
                    return "씬 저장: " + EditorSceneManager.GetActiveScene().path;
                case "open_scene":
                    EditorSceneManager.SaveOpenScenes(); // 대화상자 없이 저장 후 전환
                    EditorSceneManager.OpenScene(c.path);
                    return "씬 열기: " + c.path;
                case "new_scene":
                {
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    if (!string.IsNullOrEmpty(c.path)) EditorSceneManager.SaveScene(scene, c.path);
                    return "새 씬: " + scene.path;
                }
                case "inspect": return Inspect(c.path, c.depth);
                case "invoke":
                {
                    Type t = null;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { t = asm.GetType(c.type); if (t != null) break; }
                    if (t == null) throw new Exception("타입 없음: " + c.type);
                    var m = t.GetMethod(c.method, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (m == null) throw new Exception("메서드 없음: " + c.method);
                    var ret = m.GetParameters().Length == 1 ? m.Invoke(null, new object[] { c.arg }) : m.Invoke(null, null);
                    return ret?.ToString() ?? "(완료)";
                }
                case "refresh":
                    AssetDatabase.Refresh();
                    return "AssetDatabase.Refresh 요청";
                case "console":
                    lock (logBuffer) { var s = string.Join("\n", logBuffer); logBuffer.Clear(); return s.Length == 0 ? "(경고/에러 없음)" : s; }
                default: throw new Exception("알 수 없는 op: " + c.op);
            }
        }

        static BustedMapBuildSettings Settings()
        {
            var s = AssetDatabase.LoadAssetAtPath<BustedMapBuildSettings>("Assets/BustedMap/BustedMapBuildSettings.asset");
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<BustedMapBuildSettings>();
                AssetDatabase.CreateAsset(s, "Assets/BustedMap/BustedMapBuildSettings.asset");
            }
            if (s.mapJson == null) s.mapJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/BustedMap/BustedMapData.json");
            return s;
        }

        static void MarkDirty() => EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        static void ApplyTransform(Transform t, Cmd c)
        {
            if (c.position != null && c.position.Length == 3) t.position = new Vector3(c.position[0], c.position[1], c.position[2]);
            if (c.rotation != null && c.rotation.Length == 3) t.rotation = Quaternion.Euler(c.rotation[0], c.rotation[1], c.rotation[2]);
            if (c.scale != null && c.scale.Length == 3) t.localScale = new Vector3(c.scale[0], c.scale[1], c.scale[2]);
        }

        static GameObject FindOrCreate(string path)
        {
            var go = GameObject.Find(path);
            if (go != null) return go;
            go = new GameObject(path.Substring(path.LastIndexOf('/') + 1));
            Undo.RegisterCreatedObjectUndo(go, "Claude create parent");
            return go;
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        static string Hierarchy(int depth)
        {
            var sb = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            sb.AppendLine("Scene: " + scene.path);
            foreach (var r in scene.GetRootGameObjects()) Dump(r.transform, 0, depth, sb);
            return sb.ToString();
        }

        static void Dump(Transform t, int lv, int max, StringBuilder sb)
        {
            sb.Append(' ', lv * 2).Append(t.name);
            if (t.childCount > 0) sb.Append($"  ({t.childCount})");
            sb.AppendLine(t.gameObject.activeSelf ? "" : "  [off]");
            if (lv >= max) return;
            foreach (Transform ch in t) Dump(ch, lv + 1, max, sb);
        }

        static string Inspect(string path, int depth)
        {
            var go = GameObject.Find(path) ?? throw new Exception("오브젝트 없음: " + path);
            var sb = new StringBuilder();
            InspectRec(go.transform, 0, Math.Max(1, depth), sb);
            return sb.ToString();
        }

        static string V(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";

        static void InspectRec(Transform t, int lv, int max, StringBuilder sb)
        {
            var go = t.gameObject;
            string prefab = PrefabUtility.IsAnyPrefabInstanceRoot(go)
                ? AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go)) : "";
            var rs = go.GetComponentsInChildren<Renderer>();
            string bounds = "";
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                bounds = $" bounds(min{V(b.min)} size{V(b.size)})";
            }
            var mf = go.GetComponent<MeshFilter>();
            string mesh = mf != null && mf.sharedMesh != null ? $" mesh={mf.sharedMesh.name} meshBounds(c{V(mf.sharedMesh.bounds.center)} s{V(mf.sharedMesh.bounds.size)})" : "";
            sb.Append(' ', lv * 2).Append(t.name)
              .Append($" pos{V(t.position)} rot{V(t.eulerAngles)} scl{V(t.localScale)}")
              .Append(prefab.Length > 0 ? " prefab=" + prefab : "").Append(mesh).Append(bounds).AppendLine();
            if (lv >= max || prefab.Length > 0) return; // 프리팹 내부는 생략
            foreach (Transform ch in t) InspectRec(ch, lv + 1, max, sb);
        }

        // ─────────────────────────── 스크린샷 ───────────────────────────
        static string Screenshot(Cmd c, string id)
        {
            var root = GameObject.Find("BustedMap");
            var hidden = new List<GameObject>();
            if (root != null && c.floor > 0)
                foreach (Transform fl in root.transform)
                    if (fl.gameObject.activeSelf && fl.name != $"Floor_{c.floor}F") { fl.gameObject.SetActive(false); hidden.Add(fl.gameObject); }

            Bounds b = new Bounds(Vector3.zero, Vector3.one * 20);
            var target = string.IsNullOrEmpty(c.target) ? root : GameObject.Find(c.target);
            if (root != null && c.floor > 0 && string.IsNullOrEmpty(c.target)) { var f = root.transform.Find($"Floor_{c.floor}F"); if (f) target = f.gameObject; }
            if (target != null)
            {
                bool first = true;
                foreach (var r in target.GetComponentsInChildren<Renderer>())
                {
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                }
            }

            var camGo = new GameObject("__ClaudeShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var lightGo = new GameObject("__ClaudeShotLight") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(c.width, c.height, 24, RenderTextureFormat.ARGB32);
            try
            {
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(55, -35, 0);

                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
                cam.orthographic = true;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 1000f;
                float aspect = (float)c.width / c.height;
                if (c.view == "top")
                {
                    cam.transform.rotation = Quaternion.Euler(90, 0, 0);
                    cam.transform.position = b.center + Vector3.up * 200;
                    cam.orthographicSize = Mathf.Max(b.extents.z, b.extents.x / aspect) * 1.05f;
                }
                else
                {
                    cam.transform.rotation = Quaternion.Euler(45, 45, 0);
                    cam.transform.position = b.center - cam.transform.forward * 300;
                    cam.orthographicSize = Mathf.Max(b.extents.magnitude / aspect, b.extents.magnitude * 0.6f);
                }
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(c.width, c.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, c.width, c.height), 0, 0);
                tex.Apply();
                var file = Path.Combine(Outbox, id + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                return "스크린샷: " + file;
            }
            finally
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(lightGo);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                foreach (var h in hidden) h.SetActive(true);
            }
        }
    }
}
