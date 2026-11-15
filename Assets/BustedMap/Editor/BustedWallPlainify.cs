using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 벽 위쪽 덧댐 조각(Upper*)의 걸레받이를 평평하게 만든 메쉬로 교체해
    /// 벽 중간 높이(3m)에 생기던 가로 띠를 없앤다. 맵의 다른 부분은 건드리지 않는다.
    /// invoke: type=Busted.Map.EditorTools.BustedWallPlainify method=Apply arg=루트이름
    /// </summary>
    public static class BustedWallPlainify
    {
        const string MeshPath = "Assets/BustedMap/Generated/SM_Bld_Wall_Baseboard_01_Plain.asset";

        public static string Apply(string rootName)
        {
            var root = GameObject.Find(string.IsNullOrEmpty(rootName) ? "1F_Map" : rootName)
                       ?? throw new Exception("루트 없음: " + rootName);
            Mesh plain = null; Mesh source = null;
            int swapped = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Upper")) continue;
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (plain == null)
                {
                    source = mf.sharedMesh;
                    plain = BuildPlain(source);
                }
                if (mf.sharedMesh == plain) continue;
                Undo.RecordObject(mf, "Plain upper wall");
                mf.sharedMesh = plain;
                var mc = t.GetComponent<MeshCollider>();
                if (mc != null) { Undo.RecordObject(mc, "Plain upper wall"); mc.sharedMesh = plain; }
                swapped++;
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"덧댐 조각 {swapped}개 교체 (원본 메쉬 {source?.name})";
        }

        static Mesh BuildPlain(Mesh src)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing != null) return existing;

            var m = UnityEngine.Object.Instantiate(src);
            m.name = src.name + "_Plain";
            var v = m.vertices; var n = m.normals; var uv = m.uv;

            // 벽 앞면(+Z 쪽) 위치: 중간 높이에서 +Z를 향하는 정점들의 z
            float faceZ = float.MinValue; int refIdx = -1;
            float yMin = m.bounds.min.y, yMax = m.bounds.max.y, h = yMax - yMin;
            for (int i = 0; i < v.Length; i++)
            {
                bool mid = v[i].y > yMin + h * 0.35f && v[i].y < yMin + h * 0.9f;
                if (mid && n.Length > i && n[i].z > 0.9f && v[i].z > faceZ) { faceZ = v[i].z; refIdx = i; }
            }
            if (refIdx < 0) throw new Exception("벽 앞면을 찾지 못함");
            // 걸레받이 = 앞면보다 튀어나온 정점 → 앞면으로 밀어 넣고, 색(UV)도 벽면과 같게
            var refUV = uv.Length > refIdx ? uv[refIdx] : Vector2.zero;
            float bbTop = yMin;
            for (int i = 0; i < v.Length; i++) if (v[i].z > faceZ + 0.003f) bbTop = Mathf.Max(bbTop, v[i].y);
            for (int i = 0; i < v.Length; i++)
            {
                bool protrude = v[i].z > faceZ + 0.003f;
                bool baseboardFace = v[i].y <= bbTop + 0.003f && n.Length > i && n[i].z > 0.9f && v[i].z >= faceZ - 0.003f;
                if (protrude || baseboardFace)
                {
                    v[i].z = Mathf.Min(v[i].z, faceZ);
                    if (uv.Length > i) uv[i] = refUV;
                }
            }
            m.vertices = v; m.uv = uv;
            m.RecalculateBounds();

            var dir = Path.GetDirectoryName(MeshPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/BustedMap", "Generated");
            AssetDatabase.CreateAsset(m, MeshPath);
            AssetDatabase.SaveAssets();
            return m;
        }
    }
}
