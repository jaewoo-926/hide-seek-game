using System;
using UnityEditor;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 1층과 같은 방식으로 한 층을 통째로 생성:
    /// 격자 빌드(V3: 4.5m 벽, 칸 그룹, 양면 외벽) → 가운데 띠 제거(Plainify4) → 문 정리(DoorClean)
    /// arg: "json경로|루트이름|offsetX|offsetZ|벽높이"
    /// 현재 열린 씬에 example 방이 없으면 1층과 같은 프리팹으로 임시 example을 만들었다가 지운다.
    /// </summary>
    public static class BustedFloorPipeline2
    {
        const string WallPath = "Assets/03.Models/Ofiice/PolygonOffice/Prefabs/Buildings/SM_Bld_Wall_Baseboard_01.prefab";
        const string DoorPath = "Assets/03.Models/Ofiice/PolygonOffice/Prefabs/Buildings/SM_Bld_Wall_Baseboard_Door_01.prefab";

        public static string BuildFloor(string arg)
        {
            var parts = arg.Split('|');
            string rootName = parts.Length > 1 ? parts[1] : "Map";
            GameObject temp = null;
            // 씬의 example이 프리팹 연결이 끊긴 상태일 수 있으므로 항상 임시 example을 사용
            var userExample = GameObject.Find("example");
            if (userExample != null) userExample.name = "example__keep";
            {
                var floorPath = FindFloorPrefab();
                temp = new GameObject("example");
                Add(temp, WallPath, "SM_Bld_Wall_Baseboard_01", 0f);
                Add(temp, DoorPath, "SM_Bld_Wall_Baseboard_Door_01", 0f);
                Add(temp, floorPath, "SM_Bld_Floor_Concrete_01", -0.0043f);
            }
            string r1, r2, r3;
            try
            {
                r1 = BustedGridBuilderV3.Build(arg);
            }
            finally
            {
                if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
                if (userExample != null) userExample.name = "example";
            }
            r2 = BustedWallPlainify4.Apply(rootName);
            r3 = BustedDoorClean.Apply(rootName); // 씬 저장 포함
            return r1 + "\n" + r2 + "\n" + r3;
        }

        static string FindFloorPrefab()
        {
            foreach (var g in AssetDatabase.FindAssets("SM_Bld_Floor_Concrete_01 t:Prefab"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("PolygonOffice") && p.EndsWith("/SM_Bld_Floor_Concrete_01.prefab")) return p;
            }
            throw new Exception("SM_Bld_Floor_Concrete_01 프리팹을 찾지 못함");
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
