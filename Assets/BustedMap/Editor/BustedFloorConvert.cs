using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 층의 벽 칸(WallSegment 그룹)을 Busted 벽 프리팹 3종으로 교체.
    ///  외벽 → Busted_Wall_Window / 내부 벽 → Busted_Wall_Plain / 문 → Busted_Wall_Door(5m, 문 칸 + 옆 벽 칸)
    /// 칸 이름·방 그룹·위치는 유지. arg: 루트이름
    /// </summary>
    public static class BustedFloorConvert
    {
        const string Dir = "Assets/BustedMap/Prefabs/";
        const float G = 2.5f; const int NX = 24, NZ = 16;

        public static string Convert(string rootName)
        {
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            var pPlain = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Busted_Wall_Plain.prefab");
            var pWin = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Busted_Wall_Window.prefab");
            var pDoor = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Busted_Wall_Door.prefab");
            if (!pPlain || !pWin || !pDoor) throw new Exception("Busted 벽 프리팹이 없습니다");
            Vector3 o = root.transform.position;

            var segs = new Dictionary<(bool, int, int), Busted.Map.WallSegment>();
            foreach (var s in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(s.gameObject);
                if (src != null && src.name.StartsWith("Busted_Wall_")) continue; // 이미 변환됨
                segs[(s.vertical, s.gridK, s.gridJ)] = s;
            }

            // 문 + 옆 벽 짝짓기
            var consumed = new HashSet<(bool, int, int)>();
            var partner = new Dictionary<(bool, int, int), (bool, int, int)?>();
            foreach (var kv in segs)
            {
                if (!kv.Value.isDoor) continue;
                var (v, k, j) = kv.Key;
                var minus = v ? (v, k, j - 1) : (v, k - 1, j);
                var plus = v ? (v, k, j + 1) : (v, k + 1, j);
                var plus2 = v ? (v, k, j + 2) : (v, k + 2, j);
                bool okM = segs.TryGetValue(minus, out var sm) && !sm.isDoor && !consumed.Contains(minus);
                bool okP = segs.TryGetValue(plus, out var sp) && !sp.isDoor && !consumed.Contains(plus);
                (bool, int, int)? pick = null;
                if (okP && (!okM || segs.ContainsKey(plus2))) pick = plus;
                else if (okM) pick = minus;
                if (pick.HasValue) consumed.Add(pick.Value);
                partner[kv.Key] = pick;
            }

            int nP = 0, nW = 0, nD = 0, nNarrow = 0;
            foreach (var kv in segs)
            {
                if (consumed.Contains(kv.Key)) continue;
                var s = kv.Value; var (v, k, j) = kv.Key;
                float line = v ? o.x + k * G : o.z + j * G;
                float start = v ? o.z + j * G : o.x + k * G;
                GameObject prefab; float len = G; float sx = 1f;
                if (s.isDoor)
                {
                    prefab = pDoor; len = 5f;
                    var p = partner[kv.Key];
                    if (p.HasValue)
                    {
                        var (_, pk, pj) = p.Value;
                        start = Mathf.Min(start, v ? o.z + pj * G : o.x + pk * G);
                        Undo.DestroyObjectImmediate(segs[p.Value].gameObject);
                    }
                    else { len = G; sx = 0.5f; nNarrow++; }
                    nD++;
                }
                else
                {
                    bool exterior = v ? (k == 0 || k == NX) : (j == 0 || j == NZ);
                    prefab = exterior ? pWin : pPlain;
                    if (exterior) nW++; else nP++;
                }
                var center = v ? new Vector3(line, o.y, start + len / 2) : new Vector3(start + len / 2, o.y, line);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, s.transform.parent);
                inst.name = s.name;
                inst.transform.SetPositionAndRotation(center, Quaternion.Euler(0, v ? 90f : 0f, 0));
                inst.transform.localScale = new Vector3(sx, 1, 1);
                inst.transform.SetSiblingIndex(s.transform.GetSiblingIndex());
                var ws = inst.GetComponent<Busted.Map.WallSegment>();
                ws.isDoor = s.isDoor; ws.vertical = v; ws.gridK = k; ws.gridJ = j;
                GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                Undo.RegisterCreatedObjectUndo(inst, "Floor convert");
                Undo.DestroyObjectImmediate(s.gameObject);
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"{rootName}: 내부벽 {nP}, 외벽(창문) {nW}, 양문 {nD} (좁은 문 {nNarrow})";
        }
    }
}
