using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    public static class BustedMeshProbe
    {
        /// <summary>프리팹 메쉬의 단면(y,z) 프로파일 출력. arg = 프리팹 경로</summary>
        public static string Dump(string prefabPath)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var mesh = go.GetComponentInChildren<MeshFilter>().sharedMesh;
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv;
            var sb = new StringBuilder($"{mesh.name} verts={v.Length} bounds={mesh.bounds}\n");
            var rows = Enumerable.Range(0, v.Length)
                .Select(i => $"y={v[i].y:0.000} z={v[i].z:0.000} n=({n[i].x:0.0},{n[i].y:0.0},{n[i].z:0.0}) uv=({uv[i].x:0.000},{uv[i].y:0.000})")
                .Distinct().OrderBy(s => s);
            foreach (var r in rows) sb.AppendLine(r);
            return sb.ToString();
        }
    }
}
