using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    /// <summary>화장실 문 / 엘리베이터 공통 작업 (1·2·3층)</summary>
    public static class BustedCommonRooms
    {
        const float G = 2.5f, H = 3f;
        const string ToiletPrefab = "Assets/BustedMap/Prefabs/화장실 문.prefab";
        const string ElevSet = "Assets/BustedMap/elevator_set.json";

        // ───────── 화장실 문 ─────────
        /// arg: "문짝1|문짝2" — 두 문짝을 "화장실 문" 하나로 묶고 프리팹으로 저장 (기준점 = 두 문짝 가운데 바닥)
        public static string MakeToiletDoor(string arg)
        {
            var names = arg.Split('|');
            var parts = names.Select(n => GameObject.Find(n) ?? throw new Exception("없음: " + n)).ToList();
            Bounds b = default; bool first = true;
            foreach (var p in parts) foreach (var r in p.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            var g = GameObject.Find("화장실 문") ?? new GameObject("화장실 문");
            g.transform.SetParent(parts[0].transform.parent, true);
            g.transform.SetPositionAndRotation(new Vector3(b.center.x, b.min.y, b.center.z), Quaternion.identity);
            foreach (var p in parts) p.transform.SetParent(g.transform, true);
            if (!AssetDatabase.IsValidFolder("Assets/BustedMap/Prefabs")) AssetDatabase.CreateFolder("Assets/BustedMap", "Prefabs");
            PrefabUtility.SaveAsPrefabAssetAndConnect(g, ToiletPrefab, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(g.scene); EditorSceneManager.SaveScene(g.scene);
            return $"화장실 문 프리팹 저장: 폭 {b.size.x:0.##}m, 높이 {b.size.y:0.##}m (문짝 바닥을 기준점으로)";
        }

        static bool Touches(DoorUnit du, Transform map, Func<Transform, bool> pred)
        {
            var cells = BustedWallSystem.CellRooms(map);
            foreach (var s in new[] { -0.6f, 0.6f })
            {
                var p = du.transform.position + du.transform.forward * s - map.position;
                if (cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / G), Mathf.FloorToInt(p.z / G)), out var r) && pred(r)) return true;
            }
            return false;
        }

        /// arg: 루트 — 화장실에 닿은 문들의 문짝을 "화장실 문" 프리팹으로 교체 (문 위치 그대로, 폭/높이/문틀 맞춤)
        public static string ApplyToiletDoors(string arg)
        {
            var ap = arg.Split('|'); string root = ap[0];
            var map = GameObject.Find(root).transform;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ToiletPrefab) ?? throw new Exception("화장실 문 프리팹 없음");
            var sb = new StringBuilder(); int n = 0;
            // 기준: 화장실이 아닌 문 중 원래 유리 양문(DoorLeaf_*)의 폭·높이
            Vector2? target = null; Vector3 lastScale = Vector3.one;
            if (ap.Length >= 3) target = new Vector2(float.Parse(ap[1], CultureInfo.InvariantCulture), float.Parse(ap[2], CultureInfo.InvariantCulture));
            foreach (var du0 in map.GetComponentsInChildren<DoorUnit>(true))
            {
                if (target.HasValue) break;
                var leaves = du0.transform.Cast<Transform>().Where(c => c.name.StartsWith("DoorLeaf")).ToList();
                if (leaves.Count != 2) continue;   // 양문 한 세트(문짝 2장)만 기준으로
                Bounds gb = default; bool f0 = true;
                foreach (var r in leaves.SelectMany(l => l.GetComponentsInChildren<MeshRenderer>()))
                {
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var cc = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var p = du0.transform.InverseTransformPoint(r.transform.TransformPoint(cc));
                        if (f0) { gb = new Bounds(p, Vector3.zero); f0 = false; } else gb.Encapsulate(p);
                    }
                }
                if (!f0) { target = new Vector2(gb.size.x, gb.max.y); sb.Append($"(기준 유리문 {du0.name}: 폭 {gb.size.x:0.##}m, 높이 {gb.max.y:0.##}m)"); break; }
            }
            foreach (var du in map.GetComponentsInChildren<DoorUnit>(true))
            {
                if (!Touches(du, map, r => r.name.Contains("화장실"))) continue;
                foreach (var c in du.transform.Cast<Transform>().Where(c => c.name != "Frame").ToList()) UnityEngine.Object.DestroyImmediate(c.gameObject);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, du.transform);
                inst.name = "화장실 문";
                inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
                foreach (var col in inst.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(col);
                // 문짝 크기
                Bounds lb = default; bool first = true; Material mat = null; Vector2 uv = Vector2.zero;
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    var m = mf.sharedMesh; var mb = m.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var cc = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var p = du.transform.InverseTransformPoint(r.transform.TransformPoint(cc));
                        if (first) { lb = new Bounds(p, Vector3.zero); first = false; } else lb.Encapsulate(p);
                    }
                    if (mat == null)
                    {
                        mat = r.sharedMaterial;
                        var vs = m.vertices; var us = m.uv; int bi = 0;
                        for (int q = 1; q < vs.Length; q++) if (vs[q].y > vs[bi].y) bi = q;
                        if (us.Length == vs.Length) uv = us[bi];
                    }
                }
                // 원래 유리 양문과 같은 크기로 (폭·높이)
                if (target.HasValue)
                {
                    float sx = target.Value.x / lb.size.x, sy = target.Value.y / Mathf.Max(lb.max.y, 0.01f);
                    inst.transform.localScale = new Vector3(sx, sy, 1);
                    lb = new Bounds(new Vector3(lb.center.x * sx, lb.center.y * sy, lb.center.z), new Vector3(lb.size.x * sx, lb.size.y * sy, lb.size.z));
                    lastScale = new Vector3(sx, sy, 1);
                }
                // 문짝 가운데를 문 기준점에 (문 위치는 그대로, 문짝만 가운데로)
                inst.transform.localPosition = new Vector3(-lb.center.x, 0, 0);
                float w = lb.size.x + 0.04f, h = lb.max.y + 0.03f;
                du.width = w; du.height = h; du.pairs = 1;
                var fr = du.transform.Find("Frame");
                if (fr == null) { fr = new GameObject("Frame").transform; fr.SetParent(du.transform, false); fr.gameObject.AddComponent<MeshFilter>(); fr.gameObject.AddComponent<MeshRenderer>(); }
                fr.localPosition = Vector3.zero; fr.localRotation = Quaternion.identity;
                fr.GetComponent<MeshFilter>().sharedMesh = BoxMesh($"ToiletFrame_{w:0.000}_{h:0.000}", Frame(w, h), uv);
                if (mat != null) fr.GetComponent<MeshRenderer>().sharedMaterial = mat;
                n++; sb.Append($" {du.name}");
            }
            if (target.HasValue && n > 0)
            {
                // 프리팹 자체도 같은 크기로 (나중에 새로 놓을 때도 같은 크기)
                var pc = PrefabUtility.LoadPrefabContents(ToiletPrefab);
                pc.transform.localScale = lastScale;
                PrefabUtility.SaveAsPrefabAsset(pc, ToiletPrefab);
                PrefabUtility.UnloadPrefabContents(pc);
            }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{root}: 화장실 문 {n}개 교체 {sb}";
        }

        /// arg: 프리팹 경로 — 프리팹 루트의 위치/회전을 0으로 (크기는 그대로). 오버라이드 없는 인스턴스가 문 자리로 돌아옴
        public static string FixPrefabRoot(string path)
        {
            var pc = PrefabUtility.LoadPrefabContents(path);
            var old = $"pos={pc.transform.localPosition} rot={pc.transform.localEulerAngles} scale={pc.transform.localScale}";
            pc.transform.localPosition = Vector3.zero; pc.transform.localRotation = Quaternion.identity;
            PrefabUtility.SaveAsPrefabAsset(pc, path);
            PrefabUtility.UnloadPrefabContents(pc);
            return $"{path}: 루트 {old} → pos=0, rot=0";
        }

        /// arg: "루트|프리팹경로" — 그 프리팹 문짝이 문틀 가운데(바닥 높이)에서 벗어나 있으면 문틀 가운데로 되돌림 (제자리인 문은 그대로)
        public static string RecenterLeaves(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p[1]);
            var sb = new StringBuilder(); int n = 0;
            foreach (var du in map.GetComponentsInChildren<DoorUnit>(true))
                foreach (Transform c in du.transform)
                {
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(c.gameObject) || PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject) != prefab) continue;
                    Bounds lb = default; bool first = true;
                    foreach (var r in c.GetComponentsInChildren<MeshRenderer>())
                    {
                        var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                        var mb = mf.sharedMesh.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            var cc = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                            var q = du.transform.InverseTransformPoint(r.transform.TransformPoint(cc));
                            if (first) { lb = new Bounds(q, Vector3.zero); first = false; } else lb.Encapsulate(q);
                        }
                    }
                    var off = new Vector3(lb.center.x, lb.min.y, lb.center.z);
                    if (off.magnitude < 0.02f) continue;
                    c.localPosition -= off;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                    n++; sb.Append($" {du.name}(이동 {off.magnitude:0.##}m)");
                }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{p[0]}: 문틀 가운데로 되돌린 문짝 {n}개{sb}";
        }

        /// arg: "부품1|부품2|프리팹경로" — 씬의 부품들을 복제해 하나의 문 프리팹으로 저장 (원본은 그대로 둠, 기준점 = 바닥 가운데)
        public static string MakeDoorPrefab(string arg)
        {
            var p = arg.Split('|'); var path = p[p.Length - 1];
            var parts = p.Take(p.Length - 1).Select(n => GameObject.Find(n) ?? throw new Exception("없음: " + n)).ToList();
            Bounds b = default; bool first = true;
            foreach (var g in parts) foreach (var r in g.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            var pivot = new Vector3(b.center.x, b.min.y, b.center.z);
            var tmp = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            foreach (var g in parts)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(g);
                var c = src != null ? (GameObject)PrefabUtility.InstantiatePrefab(src) : UnityEngine.Object.Instantiate(g);
                c.name = g.name;
                c.transform.SetPositionAndRotation(g.transform.position - pivot, g.transform.rotation);
                c.transform.localScale = g.transform.lossyScale;
                c.transform.SetParent(tmp.transform, true);
            }
            PrefabUtility.SaveAsPrefabAsset(tmp, path);
            UnityEngine.Object.DestroyImmediate(tmp);
            return $"{path} 저장: 폭 {b.size.x:0.##}m, 높이 {b.size.y:0.##}m (원본 {string.Join(", ", parts.Select(x => x.name))} 은 그대로)";
        }

        /// arg: "루트|문이름|프리팹경로|폭|높이" — 지정한 문(DoorUnit) 하나의 문짝만 프리팹으로 교체 (문 위치 그대로, 크기·문틀 맞춤)
        public static string ApplyDoorPrefab(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform;
            var du = map.GetComponentsInChildren<DoorUnit>(true).FirstOrDefault(d => d.name == p[1]) ?? throw new Exception("문 없음: " + p[1]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p[2]) ?? throw new Exception("프리팹 없음: " + p[2]);
            var target = new Vector2(F(p[3]), F(p[4]));
            var removed = du.transform.Cast<Transform>().Where(c => c.name != "Frame").Select(c => c.name).ToList();
            foreach (var c in du.transform.Cast<Transform>().Where(c => c.name != "Frame").ToList()) UnityEngine.Object.DestroyImmediate(c.gameObject);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, du.transform);
            inst.name = prefab.name;
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            foreach (var col in inst.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            Bounds lb = default; bool first = true; Material mat = null; Vector2 uv = Vector2.zero;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var m = mf.sharedMesh; var mb = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var cc = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var q = du.transform.InverseTransformPoint(r.transform.TransformPoint(cc));
                    if (first) { lb = new Bounds(q, Vector3.zero); first = false; } else lb.Encapsulate(q);
                }
                if (mat == null)
                {
                    mat = r.sharedMaterial;
                    var vs = m.vertices; var us = m.uv; int bi = 0;
                    for (int q = 1; q < vs.Length; q++) if (vs[q].y > vs[bi].y) bi = q;
                    if (us.Length == vs.Length) uv = us[bi];
                }
            }
            float sx = target.x / lb.size.x, sy = target.y / Mathf.Max(lb.max.y, 0.01f);
            inst.transform.localScale = new Vector3(sx, sy, 1);
            inst.transform.localPosition = new Vector3(-lb.center.x * sx, 0, 0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(inst.transform);
            float w = target.x + 0.04f, h = target.y + 0.03f;
            du.width = w; du.height = h; du.pairs = 1;
            var fr = du.transform.Find("Frame");
            fr.GetComponent<MeshFilter>().sharedMesh = BoxMesh($"ToiletFrame_{w:0.000}_{h:0.000}", Frame(w, h), uv);
            if (mat != null) fr.GetComponent<MeshRenderer>().sharedMaterial = mat;
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{p[0]}/{du.name}: 문짝 {string.Join(",", removed)} → {prefab.name} (폭 {target.x}m × 높이 {target.y}m)";
        }

        static List<(Vector3 mn, Vector3 mx)> Frame(float w, float h)
        {
            const float fw = 0.08f, dz = 0.22f;
            return new List<(Vector3, Vector3)> {
                (new Vector3(-w / 2 - fw, 0, -dz), new Vector3(-w / 2, h + fw, dz)),
                (new Vector3(w / 2, 0, -dz), new Vector3(w / 2 + fw, h + fw, dz)),
                (new Vector3(-w / 2 - fw, h, -dz), new Vector3(w / 2 + fw, h + fw, dz)) };
        }

        static Mesh BoxMesh(string name, List<(Vector3 mn, Vector3 mx)> boxes, Vector2 uv)
        {
            string dir = "Assets/BustedMap/Generated/Faces";
            string path = $"{dir}/{name}_{uv.x:0.0000}_{uv.y:0.0000}.asset".Replace(",", ".");
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (ex != null) return ex;
            var P = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var I = new List<int>();
            foreach (var (mn, mx) in boxes)
            {
                var c = (mn + mx) / 2; var e = (mx - mn) / 2;
                foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                {
                    Vector3 a = n.x != 0 ? Vector3.up : Vector3.right, bb = Vector3.Cross(n, a);
                    int s = P.Count;
                    foreach (var (sa, sb2) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                    { P.Add(c + Vector3.Scale(n, e) + Vector3.Scale(a * sa, e) + Vector3.Scale(bb * sb2, e)); N.Add(n); U.Add(uv); }
                    if (Vector3.Dot(Vector3.Cross(P[s + 1] - P[s], P[s + 2] - P[s]), n) > 0) I.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                    else I.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                }
            }
            var m = new Mesh { name = name };
            m.SetVertices(P); m.SetNormals(N); m.SetUVs(0, U); m.SetTriangles(I, 0); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static float F(string x) => float.Parse(x, CultureInfo.InvariantCulture);

        // ───────── 엘리베이터 ─────────
        static (Vector2 min, Vector2 max) RoomRect(Transform room, Vector3 o)
        {
            var f = room.Find("Floor");
            float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
            foreach (Transform t in f)
            {
                var r = t.GetComponent<Renderer>(); if (r == null) continue;
                var c = r.bounds.center - o;
                x0 = Mathf.Min(x0, c.x - G / 2); x1 = Mathf.Max(x1, c.x + G / 2); z0 = Mathf.Min(z0, c.z - G / 2); z1 = Mathf.Max(z1, c.z + G / 2);
            }
            return (new Vector2(x0, z0), new Vector2(x1, z1));
        }

        static Transform ElevRoom(Transform map) => map.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Room_") && t.name.Contains("엘리베이터")) ?? throw new Exception("Room_엘리베이터 없음");

        /// arg: 루트 — (1층) 엘리베이터 방 근처의 엘리베이터/버튼을 Room_엘리베이터/Elevators 로 모으고, 엘리베이터에 WallOpening 추가, 엘리베이터 방 문 삭제, 배치를 파일로 저장
        public static string CollectElevators(string root)
        {
            var map = GameObject.Find(root).transform; var o = map.position;
            var room = ElevRoom(map);
            var (mn, mx) = RoomRect(room, o);
            var folder = room.Find("Elevators") ?? new GameObject("Elevators").transform;
            folder.SetParent(room, false); folder.localPosition = Vector3.zero; folder.localRotation = Quaternion.identity;
            var list = new List<string>(); var lines = new List<string>();
            foreach (var go in room.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList())
            {
                if (go == null || !PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
                if (src == null || !(src.name.Contains("Elevator"))) continue;
                var p = go.transform.position - o;
                if (p.x < mn.x - 1.5f || p.x > mx.x + 1.5f || p.y > 5 || p.z < mn.y - 1.5f || p.z > mx.y + 1.5f) continue;
                go.transform.SetParent(folder, true);
                bool isElev = src.name.StartsWith("SM_Bld_Elevator");
                if (isElev && go.GetComponent<WallOpening>() == null) go.AddComponent<WallOpening>();
                list.Add(go.name);
                var rel = go.transform.position - o;
                var ci = CultureInfo.InvariantCulture;
                lines.Add(string.Join("|", AssetDatabase.GetAssetPath(src), go.name, rel.x.ToString("R", ci), rel.y.ToString("R", ci), rel.z.ToString("R", ci), go.transform.eulerAngles.y.ToString("R", ci), isElev ? "1" : "0"));
            }
            int del = 0;
            foreach (var du in map.GetComponentsInChildren<DoorUnit>(true).ToList())
                if (Touches(du, map, r => r == room)) { UnityEngine.Object.DestroyImmediate(du.gameObject); del++; }
            File.WriteAllLines(ElevSet, lines);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{root}: 엘리베이터 관련 {list.Count}개 정리 ({string.Join(", ", list)}), 엘리베이터 방 문 {del}개 삭제";
        }

        /// arg: 루트 — (2·3층) 1층과 같은 위치에 엘리베이터/버튼 배치 + 엘리베이터 방 문 삭제
        public static string PlaceElevators(string root)
        {
            var map = GameObject.Find(root).transform; var o = map.position;
            var room = ElevRoom(map);
            var folder = room.Find("Elevators");
            if (folder != null) UnityEngine.Object.DestroyImmediate(folder.gameObject);
            folder = new GameObject("Elevators").transform; folder.SetParent(room, false);
            int n = 0;
            foreach (var line in File.ReadAllLines(ElevSet))
            {
                var p = line.Split('|');
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(p[0]); if (src == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src, folder);
                go.name = p[1];
                go.transform.SetPositionAndRotation(o + new Vector3(F(p[2]), F(p[3]), F(p[4])), Quaternion.Euler(0, F(p[5]), 0));
                if (p[6] == "1") go.AddComponent<WallOpening>();
                n++;
            }
            int del = 0;
            foreach (var du in map.GetComponentsInChildren<DoorUnit>(true).ToList())
                if (Touches(du, map, r => r == room)) { UnityEngine.Object.DestroyImmediate(du.gameObject); del++; }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{root}: 엘리베이터 관련 {n}개 배치, 엘리베이터 방 문 {del}개 삭제";
        }

        /// arg: 루트 — 엘리베이터 방 안에서 엘리베이터가 차지하지 않는 빈 공간을 벽 덩어리로 채움
        public static string FillElevatorRoom(string root)
        {
            var map = GameObject.Find(root).transform; var o = map.position;
            var room = ElevRoom(map);
            var (mn, mx) = RoomRect(room, o);
            var old = room.Find("Fill"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var fill = new GameObject("Fill").transform; fill.SetParent(room, false); fill.localPosition = Vector3.zero;
            var elevs = room.GetComponentsInChildren<WallOpening>(true).ToList();
            if (elevs.Count == 0) return "엘리베이터 없음";
            // 엘리베이터 앞면 줄 / 방향 (가로벽 기준)
            var bs = elevs.Select(e => { var rs = e.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return (e, b); }).ToList();
            var fwd = elevs[0].transform.forward;
            bool v = Mathf.Abs(fwd.x) > Mathf.Abs(fwd.z);
            // 벽 재질 (이 방 벽면 중 하나)
            MeshRenderer wallR = BustedWallSystem.AllFaces(map).Where(x => x.room == room).Select(x => x.r).FirstOrDefault()
                              ?? room.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name.StartsWith("Face_"));
            Material mat = wallR != null ? wallR.sharedMaterial : null;
            Vector2 uv = Vector2.zero;
            if (wallR != null)
            {
                var m = wallR.GetComponent<MeshFilter>().sharedMesh; var vs = m.vertices; var ns = m.normals; var us = m.uv; int bi = -1;
                for (int q = 0; q < vs.Length; q++) if (ns.Length == vs.Length && ns[q].y > 0.9f && (bi < 0 || vs[q].y > vs[bi].y)) bi = q;
                if (bi < 0) { bi = 0; for (int q = 1; q < vs.Length; q++) if (vs[q].y > vs[bi].y) bi = q; }
                if (us.Length == vs.Length) uv = us[bi];
            }
            // 벽 방향 구간에서 엘리베이터가 없는 부분
            float a0 = v ? mn.y : mn.x, a1 = v ? mx.y : mx.x;
            var occ = bs.Select(x => v ? (x.b.min.z - o.z, x.b.max.z - o.z) : (x.b.min.x - o.x, x.b.max.x - o.x)).OrderBy(x => x.Item1).ToList();
            float frontLine = bs.Average(x => v ? (fwd.x < 0 ? x.b.min.x : x.b.max.x) - o.x : (fwd.z < 0 ? x.b.min.z : x.b.max.z) - o.z);
            float backLine = v ? (fwd.x < 0 ? mx.x : mn.x) : (fwd.z < 0 ? mx.y : mn.y);
            var gaps = new List<(float, float)>(); float cur = a0;
            foreach (var (s, e) in occ) { if (s - cur > 0.05f) gaps.Add((cur, s)); cur = Mathf.Max(cur, e); }
            if (a1 - cur > 0.05f) gaps.Add((cur, a1));
            int n = 0;
            foreach (var (s, e) in gaps)
            {
                float d0 = Mathf.Min(frontLine, backLine), d1 = Mathf.Max(frontLine, backLine);
                Vector3 bmin = v ? new Vector3(d0, 0, s) : new Vector3(s, 0, d0), bmax = v ? new Vector3(d1, H - 0.01f, e) : new Vector3(e, H - 0.01f, d1);
                var go = new GameObject($"Fill_{n}");
                go.transform.SetParent(fill, false);
                go.transform.position = o + (bmin + bmax) / 2;
                var size = bmax - bmin;
                go.AddComponent<MeshFilter>().sharedMesh = BoxMesh($"ElevFill_{size.x:0.000}_{size.y:0.000}_{size.z:0.000}", new List<(Vector3, Vector3)> { (-size / 2, size / 2) }, uv);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.AddComponent<BoxCollider>();
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                n++;
            }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{root}: 엘리베이터 {elevs.Count}대, 빈 공간 {n}곳 벽으로 채움 ({string.Join(", ", gaps.Select(g => $"{g.Item1:0.##}~{g.Item2:0.##}"))})";
        }
    }
}
