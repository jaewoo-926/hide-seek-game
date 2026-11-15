using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    public static class BustedMeshProbe2
    {
        public static string Dump(string prefabPath)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var mesh = go.GetComponentInChildren<MeshFilter>().sharedMesh;
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var t = mesh.triangles;
            var sb = new StringBuilder($"{mesh.name} verts={v.Length} tris={t.Length / 3} sub={mesh.subMeshCount}\n");
            for (int i = 0; i < v.Length; i++)
                sb.AppendLine($"{i} p=({v[i].x:0.000},{v[i].y:0.000},{v[i].z:0.000}) n=({n[i].x:0.00},{n[i].y:0.00},{n[i].z:0.00}) uv=({uv[i].x:0.000},{uv[i].y:0.000})");
            sb.Append("T:");
            foreach (var i in t) sb.Append(i).Append(',');
            return sb.ToString();
        }
    }
}
