using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>외곽 벽 중 2.5m 칸에 맞지 않는(어긋난) 조각을 지우고, 비어 있는 외곽 칸을 Busted_Wall_Plain 으로 채움. arg: 루트이름</summary>
    public static class BustedPerimeterFix2
    {
        const float G = 2.5f; const int NX = 24, NZ = 16;

        public static string Fix(string rootName)
        {
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            var plain = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BustedMap/Prefabs/Busted_Wall_Plain.prefab");
            var o = root.transform.position;
            var log = new List<string>();

            // 1) 어긋난 외곽 조각 제거
            var keep = new HashSet<string>();
            foreach (var s in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
            {
                var p = s.transform.position - o;
                float dV = Mathf.Min(Mathf.Abs(p.x), Mathf.Abs(p.x - NX * G)), dH = Mathf.Min(Mathf.Abs(p.z), Mathf.Abs(p.z - NZ * G));
                bool onV = dV < 1.0f && s.vertical, onH = dH < 1.0f && !s.vertical;
                if (!onV && !onH) continue; // 내부 벽
                float along = onH && !onV ? p.x : p.z;
                if (onH && onV) continue;
                float cell = along / G - 0.5f;
                bool aligned = Mathf.Abs(cell - Mathf.Round(cell)) < 0.05f && (onV ? dV : dH) < 0.05f;
                bool v = onV;
                if (aligned) { keep.Add($"{(v ? "v" : "h")}{(v ? (Mathf.Abs(p.x) < 1f ? 0 : NX) : Mathf.RoundToInt(cell))}_{(v ? Mathf.RoundToInt(cell) : (Mathf.Abs(p.z) < 1f ? 0 : NZ))}"); continue; }
                log.Add("어긋난 조각 삭제: " + s.name + " @" + p);
                UnityEngine.Object.DestroyImmediate(s.gameObject);
            }

            // 2) 비어 있는 외곽 칸 채우기
            int added = 0;
            void TryAdd(bool v, int k, int j)
            {
                string key = $"{(v ? "v" : "h")}{k}_{j}";
                if (keep.Contains(key)) return;
                var c = v ? o + new Vector3(k * G, 0, (j + .5f) * G) : o + new Vector3((k + .5f) * G, 0, j * G);
                var parent = NearestWalls(root.transform, c);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(plain, parent);
                inst.name = "Wall_" + key;
                inst.transform.SetPositionAndRotation(c, Quaternion.Euler(0, v ? 90 : 0, 0));
                var ws = inst.GetComponent<Busted.Map.WallSegment>();
                ws.vertical = v; ws.gridK = k; ws.gridJ = j; ws.isDoor = false;
                GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                added++; log.Add("채움: " + inst.name);
            }
            for (int k = 0; k < NX; k++) { TryAdd(false, k, 0); TryAdd(false, k, NZ); }
            for (int j = 0; j < NZ; j++) { TryAdd(true, 0, j); TryAdd(true, NX, j); }

            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"{rootName}: 외곽 칸 {keep.Count}개 정상, {added}개 채움\n" + string.Join("\n", log);
        }

        static Transform NearestWalls(Transform root, Vector3 p)
        {
            Transform best = root; float bd = float.MaxValue;
            foreach (Transform room in root)
            {
                if (!room.name.StartsWith("Room_")) continue;
                var floor = room.Find("Floor");
                if (floor == null) continue;
                foreach (Transform t in floor)
                {
                    float d = new Vector3(t.position.x - p.x, 0, t.position.z - p.z).sqrMagnitude;
                    if (d < bd) { bd = d; best = room; }
                }
            }
            if (best == root) return root;
            return best.Find("Walls") ?? best;
        }
    }
}
