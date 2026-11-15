using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    /// <summary>읽기 전용 조사 도구 (씬을 바꾸지 않음)</summary>
    public static class BustedDoorProbe
    {
        const float G = 2.5f;

        /// arg: "루트|방이름패턴" — 그 방의 계층(벽 면 제외)과, 그 방에 닿은 문(DoorUnit) 목록
        public static string Room(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform; var o = map.position;
            var sb = new StringBuilder();
            var cells = BustedWallSystem.CellRooms(map);
            foreach (Transform room in map)
            {
                if (!room.name.StartsWith("Room_") || !room.name.Contains(p[1])) continue;
                sb.AppendLine($"## {room.name}");
                void Walk(Transform t, int d)
                {
                    if (t.GetComponent<WallSegment>() != null) return;
                    var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
                    var r = t.GetComponent<Renderer>();
                    sb.AppendLine($"{new string(' ', d * 2)}{t.name} pos=({t.position.x - o.x:0.##},{t.position.y:0.##},{t.position.z - o.z:0.##}) rotY={t.eulerAngles.y:0} s={t.localScale.x:0.##},{t.localScale.y:0.##} {(src ? "[" + src.name + "]" : "")}{(r ? " R" : "")}{(t.GetComponent<DoorUnit>() ? " DU" : "")} kids={t.childCount}");
                    if (d < 4 && !(PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && d > 0)) foreach (Transform c in t) Walk(c, d + 1);
                }
                Walk(room, 0);
                sb.AppendLine("  -- 닿은 문 --");
                foreach (var du in map.GetComponentsInChildren<DoorUnit>(true))
                {
                    bool hit = false;
                    foreach (var s in new[] { -0.6f, 0.6f })
                    {
                        var q = du.transform.position + du.transform.forward * s - o;
                        if (cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(q.x / G), Mathf.FloorToInt(q.z / G)), out var rr) && rr == room) hit = true;
                    }
                    if (!hit) continue;
                    sb.AppendLine($"  {du.transform.parent?.parent?.name}/{du.name} pos=({du.transform.position.x - o.x:0.##},{du.transform.position.z - o.z:0.##}) w={du.width:0.##} h={du.height:0.##} kids={string.Join(",", du.transform.Cast<Transform>().Select(c => c.name + (c.gameObject.activeSelf ? "" : "(off)")))}");
                }
            }
            return sb.Length == 0 ? "(방 없음)" : sb.ToString();
        }
    }
}
