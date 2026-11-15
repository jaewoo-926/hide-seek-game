using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 2·3층 내부 벽을 처음의 깔끔한 PolygonOffice 벽으로 되돌림 (Upper 없이 기본 높이, 꼭대기 몰딩 유지).
    /// 외곽 벽(Busted_Wall_Plain)과 바닥·방 그룹은 그대로 둠.
    /// arg: "grid.json|patch.json(없으면 빈칸)|루트이름"
    /// </summary>
    public static class BustedInnerRestore
    {
        const string Gen = "Assets/BustedMap/Generated";
        const string WallPath = "Assets/03.Models/Ofiice/PolygonOffice/Prefabs/Buildings/SM_Bld_Wall_Baseboard_01.prefab";
        const string DoorPath = "Assets/03.Models/Ofiice/PolygonOffice/Prefabs/Buildings/SM_Bld_Wall_Baseboard_Door_01.prefab";
        const string FloorPath = "Assets/03.Models/Ofiice/PolygonOffice/Prefabs/Buildings/SM_Bld_Floor_Concrete_01.prefab";
        const float OpenX0 = -1.833f, OpenX1 = -0.667f, OpenTop = 2.049f, Len = 2.5f;
        const int NX = 24, NZ = 16;

        public static string Restore(string arg)
        {
            var p = arg.Split('|');
            string grid = p[0], patch = p[1], rootName = p[2];
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            var off = root.transform.position;

            // 1) 임시 층 생성 (기본 높이 = Upper 없음)
            BuildTemp($"{grid}|__Inner|{off.x}|{off.z}|0|1");
            var temp = GameObject.Find("__Inner");
            if (!string.IsNullOrEmpty(patch))
            {
                BuildTemp($"{patch}|__InnerPatch|{off.x}|{off.z}|0|1");
                var tp = GameObject.Find("__InnerPatch");
                var have = new HashSet<string>();
                foreach (var s in temp.GetComponentsInChildren<Busted.Map.WallSegment>(true)) have.Add(s.name);
                foreach (var s in tp.GetComponentsInChildren<Busted.Map.WallSegment>(true))
                    if (!have.Contains(s.name)) s.transform.SetParent(temp.transform, true);
                UnityEngine.Object.DestroyImmediate(tp);
            }

            // 2) 문 정리(문틀 + 평평한 벽) + 꼭대기 몰딩 복원
            DoorClean(temp.transform);
            RestoreCrown(temp.transform);

            // 3) 기존 내부 벽 목록
            var oldInner = new List<Transform>();
            foreach (var s in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
                if (!IsExterior(s)) oldInner.Add(s.transform);
            var oldByName = new Dictionary<string, Transform>();
            foreach (var t in oldInner) oldByName[t.name] = t;
            var oldBounds = new List<(Bounds b, Transform t)>();
            foreach (var t in oldInner)
            {
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                b.Expand(0.05f);
                oldBounds.Add((b, t));
            }

            // 4) 새 내부 벽을 같은 방 그룹으로 이동
            int moved = 0;
            var newSegs = new List<Busted.Map.WallSegment>(temp.GetComponentsInChildren<Busted.Map.WallSegment>(true));
            foreach (var s in newSegs)
            {
                if (IsExterior(s)) continue;
                Transform room = null;
                string altName = s.isDoor ? s.name.Replace("Door_", "Wall_") : s.name.Replace("Wall_", "Door_");
                if (oldByName.TryGetValue(s.name, out var o) || oldByName.TryGetValue(altName, out o)) room = RoomOf(o, root.transform);
                if (room == null)
                {
                    var c = s.transform.position + Vector3.up * 1.5f;
                    foreach (var ob in oldBounds) if (ob.b.Contains(c)) { room = RoomOf(ob.t, root.transform); break; }
                }
                if (room == null) room = NearestRoom(root.transform, s.transform.position);
                Transform folder = room;
                if (room != root.transform)
                {
                    string fn = s.isDoor ? "Doors" : "Walls";
                    folder = room.Find(fn);
                    if (folder == null) { folder = new GameObject(fn).transform; folder.SetParent(room, false); }
                }
                s.transform.SetParent(folder, true);
                moved++;
            }

            // 5) 기존 내부 벽 삭제, 임시 정리
            foreach (var t in oldInner) if (t != null) UnityEngine.Object.DestroyImmediate(t.gameObject);
            UnityEngine.Object.DestroyImmediate(temp);

            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"{rootName}: 내부 벽 {oldInner.Count}칸 삭제 → 처음의 깔끔한 벽 {moved}칸으로 복원 (외곽 벽은 그대로)";
        }

        static bool IsExterior(Busted.Map.WallSegment s) =>
            s.vertical ? (s.gridK == 0 || s.gridK == NX) : (s.gridJ == 0 || s.gridJ == NZ);

        static Transform RoomOf(Transform seg, Transform root)
        {
            var t = seg.parent;
            while (t != null && t != root && !t.name.StartsWith("Room_")) t = t.parent;
            return t != null && t.name.StartsWith("Room_") ? t : null;
        }

        static Transform NearestRoom(Transform root, Vector3 p)
        {
            Transform best = root; float bd = float.MaxValue;
            foreach (Transform room in root)
            {
                if (!room.name.StartsWith("Room_")) continue;
                var floor = room.Find("Floor");
                if (floor == null) continue;
                foreach (Transform t in floor)
                {
                    float d = (new Vector3(t.position.x - p.x, 0, t.position.z - p.z)).sqrMagnitude;
                    if (d < bd) { bd = d; best = room; }
                }
            }
            return best;
        }

        static void BuildTemp(string buildArg)
        {
            var user = GameObject.Find("example");
            if (user != null) user.name = "example__keep";
            var ex = new GameObject("example");
            Add(ex, WallPath, "SM_Bld_Wall_Baseboard_01", 0f);
            Add(ex, DoorPath, "SM_Bld_Wall_Baseboard_Door_01", 0f);
            Add(ex, FloorPath, "SM_Bld_Floor_Concrete_01", -0.0043f);
            try { BustedGridBuilderV3.Build(buildArg); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ex);
                if (user != null) user.name = "example";
            }
            // 임시 층의 바닥은 필요 없음
            var r = GameObject.Find(buildArg.Split('|')[1]);
            var f = r.transform.Find("Floor");
            if (f != null) UnityEngine.Object.DestroyImmediate(f.gameObject);
        }

        static void DoorClean(Transform root)
        {
            var wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPath);
            var lower = Load("Wall_Lower_NoCrown"); var noTrim = Load("Wall_NoTrim"); var frame = Load("Door_FrameOnly");
            float baseH = wallPrefab.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            foreach (var seg in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
            {
                if (!seg.isDoor) continue;
                var doors = new List<Transform>();
                foreach (Transform ch in seg.transform)
                    if (ch.name.StartsWith("Door") && !ch.name.StartsWith("DoorWall")) doors.Add(ch);
                foreach (var d in doors)
                {
                    d.GetComponent<MeshFilter>().sharedMesh = frame;
                    var mc = d.GetComponent<MeshCollider>(); if (mc != null) mc.sharedMesh = frame;
                    string side = d.name.Length > 4 ? d.name.Substring(4) : "";
                    Piece(wallPrefab, lower, seg.transform, d, "DoorWallL" + side, OpenX0, 0f, (Len + OpenX0) / Len, 1f);
                    Piece(wallPrefab, lower, seg.transform, d, "DoorWallR" + side, 0f, 0f, -OpenX1 / Len, 1f);
                    Piece(wallPrefab, noTrim, seg.transform, d, "DoorWallTop" + side, OpenX1, OpenTop, (OpenX1 - OpenX0) / Len, (baseH - OpenTop) / baseH);
                }
            }
        }

        static void RestoreCrown(Transform root)
        {
            var orig = AssetDatabase.LoadAssetAtPath<GameObject>(WallPath).GetComponent<MeshFilter>().sharedMesh;
            var noBaseboard = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/SM_Bld_Wall_Baseboard_01_NoBaseboard.asset");
            foreach (var seg in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
                foreach (Transform c in seg.transform)
                {
                    var mf = c.GetComponent<MeshFilter>(); if (mf == null) continue;
                    Mesh target = null;
                    if (c.name.StartsWith("Wall") || c.name.StartsWith("DoorWallL") || c.name.StartsWith("DoorWallR")) target = orig;
                    else if (c.name.StartsWith("DoorWallTop") && noBaseboard != null) target = noBaseboard;
                    if (target == null) continue;
                    mf.sharedMesh = target;
                    var mc = c.GetComponent<MeshCollider>(); if (mc != null) mc.sharedMesh = target;
                }
        }

        static Mesh Load(string name) =>
            AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/{name}.asset") ?? throw new Exception($"{Gen}/{name}.asset 이 없습니다");

        static void Piece(GameObject prefab, Mesh mesh, Transform parent, Transform d, string name, float pivotX, float y, float sx, float sy)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.rotation = d.rotation;
            go.transform.position = d.TransformPoint(new Vector3(pivotX, y, 0));
            go.transform.localScale = new Vector3(sx, sy, 1);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mc = go.GetComponent<MeshCollider>(); if (mc != null) mc.sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(d.gameObject));
        }

        static void Add(GameObject parent, string path, string name, float y)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new Exception("프리팹 없음: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            go.name = name;
            go.transform.position = new Vector3(-100, y, -100);
        }
    }
}
