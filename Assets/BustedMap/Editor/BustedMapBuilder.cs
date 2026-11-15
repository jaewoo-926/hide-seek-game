using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Busted.Map.EditorTools
{
    // ───────────── JSON 구조 (extract_map.py 출력과 동일) ─────────────
    [Serializable] public class MapRect { public string type; public float cx, cz, sx, sz; public string axis; public bool exterior; }
    [Serializable] public class MapRoom { public string name; public float area, cx, cz; public MapRect[] zones; }
    [Serializable] public class MapMarkerData { public string kind, id, room; public float x, z; }
    [Serializable] public class MapFloor { public int floor; public float width, depth; public MapRect[] walls, doors, counters; public MapRoom[] rooms; public MapMarkerData[] markers; }
    [Serializable] public class MapData { public int version; public float cell; public MapFloor[] floors; }

    /// <summary>
    /// Busted! 은행 맵 자동 생성기.  메뉴: Busted ▸ Map Builder
    /// 설계도에서 추출한 JSON을 읽어 1~3층 벽/문/바닥/카운터/방 구역/마커를 한 번에 생성한다.
    /// 다시 생성하면 기존 "BustedMap" 오브젝트를 지우고 새로 만든다 (Ctrl+Z 가능).
    /// </summary>
    public class BustedMapBuilder : EditorWindow
    {
        const string RootName = "BustedMap";
        const string BaseDir = "Assets/BustedMap";
        const string SettingsPath = BaseDir + "/BustedMapBuildSettings.asset";
        const string MatDir = BaseDir + "/Generated/Materials";

        BustedMapBuildSettings settings;
        Editor settingsEditor;
        Vector2 scroll;

        [MenuItem("Busted/Map Builder", priority = 0)]
        static void Open() => GetWindow<BustedMapBuilder>("Busted Map Builder");

        void OnEnable()
        {
            settings = AssetDatabase.LoadAssetAtPath<BustedMapBuildSettings>(SettingsPath);
            if (settings == null)
            {
                settings = CreateInstance<BustedMapBuildSettings>();
                EnsureFolder(BaseDir);
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }
            if (settings.mapJson == null)
                settings.mapJson = AssetDatabase.LoadAssetAtPath<TextAsset>(BaseDir + "/BustedMapData.json");
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("1) 설정 확인  2) [맵 생성] 클릭\n프리팹을 비워두면 그레이박스(큐브)로 생성됩니다. 에셋이 준비되면 프리팹만 넣고 다시 생성하세요.", MessageType.Info);
            if (settingsEditor == null || settingsEditor.target != settings)
                settingsEditor = Editor.CreateEditor(settings);
            settingsEditor.OnInspectorGUI();
            EditorGUILayout.Space(10);

            using (new EditorGUI.DisabledScope(settings.mapJson == null))
            {
                GUI.backgroundColor = new Color(0.5f, 0.9f, 0.6f);
                if (GUILayout.Button("맵 생성 (기존 BustedMap 교체)", GUILayout.Height(36))) Build(settings);
                GUI.backgroundColor = Color.white;
            }
            if (GUILayout.Button("생성된 맵 삭제")) Clear();
            EditorGUILayout.EndScrollView();
        }

        // ─────────────────────────── 빌드 ───────────────────────────
        public static void Build(BustedMapBuildSettings s)
        {
            var data = JsonUtility.FromJson<MapData>(s.mapJson.text);
            if (data?.floors == null || data.floors.Length == 0) { Debug.LogError("[BustedMap] JSON에 층 데이터가 없습니다."); return; }

            Clear();
            var mats = new MatCache(s);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Busted Map");
            int wallLayer = string.IsNullOrEmpty(s.wallLayerName) ? 0 : Mathf.Max(0, LayerMask.NameToLayer(s.wallLayerName));
            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            int count = 0;

            try
            {
                for (int i = 0; i < data.floors.Length; i++)
                {
                    var f = data.floors[i];
                    EditorUtility.DisplayProgressBar("Busted Map Builder", $"{f.floor}층 생성 중...", (float)i / data.floors.Length);
                    float sc = s.mapScale;
                    var floorRoot = new GameObject($"Floor_{f.floor}F");
                    floorRoot.transform.SetParent(root.transform, false);
                    floorRoot.transform.localPosition = s.layout == FloorLayout.Stacked
                        ? new Vector3(0, i * s.floorHeight, 0)
                        : new Vector3(i * (f.width * sc + s.sideBySideGap), 0, 0);

                    Vector3 P(float x, float z, float y = 0) => new Vector3((x - f.width / 2f) * sc, y, (z - f.depth / 2f) * sc);

                    // 바닥 (베이스 슬래브)
                    var geo = Child(floorRoot, "Geometry");
                    var slab = Cube(geo, "FloorSlab", P(f.width / 2, f.depth / 2, -0.1f), new Vector3(f.width * sc, 0.2f, f.depth * sc), mats.Floor);
                    MakeStatic(slab);

                    // 벽
                    var walls = Child(geo, "Walls");
                    int wi = 0;
                    foreach (var w in f.walls)
                    {
                        var size = new Vector3(w.sx * sc, s.wallHeight, w.sz * sc);
                        var go = new GameObject($"Wall_{wi++:000}");
                        go.transform.SetParent(walls.transform, false);
                        go.transform.localPosition = P(w.cx, w.cz, s.wallHeight / 2f);
                        go.AddComponent<BoxCollider>().size = size;
                        go.layer = wallLayer;
                        if (s.wallPrefab != null) PlaceWallPrefab(s, go, size, wallLayer);
                        else { var v = Cube(go, "Visual", Vector3.zero, size, mats.Wall, collider: false); v.layer = wallLayer; MakeStatic(v); }
                        MakeStatic(go);
                        count++;
                    }

                    // 카운터 (1층 안내 창구)
                    if (f.counters != null && f.counters.Length > 0)
                    {
                        var counters = Child(geo, "Counters");
                        foreach (var c in f.counters)
                        {
                            var size = new Vector3(c.sx * sc, s.counterHeight, c.sz * sc);
                            var go = new GameObject("CounterPiece");
                            go.transform.SetParent(counters.transform, false);
                            go.transform.localPosition = P(c.cx, c.cz, s.counterHeight / 2f);
                            go.AddComponent<BoxCollider>().size = size;
                            if (s.counterPrefab == null) Cube(go, "Visual", Vector3.zero, size, mats.Counter, collider: false);
                            MakeStatic(go);
                        }
                        if (s.counterPrefab != null)
                        {
                            // 카운터 전체 범위에 프리팹 1개를 맞춰 배치
                            var b = new Bounds(); bool first = true;
                            foreach (var c in f.counters)
                            {
                                var cb = new Bounds(P(c.cx, c.cz, s.counterHeight / 2f), new Vector3(c.sx * sc, s.counterHeight, c.sz * sc));
                                if (first) { b = cb; first = false; } else b.Encapsulate(cb);
                            }
                            FitPrefab(s.counterPrefab, counters.transform, b.center, b.size, 0f, true);
                        }
                    }

                    // 문
                    var doors = Child(geo, "Doors");
                    int di = 0;
                    foreach (var d in f.doors)
                    {
                        bool alongX = d.axis == "x";
                        float rotY = alongX ? 0f : 90f;
                        float doorWidth = (alongX ? d.sx : d.sz) * sc;
                        var go = new GameObject($"Door_{di:00}{(d.exterior ? "_Exterior" : "")}");
                        go.transform.SetParent(doors.transform, false);
                        go.transform.localPosition = P(d.cx, d.cz);
                        go.transform.localRotation = Quaternion.Euler(0, rotY, 0);
                        if (s.doorPrefab != null)
                        {
                            var size = new Vector3(doorWidth, s.wallHeight, 0.3f);
                            var holder = new GameObject("DoorVisual");
                            holder.transform.SetParent(go.transform, false);
                            holder.transform.localPosition = new Vector3(0, s.wallHeight / 2f, 0);
                            FitPrefabLocal(s.doorPrefab, holder.transform, size, s.scaleDoorPrefabToFit);
                        }
                        if (s.generateDoorMarkers)
                        {
                            var m = go.AddComponent<MapMarker>();
                            m.kind = MapMarkerKind.Door; m.id = $"F{f.floor}_Door{di:00}"; m.floor = f.floor;
                        }
                        // 1층 외부 출입문 = 탈출구 후보
                        if (d.exterior && f.floor == 1)
                        {
                            var ex = new GameObject($"ExitCandidate_{di:00}");
                            ex.transform.SetParent(go.transform, false);
                            var m = ex.AddComponent<MapMarker>();
                            m.kind = MapMarkerKind.ExitCandidate; m.id = $"Exit_{di:00}"; m.floor = f.floor;
                        }
                        di++;
                    }

                    // 방 바닥 색 + 방 구역 트리거
                    if (s.generateRoomFloorTint || s.generateRoomZones)
                    {
                        var roomsRoot = Child(floorRoot, "Rooms");
                        foreach (var r in f.rooms)
                        {
                            var rgo = new GameObject($"Room_{r.name}");
                            rgo.transform.SetParent(roomsRoot.transform, false);
                            rgo.transform.localPosition = P(r.cx, r.cz);
                            if (s.generateRoomZones)
                            {
                                var rz = rgo.AddComponent<RoomZone>();
                                rz.floor = f.floor; rz.roomName = r.name; rz.area = r.area;
                            }
                            int zi = 0;
                            foreach (var z in r.zones)
                            {
                                var center = P(z.cx, z.cz) - rgo.transform.localPosition;
                                if (s.generateRoomFloorTint)
                                {
                                    var t = Cube(rgo, $"Tint_{zi}", center + Vector3.up * 0.005f, new Vector3(z.sx * sc, 0.01f, z.sz * sc), mats.Room(r.name), collider: false);
                                    MakeStatic(t);
                                }
                                if (s.generateRoomZones)
                                {
                                    var zgo = new GameObject($"Zone_{zi}");
                                    zgo.transform.SetParent(rgo.transform, false);
                                    zgo.transform.localPosition = center + Vector3.up * (s.wallHeight / 2f);
                                    var bc = zgo.AddComponent<BoxCollider>();
                                    bc.isTrigger = true;
                                    bc.size = new Vector3(z.sx * sc, s.wallHeight, z.sz * sc);
                                    if (ignoreRaycast >= 0) zgo.layer = ignoreRaycast;
                                }
                                zi++;
                            }
                        }
                    }

                    // 마커
                    if (s.generateMarkers && f.markers != null)
                    {
                        var mroot = Child(floorRoot, "Markers");
                        var groups = new Dictionary<string, GameObject>();
                        foreach (var md in f.markers)
                        {
                            if (!Enum.TryParse(md.kind, out MapMarkerKind kind)) continue;
                            if (!groups.TryGetValue(md.kind, out var g)) groups[md.kind] = g = Child(mroot, md.kind);
                            var go = new GameObject(md.id);
                            go.transform.SetParent(g.transform, false);
                            go.transform.localPosition = P(md.x, md.z);
                            var m = go.AddComponent<MapMarker>();
                            m.kind = kind; m.id = md.id; m.floor = f.floor; m.room = md.room;
                        }
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[BustedMap] 생성 완료: {data.floors.Length}개 층, 벽 {count}개. 스케일 {s.mapScale}, 배치 {s.layout}");
        }

        public static void Clear()
        {
            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }

        // ─────────────────────────── 프리팹 배치 ───────────────────────────
        static void PlaceWallPrefab(BustedMapBuildSettings s, GameObject holder, Vector3 size, int layer)
        {
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z;
            float thick = alongX ? size.z : size.x;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / Mathf.Max(0.1f, s.wallSegmentLength)));
            float seg = length / n;
            for (int k = 0; k < n; k++)
            {
                float offset = -length / 2f + seg * (k + 0.5f);
                var piece = new GameObject($"Seg_{k}");
                piece.transform.SetParent(holder.transform, false);
                piece.transform.localPosition = alongX ? new Vector3(offset, 0, 0) : new Vector3(0, 0, offset);
                piece.transform.localRotation = Quaternion.Euler(0, alongX ? 0 : 90, 0);
                var inst = FitPrefabLocal(s.wallPrefab, piece.transform, new Vector3(seg, size.y, thick), true);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                // 프리팹 자체 콜라이더는 끔 (부모 BoxCollider가 정확한 충돌 담당)
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }
        }

        /// <summary>부모 로컬 공간에서, 중심 (0,0,0)·크기 size 박스에 프리팹을 맞춘다.</summary>
        static GameObject FitPrefabLocal(GameObject prefab, Transform parent, Vector3 size, bool stretch)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            var b = LocalBounds(inst, parent);
            if (b.size.sqrMagnitude < 1e-6f) return inst;
            Vector3 k = new Vector3(SafeDiv(size.x, b.size.x), SafeDiv(size.y, b.size.y), SafeDiv(size.z, b.size.z));
            if (!stretch) { float u = Mathf.Min(k.x, k.y); k = new Vector3(u, u, u); }
            inst.transform.localScale = k;
            b = LocalBounds(inst, parent);
            // 바닥(y 최솟값)을 박스 바닥에, XZ 중심을 박스 중심에
            inst.transform.localPosition += new Vector3(-b.center.x, -size.y / 2f - b.min.y, -b.center.z);
            MakeStatic(inst);
            return inst;
        }

        static void FitPrefab(GameObject prefab, Transform parent, Vector3 localCenter, Vector3 size, float rotY, bool stretch)
        {
            var holder = new GameObject(prefab.name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localCenter;
            holder.transform.localRotation = Quaternion.Euler(0, rotY, 0);
            FitPrefabLocal(prefab, holder.transform, size, stretch);
        }

        static Bounds LocalBounds(GameObject go, Transform space)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = new Bounds(); bool first = true;
            foreach (var r in rs)
            {
                var wb = r.bounds;
                var c = wb.center; var e = wb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var p = space.InverseTransformPoint(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static float SafeDiv(float a, float b) => b > 1e-4f ? a / b : 1f;

        // ─────────────────────────── 유틸 ───────────────────────────
        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static GameObject Cube(GameObject parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void MakeStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ─────────────────────────── 머티리얼 ───────────────────────────
        class MatCache
        {
            readonly BustedMapBuildSettings s;
            readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
            public MatCache(BustedMapBuildSettings s) { this.s = s; EnsureFolder(MatDir); }

            public Material Wall => s.wallMaterial ? s.wallMaterial : Get("M_Wall", new Color(0.22f, 0.23f, 0.26f));
            public Material Floor => s.floorMaterial ? s.floorMaterial : Get("M_FloorBase", new Color(0.55f, 0.55f, 0.55f));
            public Material Counter => s.counterMaterial ? s.counterMaterial : Get("M_Counter", new Color(0.45f, 0.33f, 0.22f));

            public Material Room(string name)
            {
                if (Has(name, "금고", "보석")) return Get("M_Room_Vault", new Color(0.85f, 0.72f, 0.35f));
                if (Has(name, "화장실")) return Get("M_Room_Toilet", new Color(0.62f, 0.75f, 0.85f));
                if (Has(name, "창고", "비품")) return Get("M_Room_Storage", new Color(0.66f, 0.56f, 0.45f));
                if (Has(name, "계단", "엘리베이터")) return Get("M_Room_FloorLink", new Color(0.45f, 0.72f, 0.66f));
                if (Has(name, "CCTV", "보안", "관제")) return Get("M_Room_Security", new Color(0.78f, 0.48f, 0.48f));
                if (Has(name, "로비", "복도")) return Get("M_Room_Hall", new Color(0.82f, 0.81f, 0.78f));
                if (Has(name, "ATM", "서류", "대기")) return Get("M_Room_Customer", new Color(0.66f, 0.8f, 0.62f));
                return Get("M_Room_Office", new Color(0.86f, 0.78f, 0.66f));
            }

            static bool Has(string n, params string[] keys) { foreach (var k in keys) if (n.Contains(k)) return true; return false; }

            Material Get(string name, Color color)
            {
                if (cache.TryGetValue(name, out var m)) return m;
                string path = $"{MatDir}/{name}.mat";
                m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(DefaultShader()) { name = name };
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
                    AssetDatabase.CreateAsset(m, path);
                }
                cache[name] = m;
                return m;
            }

            static Shader DefaultShader()
            {
                if (GraphicsSettings.currentRenderPipeline != null)
                {
                    var urp = Shader.Find("Universal Render Pipeline/Lit");
                    if (urp != null) return urp;
                    var hdrp = Shader.Find("HDRP/Lit");
                    if (hdrp != null) return hdrp;
                }
                return Shader.Find("Standard");
            }
        }
    }
}
