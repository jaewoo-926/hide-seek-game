using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    public static class BustedSceneProbe
    {
        /// arg: "패턴1,패턴2" — 현재 씬에서 이름에 패턴이 들어간 오브젝트 목록 (경로/위치/회전/크기/보이는 범위/프리팹)
        public static string Find(string arg)
        {
            var pats = arg.Split(',').Select(p => p.Trim().ToLower()).Where(p => p.Length > 0).ToArray();
            var sb = new StringBuilder();
            foreach (var r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                {
                    var n = t.name.ToLower();
                    if (!pats.Any(p => n.Contains(p))) continue;
                    if (t.parent != null && pats.Any(p => t.parent.name.ToLower().Contains(p)) && t.parent.name != t.name) continue; // 자식 중복 생략
                    var rs = t.GetComponentsInChildren<Renderer>(true);
                    string bs = "";
                    if (rs.Length > 0) { var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); bs = $"bounds c=({b.center.x:0.##},{b.center.y:0.##},{b.center.z:0.##}) size=({b.size.x:0.##},{b.size.y:0.##},{b.size.z:0.##})"; }
                    var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
                    var kids = string.Join(",", t.Cast<Transform>().Select(c => c.name).Take(8));
                    sb.AppendLine($"{Path(t)} | pos=({t.position.x:0.###},{t.position.y:0.###},{t.position.z:0.###}) rotY={t.eulerAngles.y:0.#} scale={t.lossyScale} | {bs} | prefab={(src ? AssetDatabase.GetAssetPath(src) : "-")} | kids={kids} | active={t.gameObject.activeInHierarchy}");
                }
            return sb.Length == 0 ? "(없음)" : sb.ToString();
        }

        /// 루트 바로 아래 / 방 바로 아래의 "Walls/Doors/Floor 가 아닌" 오브젝트 목록
        public static string Extras(string root)
        {
            var map = GameObject.Find(root).transform;
            var sb = new StringBuilder();
            foreach (var r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                sb.AppendLine($"[씬 루트] {r.name} pos={r.transform.position}");
            foreach (Transform c in map)
            {
                if (c.name.StartsWith("Room_"))
                {
                    foreach (Transform k in c)
                        if (k.name != "Walls" && k.name != "Doors" && k.name != "Floor") sb.AppendLine($"  {c.name}/{k.name} pos={k.position}");
                    foreach (var f in new[] { "Walls", "Doors" })
                    {
                        var ft = c.Find(f); if (ft == null) continue;
                        foreach (Transform k in ft)
                            if (k.GetComponent<Busted.Map.WallSegment>() == null && k.GetComponent<Busted.Map.DoorUnit>() == null) sb.AppendLine($"  {c.name}/{f}/{k.name} pos={k.position}");
                    }
                }
                else sb.AppendLine($"  {root}/{c.name} pos={c.position}");
            }
            return sb.ToString();
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
