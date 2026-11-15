using System.Text;
using UnityEditor;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    public static class BustedPrefabProbe
    {
        public static string Tree(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var sb = new StringBuilder();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
                sb.Append(t == go.transform ? "" : "  ").Append(t.name)
                  .Append($" lp={t.localPosition} lr={t.localEulerAngles} ls={t.localScale}")
                  .Append(mf ? $" mesh={mf.sharedMesh?.name}" : "")
                  .Append(mr ? $" mats={string.Join(",", System.Array.ConvertAll(mr.sharedMaterials, m => m ? m.name : "null"))}" : "")
                  .AppendLine();
            }
            return sb.ToString();
        }
    }
}
