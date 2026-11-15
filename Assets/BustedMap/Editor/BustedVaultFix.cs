using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    /// <summary>3층 금고 문틀 자리: 문틀을 벽에 끼운 오브젝트로 표시하고, 빠진 벽 칸을 되살려 방이 합쳐지지 않게 함</summary>
    public static class BustedVaultFix
    {
        const float G = 2.5f;

        /// arg: "루트|문틀이름"
        public static string Mark(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform; var o = map.position;
            var frame = map.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == p[1]) ?? throw new Exception("문틀 없음: " + p[1]);
            var wo = frame.GetComponent<WallOpening>() ?? frame.gameObject.AddComponent<WallOpening>();
            wo.alignCenter = true; wo.height = 3f;
            var own = frame.GetComponent<Renderer>(); var rs = own != null ? new[] { own } : frame.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            bool v = b.size.z > b.size.x;
            float line = v ? b.center.x - o.x : b.center.z - o.z;
            float a0 = (v ? b.min.z - o.z : b.min.x - o.x), a1 = (v ? b.max.z - o.z : b.max.x - o.x);
            int lineIdx = Mathf.RoundToInt(line / G);
            // 문틀이 걸친 칸 중 벽이 없는 칸에 벽 추가 (구멍은 Relayout 이 문틀 폭만큼 뚫음)
            var walls = map.GetComponentsInChildren<WallSegment>(true).Where(s => s.GetComponent<WallCell>() != null).ToList();
            var template = walls.First();
            int added = 0;
            for (int k = Mathf.FloorToInt(a0 / G); k * G < a1; k++)
            {
                float c = (k + 0.5f) * G;
                bool has = walls.Any(s => s.vertical == v && Mathf.Abs((v ? s.transform.position.x - o.x : s.transform.position.z - o.z) - line) < 0.6f
                                          && Mathf.Abs((v ? s.transform.position.z - o.z : s.transform.position.x - o.x) - c) < 0.6f);
                if (has) continue;
                var go = new GameObject($"Wall_{(v ? "v" : "h")}{(v ? lineIdx : k)}_{(v ? k : lineIdx)}");
                go.transform.SetParent(frame.parent, false);
                go.transform.SetPositionAndRotation(o + (v ? new Vector3(line, 0, c) : new Vector3(c, 0, line)), Quaternion.Euler(0, v ? 90 : 0, 0));
                var ws = go.AddComponent<WallSegment>(); ws.vertical = v; ws.gridK = v ? lineIdx : k; ws.gridJ = v ? k : lineIdx;
                go.AddComponent<WallCell>();
                foreach (var side in new[] { "Face_A", "Face_B" })
                {
                    var f = new GameObject(side); f.transform.SetParent(go.transform, false);
                    f.transform.localRotation = Quaternion.Euler(0, side == "Face_A" ? 0 : 180, 0);
                    f.AddComponent<MeshFilter>(); var mr = f.AddComponent<MeshRenderer>();
                    var tf = template.transform.Find(side); if (tf != null) mr.sharedMaterials = tf.GetComponent<MeshRenderer>().sharedMaterials;
                }
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(template.gameObject));
                added++;
            }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{p[1]}: 벽 줄 {(v ? "x" : "z")}={line:0.###}, 폭 {a1 - a0:0.##}m, 벽 {added}칸 되살림";
        }

        /// arg: "루트|h또는v|줄좌표" — 그 줄에 있는 벽 칸 삭제 (잘못 추가한 벽 정리용)
        public static string RemoveLine(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform; var o = map.position;
            bool v = p[1] == "v"; float line = float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture);
            int n = 0;
            foreach (var s in map.GetComponentsInChildren<WallSegment>(true).ToList())
            {
                if (s.GetComponent<WallCell>() == null || s.vertical != v) continue;
                float l = v ? s.transform.position.x - o.x : s.transform.position.z - o.z;
                if (Mathf.Abs(l - line) < 0.05f) { UnityEngine.Object.DestroyImmediate(s.gameObject); n++; }
            }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{n}칸 삭제";
        }

        /// arg: "루트|이름앞부분|새이름" — 예: 3F_Map|Room_새공간_|Room_복도_2
        public static string Rename(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform;
            var t = map.Cast<Transform>().FirstOrDefault(x => x.name.StartsWith(p[1]));
            if (t == null) return "대상 없음";
            string old = t.name; t.name = p[2];
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{old} → {p[2]}";
        }
    }
}
