using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 벽 중간 높이(3m 이음부)의 가로 띠만 제거.
    ///  - 아래 3m 벽/문: 꼭대기 몰딩(y≥2.62)을 벽면으로 평탄화
    ///  - 위쪽 덧댐: 바닥 걸레받이(y≤0.69)를 벽면으로 평탄화
    /// 맨 위 몰딩·바닥 걸레받이·문 디자인은 그대로 유지. 오브젝트 위치/구성은 건드리지 않음.
    /// </summary>
    public static class BustedWallPlainify3
    {
        const string Dir = "Assets/BustedMap/Generated";

        public static string Apply(string rootName)
        {
            var root = GameObject.Find(string.IsNullOrEmpty(rootName) ? "1F_Map" : rootName)
                       ?? throw new Exception("루트 없음: " + rootName);
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/BustedMap", "Generated");

            var cache = new Dictionary<string, Mesh>();
            int lower = 0, upper = 0;
            foreach (var seg in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
            {
                foreach (Transform ch in seg.transform)
                {
                    var mf = ch.GetComponent<MeshFilter>();
                    if (mf == null) continue;
                    // 원본 메쉬는 프리팹에서 다시 가져옴 (이전 교체 결과와 무관하게)
                    var srcPrefab = PrefabUtility.GetCorrespondingObjectFromSource(ch.gameObject);
                    var srcMesh = srcPrefab != null ? srcPrefab.GetComponent<MeshFilter>().sharedMesh : mf.sharedMesh;
                    bool isUpper = ch.name.StartsWith("Upper");
                    string key = srcMesh.name + (isUpper ? "_NoBaseboard" : "_NoCrown");
                    if (!cache.TryGetValue(key, out var mesh))
                        cache[key] = mesh = Make(srcMesh, key, isUpper);
                    Undo.RecordObject(mf, "Remove middle trim");
                    mf.sharedMesh = mesh;
                    var mc = ch.GetComponent<MeshCollider>();
                    if (mc != null) { Undo.RecordObject(mc, "Remove middle trim"); mc.sharedMesh = mesh; }
                    if (isUpper) upper++; else lower++;
                }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"중간 띠 제거: 아래 벽/문 {lower}개, 위쪽 덧댐 {upper}개 (메쉬 {cache.Count}종)";
        }

        static Mesh Make(Mesh src, string name, bool removeBaseboard)
        {
            string path = $"{Dir}/{name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);

            var m = UnityEngine.Object.Instantiate(src);
            m.name = name;
            var v = m.vertices; var n = m.normals; var uv = m.uv;

            // 벽 기본면: 정면(+Z)을 향하는 정점 중 가장 안쪽 z (장식은 그보다 튀어나옴)
            float faceZ = float.MaxValue; int refIdx = -1;
            for (int i = 0; i < v.Length; i++)
                if (n[i].z > 0.9f && v[i].z > 0.05f && v[i].y > 0.67f && v[i].y < 2.64f && v[i].z < faceZ) { faceZ = v[i].z; refIdx = i; }
            if (refIdx < 0) throw new Exception(src.name + $": 벽면을 찾지 못함 verts={v.Length} normals={n.Length}");
            var faceUV = uv[refIdx];

            for (int i = 0; i < v.Length; i++)
            {
                bool inBand = removeBaseboard ? v[i].y <= 0.69f : v[i].y >= 2.62f;
                if (!inBand || v[i].z < 0.05f) continue;           // 뒷면/상단 뚜껑 쪽은 그대로
                if (v[i].z > faceZ) v[i].z = faceZ;                  // 튀어나온 장식을 벽면으로
                if (n[i].z > 0.3f) uv[i] = faceUV;                   // 장식 색 → 벽면 색
            }
            m.vertices = v; m.uv = uv;
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
