using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>외곽 둘레 2.5m 칸 중 벽이 덮지 않은 칸을 찾아 Busted_Wall_Plain 으로 채움. arg: 루트이름</summary>
    public static class BustedPerimeterFix
    {
        const float G = 2.5f; const int NX = 24, NZ = 16;

        public static string Fill(string rootName)
        {
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            var plain = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BustedMap/Prefabs/Busted_Wall_Plain.prefab");
            var o = root.transform.position;
            var boxes = new List<Bounds>();
            foreach (var s in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
            {
                var rs = s.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                boxes.Add(b);
            }
            // 외곽 칸 목록: (세로?, k, j, 중심)
            var edges = new List<(bool v, int k, int j, Vector3 c)>();
            for (int k = 0; k < NX; k++) { edges.Add((false, k, 0, o + new Vector3((k + .5f) * G, 1.5f, 0))); edges.Add((false, k, NZ, o + new Vector3((k + .5f) * G, 1.5f, NZ * G))); }
            for (int j = 0; j < NZ; j++) { edges.Add((true, 0, j, o + new Vector3(0, 1.5f, (j + .5f) * G))); edges.Add((true, NX, j, o + new Vector3(NX * G, 1.5f, (j + .5f) * G))); }

            var parentFor = root.transform; // 가장 가까운 방 그룹의 Walls 로 넣기
            int added = 0; var log = new List<string>();
            foreach (var e in edges)
            {
                bool covered = false;
                foreach (var b in boxes)
                {
                    var bb = b; bb.Expand(new Vector3(0.05f, 0.05f, 0.05f));
                    // 칸 중심과 양 끝 근처 3점이 모두 덮이면 OK
                    var d = e.v ? new Vector3(0, 0, 0.9f) : new Vector3(0.9f, 0, 0);
                    if (bb.Contains(e.c)) { covered = true; break; }
                }
                if (covered) continue;
                var parent = NearestWalls(root.transform, e.c);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(plain, parent);
                inst.name = $"Wall_{(e.v ? "v" : "h")}{e.k}_{e.j}";
                inst.transform.SetPositionAndRotation(new Vector3(e.c.x, o.y, e.c.z), Quaternion.Euler(0, e.v ? 90 : 0, 0));
                var ws = inst.GetComponent<Busted.Map.WallSegment>();
                ws.vertical = e.v; ws.gridK = e.k; ws.gridJ = e.j; ws.isDoor = false;
                GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                Undo.RegisterCreatedObjectUndo(inst, "Perimeter fix");
                added++; log.Add(inst.name + " → " + parent.parent?.name);
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"{rootName}: 외곽 빈칸 {added}개 채움\n" + string.Join("\n", log);
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
                    float d = (new Vector3(t.position.x, 0, t.position.z) - new Vector3(p.x, 0, p.z)).sqrMagnitude;
                    if (d < bd) { bd = d; best = room; }
                }
            }
            if (best == root) return root;
            return best.Find("Walls") ?? best;
        }
    }
}
