using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    /// <summary>문짝(DoorLeaf) 실제 크기·위치에 맞춰 문 폭/문틀/중심을 다시 맞춤. 이후 BustedRelayout 으로 벽 구멍을 채움.</summary>
    public static class BustedDoorFit
    {
        static Bounds LocalBounds(Transform root, IEnumerable<Renderer> rs)
        {
            bool first = true; Bounds b = default;
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// arg: 루트 — 문마다 문짝 실제 폭 / 기록된 폭 / 문틀 폭 출력
        public static string Inspect(string root)
        {
            var map = GameObject.Find(root).transform;
            var sb = new StringBuilder();
            foreach (var d in map.GetComponentsInChildren<DoorUnit>(true))
            {
                var leaves = d.GetComponentsInChildren<Renderer>(true).Where(r => r.name != "Frame" && r.gameObject.activeInHierarchy).ToList();
                var lb = LocalBounds(d.transform, leaves);
                var fr = d.transform.Find("Frame");
                var fb = fr != null ? LocalBounds(d.transform, new[] { fr.GetComponent<Renderer>() }) : default;
                var kids = string.Join(",", d.transform.Cast<Transform>().Select(c => $"{c.name}{(c.gameObject.activeSelf ? "" : "(off)")}@{c.localPosition.x:0.##}"));
                sb.AppendLine($"{d.name} pairs={d.pairs} width={d.width:0.##} | 문짝 x {lb.min.x:0.##}~{lb.max.x:0.##} (폭 {lb.size.x:0.##}) | 문틀 폭 {fb.size.x:0.##} | {kids}");
            }
            return sb.ToString();
        }

        /// arg: 루트 — 문짝 실제 폭에 맞춰 width/pairs/문틀 다시 만들고, 문 중심을 문짝 중심으로 (보이는 문짝 위치는 그대로)
        public static string Fit(string root)
        {
            var map = GameObject.Find(root).transform;
            var sb = new StringBuilder(); int n = 0;
            foreach (var d in map.GetComponentsInChildren<DoorUnit>(true))
            {
                var fr = d.transform.Find("Frame");
                var leaves = d.GetComponentsInChildren<Renderer>(true).Where(r => r.name != "Frame" && r.gameObject.activeInHierarchy).ToList();
                if (leaves.Count == 0) { sb.AppendLine($"  ⚠ {d.name}: 문짝 없음"); continue; }
                var lb = LocalBounds(d.transform, leaves);
                float w = lb.size.x + 0.04f;
                float cx = lb.center.x;
                bool changed = Mathf.Abs(w - d.width) > 0.05f || Mathf.Abs(cx) > 0.02f;
                if (!changed) continue;
                // 중심 이동 (자식은 제자리)
                var shift = new Vector3(cx, 0, 0);
                var worldShift = d.transform.TransformVector(shift);
                d.transform.position += worldShift;
                foreach (Transform c in d.transform) c.position -= worldShift;
                if (fr != null) fr.localPosition = new Vector3(0, fr.localPosition.y, fr.localPosition.z);
                int pairs = Mathf.Max(1, Mathf.RoundToInt(lb.size.x / 1.99f));
                sb.AppendLine($"  {d.name}: 폭 {d.width:0.##} → {w:0.##} (양문 {d.pairs}→{pairs}세트), 중심 {cx:+0.##;-0.##;0}m");
                d.width = w; d.pairs = pairs;
                if (fr != null)
                {
                    var mf = fr.GetComponent<MeshFilter>();
                    var uv = mf.sharedMesh != null && mf.sharedMesh.uv.Length > 0 ? mf.sharedMesh.uv[0] : Vector2.zero;
                    mf.sharedMesh = FrameMesh(w, d.height > 0 ? d.height : 2.27f, uv);
                }
                n++;
            }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{root}: 문 {n}개 크기 맞춤\n" + sb;
        }

        static Mesh FrameMesh(float w, float h, Vector2 uv)
        {
            const float fw = 0.08f, dz = 0.22f;
            string dir = "Assets/BustedMap/Generated/Faces";
            string path = $"{dir}/DoorFrame_fit_{w:0.000}_{h:0.000}_{uv.x:0.0000}_{uv.y:0.0000}.asset".Replace(",", ".");
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (ex != null) return ex;
            var P = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var I = new List<int>();
            void Cube(Vector3 mn, Vector3 mx)
            {
                var c = (mn + mx) / 2; var e = (mx - mn) / 2;
                foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                {
                    Vector3 a = n.x != 0 ? Vector3.up : Vector3.right, b = Vector3.Cross(n, a);
                    int s = P.Count;
                    foreach (var (sa, sb2) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                    { P.Add(c + Vector3.Scale(n, e) + Vector3.Scale(a * sa, e) + Vector3.Scale(b * sb2, e)); N.Add(n); U.Add(uv); }
                    var p0 = P[s]; var p1 = P[s + 1]; var p2 = P[s + 2];
                    if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) > 0) I.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                    else I.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                }
            }
            Cube(new Vector3(-w / 2 - fw, 0, -dz), new Vector3(-w / 2, h + fw, dz));
            Cube(new Vector3(w / 2, 0, -dz), new Vector3(w / 2 + fw, h + fw, dz));
            Cube(new Vector3(-w / 2 - fw, h, -dz), new Vector3(w / 2 + fw, h + fw, dz));
            var m = new Mesh { name = "DoorFrame_fit" };
            m.SetVertices(P); m.SetNormals(N); m.SetUVs(0, U); m.SetTriangles(I, 0); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
