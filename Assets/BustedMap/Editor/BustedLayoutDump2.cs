using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    /// <summary>층 구조 덤프 (벽 칸 / 문 / 바닥 / 기타 오브젝트) → ClaudeBridge/outbox/layout_루트.txt</summary>
    public static class BustedLayoutDump2
    {
        public static string Dump(string rootName)
        {
            var map = GameObject.Find(rootName).transform;
            var o = map.position;
            var sb = new StringBuilder();
            int nw = 0, nd = 0, nf = 0;
            foreach (var s in map.GetComponentsInChildren<WallSegment>(true))
            {
                var p = s.transform.position - o;
                var rs = s.GetComponentsInChildren<Renderer>();
                Bounds b = new Bounds(s.transform.position, Vector3.zero);
                if (rs.Length > 0) { b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); }
                var kids = new StringBuilder();
                foreach (Transform c in s.transform) kids.Append($"{c.name}@{c.localPosition.x:0.##},{c.localPosition.z:0.##},{c.localEulerAngles.y:0};");
                sb.AppendLine($"W|{s.name}|{p.x:0.###}|{p.z:0.###}|{s.transform.eulerAngles.y:0.#}|{(s.GetComponent<WallCell>() ? 1 : 0)}|{s.vertical}|{s.gridK}|{s.gridJ}|{Path(s.transform, map)}|{s.gameObject.activeInHierarchy}|{b.center.x - o.x:0.###}|{b.center.z - o.z:0.###}|{b.size.x:0.###}|{b.size.z:0.###}|{kids}|{s.transform.lossyScale}");
                nw++;
            }
            foreach (var d in map.GetComponentsInChildren<DoorUnit>(true))
            {
                var p = d.transform.position - o;
                sb.AppendLine($"D|{d.name}|{p.x:0.###}|{p.z:0.###}|{d.transform.eulerAngles.y:0.#}|{d.width:0.###}|{d.pairs}|{Path(d.transform, map)}|{d.transform.localScale}");
                nd++;
            }
            foreach (Transform room in map)
            {
                var f = room.Find("Floor");
                if (f != null) foreach (Transform t in f)
                    {
                        var r = t.GetComponent<Renderer>();
                        var c = (r ? r.bounds.center : t.position) - o;
                        sb.AppendLine($"F|{t.name}|{c.x:0.###}|{c.z:0.###}|{room.name}|{t.gameObject.activeInHierarchy}");
                        nf++;
                    }
                // 방 안의 기타 오브젝트
                foreach (Transform c in room)
                    if (c.name != "Walls" && c.name != "Doors" && c.name != "Floor") sb.AppendLine($"X|{room.name}/{c.name}");
            }
            File.WriteAllText($"ClaudeBridge/outbox/layout_{rootName}.txt", sb.ToString());
            return $"{rootName}: 벽 {nw}, 문 {nd}, 바닥 {nf}";
        }

        static string Path(Transform t, Transform root) => t.parent == null || t.parent == root ? t.name : Path(t.parent, root) + "/" + t.name;
    }
}
