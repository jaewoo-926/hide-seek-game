using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 층 생성 (비파괴 버전). 1·2층과 같은 결과를 만들되, Generated 메쉬를 새로 만들지 않고 기존 것을 재사용한다.
    /// 격자 빌드 → 벽 메쉬 연결(가운데 띠 없음) → 문 정리(문틀+평평한 벽) → 방 그룹화
    /// arg: "grid.json|루트이름|offsetX|offsetZ|벽높이|1|rooms.json"
    /// </summary>
    public static class BustedFloorPipeline3
    {
        const string Gen = "Assets/BustedMap/Generated";
        const string WallPath = "Assets/03.Models/Ofiice/PolygonOffice/Prefabs/Buildings/SM_Bld_Wall_Baseboard_01.prefab";
        const string DoorPath = "Assets/03.Models/Ofiice/PolygonOffice/Prefabs/Buildings/SM_Bld_Wall_Baseboard_Door_01.prefab";
        const string FloorPath = "Assets/03.Models/Ofiice/PolygonOffice/Prefabs/Buildings/SM_Bld_Floor_Concrete_01.prefab";
        const float OpenX0 = -1.833f, OpenX1 = -0.667f, OpenTop = 2.049f, Len = 2.5f;

        public static string BuildFloorSafe(string arg)
        {
            var p = arg.Split('|');
            string buildArg = string.Join("|", p, 0, Math.Min(6, p.Length));
            string rootName = p[1];
            string roomsJson = p.Length > 6 ? p[6] : "";

            // 1) 격자 빌드 (임시 example 사용)
            var user = GameObject.Find("example");
            if (user != null) user.name = "example__keep";
            var temp = new GameObject("example");
            Add(temp, WallPath, "SM_Bld_Wall_Baseboard_01", 0f);
            Add(temp, DoorPath, "SM_Bld_Wall_Baseboard_Door_01", 0f);
            Add(temp, FloorPath, "SM_Bld_Floor_Concrete_01", -0.0043f);
            string r1;
            try { r1 = BustedGridBuilderV3.Build(buildArg); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temp);
                if (user != null) user.name = "example";
            }

            // 2) 벽 메쉬(가운데 띠 없는 버전) 연결 — 기존 에셋 재사용
            string r2 = BustedMeshRepair.Repair(rootName);

            // 3) 문 정리 — 기존 에셋 재사용
            string r3 = DoorCleanSafe(rootName);

            // 4) 방 그룹화
            string r4 = string.IsNullOrEmpty(roomsJson) ? "(방 그룹화 생략)" : BustedRoomGrouper2.Apply($"{roomsJson}|{rootName}");
            return string.Join("\n", r1, r2, r3, r4);
        }

        static string DoorCleanSafe(string rootName)
        {
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            var wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPath);
            var lower = Load("Wall_Lower_NoCrown"); var noTrim = Load("Wall_NoTrim"); var frame = Load("Door_FrameOnly");
            float baseH = wallPrefab.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            int n = 0;
            foreach (var seg in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
            {
                if (!seg.isDoor) continue;
                var doors = new System.Collections.Generic.List<Transform>();
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
                    n++;
                }
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"문 {n}면 정리 (기존 메쉬 재사용)";
        }

        static Mesh Load(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/{name}.asset");
            if (m == null) throw new Exception($"{Gen}/{name}.asset 이 없습니다");
            return m;
        }

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
