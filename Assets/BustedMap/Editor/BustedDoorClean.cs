using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 문 칸을 "평평한 일반 벽 + 문틀"로 재구성.
    ///  - 문 프리팹 메쉬에서 문틀(개구부 주변)만 추출
    ///  - 문 좌/우/위는 일반 벽 프리팹(평탄화 메쉬)을 잘라 채움 → 옆 벽과 완전히 같은 면
    /// 문 칸의 위치/그룹/위쪽 덧댐은 그대로 둔다.
    /// </summary>
    public static class BustedDoorClean
    {
        const string Dir = "Assets/BustedMap/Generated";
        // 문 프리팹 기준 개구부(문틀 바깥선) 범위 — 메쉬 분석값
        const float OpenX0 = -1.833f, OpenX1 = -0.667f, OpenTop = 2.049f, Len = 2.5f;

        public static string Apply(string rootName)
        {
            var root = GameObject.Find(string.IsNullOrEmpty(rootName) ? "1F_Map" : rootName)
                       ?? throw new Exception("루트 없음: " + rootName);
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/BustedMap", "Generated");

            GameObject wallPrefab = null; Mesh wallSrc = null, doorSrc = null;
            // 일반 벽 프리팹 찾기 (같은 맵 안의 Wall_A 등)
            foreach (var seg in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
            {
                if (seg.isDoor) continue;
                foreach (Transform ch in seg.transform)
                {
                    if (!ch.name.StartsWith("Wall")) continue;
                    var p = PrefabUtility.GetCorrespondingObjectFromSource(ch.gameObject);
                    if (p != null) { wallPrefab = p; wallSrc = p.GetComponent<MeshFilter>().sharedMesh; break; }
                }
                if (wallPrefab != null) break;
            }
            if (wallPrefab == null) throw new Exception("일반 벽 프리팹을 찾지 못함");

            Mesh wallLower = null, wallNoTrim = null, frame = null;
            int doors = 0;
            foreach (var seg in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
            {
                if (!seg.isDoor) continue;
                // 이전 실행 결과 제거 (재실행 안전)
                var toRemove = new List<GameObject>();
                foreach (Transform ch in seg.transform) if (ch.name.StartsWith("DoorWall")) toRemove.Add(ch.gameObject);
                foreach (var g in toRemove) Undo.DestroyObjectImmediate(g);

                var doorChildren = new List<Transform>();
                foreach (Transform ch in seg.transform) if (ch.name.StartsWith("Door")) doorChildren.Add(ch);
                foreach (var d in doorChildren)
                {
                    var mf = d.GetComponent<MeshFilter>();
                    if (doorSrc == null)
                    {
                        var dp = PrefabUtility.GetCorrespondingObjectFromSource(d.gameObject);
                        doorSrc = dp != null ? dp.GetComponent<MeshFilter>().sharedMesh : mf.sharedMesh;
                        wallLower = Flatten(wallSrc, "Wall_Lower_NoCrown", crown: true, baseboard: false);
                        wallNoTrim = Flatten(wallSrc, "Wall_NoTrim", crown: true, baseboard: true);
                        frame = ExtractFrame(doorSrc, "Door_FrameOnly");
                    }
                    Undo.RecordObject(mf, "Door clean");
                    mf.sharedMesh = frame;
                    var mc = d.GetComponent<MeshCollider>();
                    if (mc != null) { Undo.RecordObject(mc, "Door clean"); mc.sharedMesh = frame; }

                    string side = d.name.Length > 4 ? d.name.Substring(4) : ""; // "_A" / "_B"
                    float leftW = Len + OpenX0;           // 0.667 (x: -2.5 ~ -1.833)
                    float rightW = -OpenX1;               // 0.667 (x: -0.667 ~ 0)
                    float headW = OpenX1 - OpenX0;        // 1.166
                    float baseH = wallSrc.bounds.size.y;  // 2.997
                    Piece(wallPrefab, wallLower, seg.transform, d, "DoorWallL" + side, OpenX0, 0f, leftW / Len, 1f);
                    Piece(wallPrefab, wallLower, seg.transform, d, "DoorWallR" + side, 0f, 0f, rightW / Len, 1f);
                    Piece(wallPrefab, wallNoTrim, seg.transform, d, "DoorWallTop" + side, OpenX1, OpenTop, headW / Len, (baseH - OpenTop) / baseH);
                    doors++;
                }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"문 {doors}면 재구성 (문틀 + 평평한 벽)";
        }

        /// 문 오브젝트 d의 로컬 좌표계 기준 (pivotX, y)에 벽 조각 배치. 벽 메쉬는 x∈[-2.5,0] 이므로 pivot이 오른쪽 끝.
        static void Piece(GameObject prefab, Mesh mesh, Transform parent, Transform d, string name, float pivotX, float y, float sx, float sy)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.rotation = d.rotation;
            go.transform.position = d.TransformPoint(new Vector3(pivotX, y, 0));
            go.transform.localScale = new Vector3(sx, sy, 1);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mc = go.GetComponent<MeshCollider>(); if (mc != null) mc.sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(d.gameObject));
            Undo.RegisterCreatedObjectUndo(go, "Door clean");
        }

        static Mesh Save(Mesh m, string name)
        {
            string path = $"{Dir}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Mesh Flatten(Mesh src, string name, bool crown, bool baseboard)
        {
            var m = UnityEngine.Object.Instantiate(src);
            var v = m.vertices; var n = m.normals; var uv = m.uv;
            float faceZ = float.MaxValue; int refIdx = -1;
            for (int i = 0; i < v.Length; i++)
                if (n[i].z > 0.9f && v[i].z > 0.05f && v[i].y > 0.67f && v[i].y < 2.64f && v[i].z < faceZ) { faceZ = v[i].z; refIdx = i; }
            if (refIdx < 0) throw new Exception(src.name + ": 벽면을 찾지 못함");
            var faceUV = uv[refIdx];
            for (int i = 0; i < v.Length; i++)
            {
                bool inBand = (crown && v[i].y >= 2.62f) || (baseboard && v[i].y <= 0.69f);
                if (!inBand || v[i].z < 0.05f) continue;
                if (v[i].z > faceZ) v[i].z = faceZ;
                if (n[i].z > 0.3f) { uv[i] = faceUV; n[i] = Vector3.forward; }
            }
            m.vertices = v; m.uv = uv; m.normals = n;
            m.RecalculateBounds();
            return Save(m, name);
        }

        /// 문 메쉬에서 개구부 주변(문틀·문설주·문턱) 삼각형만 남김
        static Mesh ExtractFrame(Mesh src, string name)
        {
            var v = src.vertices;
            const float e = 0.01f;
            var keep = new List<int>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var t = src.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    bool ok = true;
                    for (int k = 0; k < 3; k++)
                    {
                        var p = v[t[i + k]];
                        if (p.x < OpenX0 - e || p.x > OpenX1 + e || p.y > OpenTop + e) { ok = false; break; }
                    }
                    if (ok) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                }
            }
            var m = UnityEngine.Object.Instantiate(src);
            m.subMeshCount = 1;
            m.SetTriangles(keep, 0);
            m.RecalculateBounds();
            return Save(m, name);
        }
    }
}
