using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Busted.Map.EditorTools
{
    /// <summary>읽기 전용: 씬 계층 요약 (씬을 바꾸지 않음)</summary>
    public static class BustedHierProbe
    {
        static string B(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return "noR";
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return $"b=({b.min.x:0.#},{b.min.z:0.#})~({b.max.x:0.#},{b.max.z:0.#}) y{b.min.y:0.#}~{b.max.y:0.#}";
        }
        static int All(Transform t) => t.GetComponentsInChildren<Transform>(true).Length;

        /// arg: 깊이 (예: "2") — 씬 루트부터 그 깊이까지, Room_ 아래 Walls/Floor/Doors 는 개수만
        public static string Dump(string arg)
        {
            int maxD = int.TryParse(arg, out var d0) ? d0 : 2;
            var sb = new StringBuilder();
            void Walk(Transform t, int d)
            {
                var pi = PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) ? " [P]" : (PrefabUtility.IsPartOfPrefabInstance(t.gameObject) ? " [p]" : "");
                sb.AppendLine($"{new string(' ', d * 2)}{t.name}{pi}{(t.gameObject.activeSelf ? "" : " (off)")} kids={t.childCount} all={All(t)} pos=({t.position.x:0.##},{t.position.y:0.##},{t.position.z:0.##}) s={t.lossyScale.x:0.##} {B(t)}");
                if (d >= maxD) return;
                if (t.name == "Walls" || t.name == "Floor" || t.name == "Doors") return;
                if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && d > 0) return;
                foreach (Transform c in t) Walk(c, d + 1);
            }
            foreach (var g in SceneManager.GetActiveScene().GetRootGameObjects()) Walk(g.transform, 0);
            return sb.ToString();
        }

        /// arg: 오브젝트 경로 — 그 오브젝트의 직계 자식 전부
        public static string Kids(string arg)
        {
            var t = GameObject.Find(arg)?.transform;
            if (t == null) return "없음";
            var sb = new StringBuilder();
            foreach (Transform c in t)
            {
                var pi = PrefabUtility.IsAnyPrefabInstanceRoot(c.gameObject) ? " [P]" : (PrefabUtility.IsPartOfPrefabInstance(c.gameObject) ? " [p]" : "");
                sb.AppendLine($"{c.name}{pi} kids={c.childCount} pos=({c.position.x:0.##},{c.position.y:0.##},{c.position.z:0.##}) {B(c)}");
            }
            return sb.ToString();
        }
    }
}
