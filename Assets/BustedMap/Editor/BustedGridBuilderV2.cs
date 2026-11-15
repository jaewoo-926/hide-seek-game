using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 2.5m 격자 데이터(F1_grid.json)를 읽어, 씬의 "example" 방에 쓰인
    /// 벽 / 문벽 / 바닥 프리팹만으로 층을 조립한다.
    /// 호출: ClaudeBridge invoke  type=Busted.Map.EditorTools.BustedGridBuilder method=Build arg=json경로
    /// </summary>
    public static class BustedGridBuilderV2
    {
        [Serializable] class Edge { public string o; public int k, j; public string t; }
        [Serializable] class Grid { public float grid; public int nx, nz; public Edge[] edges; }

        public static string Build(string arg)
        {
            // arg: "json경로|루트이름|offsetX|offsetZ"
            var parts = arg.Split('|');
            string jsonPath = parts[0];
            string rootName = parts.Length > 1 ? parts[1] : "1F_Map";
            float offX = parts.Length > 2 ? float.Parse(parts[2]) : 10f;
            float offZ = parts.Length > 3 ? float.Parse(parts[3]) : 0f;
            float wallH = parts.Length > 4 ? float.Parse(parts[4]) : 0f; // 0 = 프리팹 원래 높이
            bool doubleOuter = parts.Length > 5 ? parts[5] == "1" : true;

            var g = JsonUtility.FromJson<Grid>(File.ReadAllText(jsonPath));
            var example = GameObject.Find("example") ?? throw new Exception("씬에 'example' 오브젝트가 없습니다");

            GameObject wallSrc = null, doorSrc = null, floorSrc = null;
            Transform wallInst = null, floorInst = null;
            foreach (Transform ch in example.transform)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(ch.gameObject);
                if (src == null) continue;
                if (ch.name.StartsWith("SM_Bld_Wall_Baseboard_Door")) doorSrc = src;
                else if (ch.name.StartsWith("SM_Bld_Wall_Baseboard")) { wallSrc = src; wallInst = ch; }
                else if (ch.name.StartsWith("SM_Bld_Floor")) { floorSrc = src; floorInst = ch; }
            }
            if (wallSrc == null || doorSrc == null || floorSrc == null)
                throw new Exception($"example에서 프리팹을 찾지 못함 wall={wallSrc} door={doorSrc} floor={floorSrc}");

            float baseY = wallInst.position.y;
            float floorY = floorInst.position.y;
            float G = g.grid;

            var old = GameObject.Find(rootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "Build " + rootName);
            root.transform.position = new Vector3(offX, 0, offZ);
            var walls = Child(root, "Walls"); var doors = Child(root, "Doors"); var floors = Child(root, "Floor");

            // 벽 메쉬가 피벗 한쪽으로만 두께를 가지는지 (그렇다면 내부벽은 양면으로 2장)
            var probe = Spawn(wallSrc, null, Vector3.zero, Quaternion.identity);
            var pb = WorldBounds(probe);
            UnityEngine.Object.DestroyImmediate(probe);
            bool oneSided = pb.min.z > -0.05f || pb.max.z < 0.05f;
            float baseH = pb.size.y;
            if (wallH <= 0) wallH = baseH;
            float wallScaleY = wallH / baseH;

            int nWall = 0, nDoor = 0;
            var occupied = new HashSet<string>();
            foreach (var e in g.edges) occupied.Add($"{e.o}{e.k},{e.j}");

            foreach (var e in g.edges)
            {
                var src = e.t == "door" ? doorSrc : wallSrc;
                var parent = e.t == "door" ? doors : walls;
                bool vertical = e.o == "v";
                // 바깥쪽 판단: 건물 외곽선이면 안쪽만 바라보게 1장
                bool outerLow = vertical ? e.k == 0 : e.j == 0;          // 서/남 외벽 → 안쪽은 +방향
                bool outerHigh = vertical ? e.k == g.nx : e.j == g.nz;   // 동/북 외벽 → 안쪽은 -방향
                var faces = new List<bool>(); // true = +방향(동/북)을 바라봄
                if (outerLow && !(doubleOuter && oneSided)) faces.Add(true);
                else if (outerHigh && !(doubleOuter && oneSided)) faces.Add(false);
                else if (oneSided) { faces.Add(true); faces.Add(false); }
                else faces.Add(true);

                foreach (var facePlus in faces)
                {
                    // 예시 방 규칙: 남쪽벽 rot0, 북쪽벽 rot180, 서쪽벽 +90, 동쪽벽 -90 (모두 방 안쪽을 봄)
                    // rot0 = +Z(북)을 봄, rot180 = -Z, +90 = +X(동), -90 = -X
                    float rotY = !vertical ? (facePlus ? 0f : 180f) : (facePlus ? 90f : -90f);
                    var rot = Quaternion.Euler(0, rotY, 0);
                    float lineX = e.k * G, lineZ = e.j * G;
                    var go = Spawn(src, parent.transform, Vector3.zero, rot);
                    var b = WorldBounds(go);
                    Vector3 p = go.transform.position;
                    if (!vertical) { p.x += (offX + e.k * G) - b.min.x; p.z += offZ + lineZ - go.transform.position.z; }
                    else { p.z += (offZ + e.j * G) - b.min.z; p.x += offX + lineX - go.transform.position.x; }
                    p.y = baseY;
                    go.transform.position = p;
                    go.name = $"{(e.t == "door" ? "Door" : "Wall")}_{e.o}{e.k}_{e.j}{(faces.Count > 1 ? (facePlus ? "_a" : "_b") : "")}";
                    if (e.t != "door")
                    {
                        if (Mathf.Abs(wallScaleY - 1f) > 0.001f) go.transform.localScale = new Vector3(1, wallScaleY, 1);
                    }
                    else if (wallH > baseH + 0.01f)
                    {
                        // 문은 원래 비율 유지, 문 위쪽은 일반 벽을 잘라 올려서 높이를 맞춤
                        var top = Spawn(wallSrc, parent.transform, go.transform.position + Vector3.up * baseH, rot);
                        top.transform.localScale = new Vector3(1, (wallH - baseH) / baseH, 1);
                        top.name = go.name + "_Top";
                    }
                    if (e.t == "door") nDoor++; else nWall++;
                }
            }

            // 바닥: 2.5m 칸마다 1장
            var fp = Spawn(floorSrc, null, Vector3.zero, Quaternion.identity);
            var fb = WorldBounds(fp);
            UnityEngine.Object.DestroyImmediate(fp);
            float fs = G / Mathf.Max(0.01f, Mathf.Max(fb.size.x, fb.size.z));
            int nFloor = 0;
            for (int k = 0; k < g.nx; k++)
                for (int j = 0; j < g.nz; j++)
                {
                    var go = Spawn(floorSrc, floors.transform, Vector3.zero, Quaternion.identity);
                    go.transform.localScale = Vector3.one * fs;
                    var b = WorldBounds(go);
                    var p = go.transform.position;
                    p.x += offX + k * G - b.min.x;
                    p.z += offZ + j * G - b.min.z;
                    p.y = floorY;
                    go.transform.position = p;
                    go.name = $"Floor_{k}_{j}";
                    nFloor++;
                }

            foreach (var t in root.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"{rootName} 생성: 벽높이 {wallH:0.##}m, 벽 {nWall}, 문 {nDoor}, 바닥 {nFloor} (벽 단면={(oneSided ? "한쪽" : "중앙")}, 바닥 스케일 {fs:0.###}, baseY {baseY})";
        }

        /// <summary>파일 변경이 감지되지 않을 때 강제 재임포트 + 컴파일. arg: 쉼표로 구분한 에셋 경로</summary>
        public static string Reimport(string arg)
        {
            foreach (var p in arg.Split(','))
                AssetDatabase.ImportAsset(p.Trim(), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
            return "재임포트 + 컴파일 요청: " + arg;
        }

        static GameObject Child(GameObject p, string n) { var g = new GameObject(n); g.transform.SetParent(p.transform, false); return g; }

        static GameObject Spawn(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, rot);
            return go;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
