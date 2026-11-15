using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 층의 벽/문을 "wall ex"(PolygonHeist 실내 벽) 스타일로 교체.
    ///  - 일반 벽: SM_Env_Wall_Interior_Ground_01 을 2.5m로 잘라 사용 (1.25m 패널 무늬 유지)
    ///  - 외벽: SM_Env_Wall_Interior_Window_Ground_01 을 2.5m로 잘라 창문 1개씩
    ///  - 문: SM_Env_Wall_Interior_DoubleDoor_01 (5m) + 문짝 2개 → 문 칸 + 옆 벽 칸 1개를 합쳐 배치
    ///  - 높이: 아래 2.74m(원본) + 위쪽 패널 줄을 이어 붙여 약 4.37m, 가운데 띠 없음
    ///  - 안/바깥 양면 동일 디자인, 칸 그룹(WallSegment) 유지
    /// arg: 루트이름 (예: 1F_Map)
    /// </summary>
    public static class BustedHeistRestyle
    {
        const string Env = "Assets/03.Models/Heist/PolygonHeist/Prefab/Environment/";
        const string PlainPath = Env + "SM_Env_Wall_Interior_Ground_01.prefab";
        const string WindowPath = Env + "SM_Env_Wall_Interior_Window_Ground_01.prefab";
        const string DoorWallPath = Env + "SM_Env_Wall_Interior_DoubleDoor_01.prefab";
        const string Leaf1Path = Env + "SM_Env_Door_01.prefab";
        const string Leaf2Path = Env + "SM_Env_Door_02.prefab";
        const string Gen = "Assets/BustedMap/Generated";
        const float G = 2.5f, CutLow = 2.738f, CutHigh = 1.369f; // 패널 줄 홈(groove) 중심
        const int NX = 24, NZ = 16;

        public static string Apply(string rootName)
        {
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            var plainP = Load<GameObject>(PlainPath); var winP = Load<GameObject>(WindowPath);
            var doorP = Load<GameObject>(DoorWallPath); var leaf1P = Load<GameObject>(Leaf1Path); var leaf2P = Load<GameObject>(Leaf2Path);
            Mesh srcPlain = MeshOf(plainP), srcWin = MeshOf(winP), srcDoor = MeshOf(doorP);

            Mesh lowPlain = Derived(srcPlain, "H_LowerPlain25", -2.5f, float.MinValue, CutLow);
            Mesh upPlain = Derived(srcPlain, "H_UpperPlain25", -2.5f, CutHigh, float.MaxValue);
            Mesh lowWin = Derived(srcWin, "H_LowerWindow25", -2.5f, float.MinValue, CutLow);
            Mesh lowDoor = Derived(srcDoor, "H_LowerDoor50", float.MinValue, float.MinValue, CutLow);
            Mesh upPlain50 = Derived(srcPlain, "H_UpperPlain50", float.MinValue, CutHigh, float.MaxValue);
            float upOffset = CutLow - CutHigh;

            // 문짝 상대 위치 (wall ex 기준)
            Pose rel1 = new Pose(new Vector3(-3.493f, 0f, 0.05f), Quaternion.Euler(0, 180, 0));
            Pose rel2 = new Pose(new Vector3(-1.508f, 0f, 0.06f), Quaternion.Euler(0, 180, 0));
            bool relFound = TryLeafRel(ref rel1, ref rel2);

            // 칸 수집
            var segs = new Dictionary<(bool, int, int), Busted.Map.WallSegment>();
            foreach (var s in root.GetComponentsInChildren<Busted.Map.WallSegment>(true)) segs[(s.vertical, s.gridK, s.gridJ)] = s;
            Vector3 origin = root.transform.position;
            float baseY = 0f;

            // 문 짝 지정 (문 칸 + 옆 벽 칸)
            var consumed = new HashSet<(bool, int, int)>();
            var partner = new Dictionary<(bool, int, int), (bool, int, int)?>();
            foreach (var kv in segs)
            {
                if (!kv.Value.isDoor) continue;
                var (v, k, j) = kv.Key;
                (bool, int, int) minus = v ? (v, k, j - 1) : (v, k - 1, j);
                (bool, int, int) plus = v ? (v, k, j + 1) : (v, k + 1, j);
                (bool, int, int) plus2 = v ? (v, k, j + 2) : (v, k + 2, j);
                (bool, int, int) minus2 = v ? (v, k, j - 2) : (v, k - 2, j);
                bool okM = segs.TryGetValue(minus, out var sm) && !sm.isDoor && !consumed.Contains(minus);
                bool okP = segs.TryGetValue(plus, out var sp) && !sp.isDoor && !consumed.Contains(plus);
                (bool, int, int)? pick = null;
                if (okP && (!okM || segs.ContainsKey(plus2))) pick = plus;
                else if (okM) pick = minus;
                if (pick.HasValue) consumed.Add(pick.Value);
                partner[kv.Key] = pick;
            }

            int nPlain = 0, nWin = 0, nDoor = 0, nNarrow = 0;
            foreach (var kv in segs)
            {
                if (consumed.Contains(kv.Key)) continue;
                var seg = kv.Value; var (v, k, j) = kv.Key;
                // 기존 내용 제거
                var kids = new List<GameObject>();
                foreach (Transform c in seg.transform) kids.Add(c.gameObject);
                foreach (var c in kids) Undo.DestroyObjectImmediate(c);

                float line = v ? origin.x + k * G : origin.z + j * G;
                if (!seg.isDoor)
                {
                    bool exterior = v ? (k == 0 || k == NX) : (j == 0 || j == NZ);
                    float start = (v ? origin.z + j * G : origin.x + k * G);
                    foreach (bool plus in new[] { true, false })
                    {
                        string sfx = plus ? "_A" : "_B";
                        Place(exterior ? winP : plainP, exterior ? lowWin : lowPlain, seg.transform, v, plus, start, G, line, baseY, 1f, "Wall" + sfx);
                        Place(plainP, upPlain, seg.transform, v, plus, start, G, line, baseY + upOffset, 1f, "Upper" + sfx);
                    }
                    if (exterior) nWin++; else nPlain++;
                }
                else
                {
                    var p = partner[kv.Key];
                    float s0 = v ? origin.z + j * G : origin.x + k * G;
                    float start = s0, len = 5f, sx = 1f;
                    if (p.HasValue)
                    {
                        var (_, pk, pj) = p.Value;
                        float ps = v ? origin.z + pj * G : origin.x + pk * G;
                        start = Mathf.Min(s0, ps);
                        var pseg = segs[p.Value];
                        Undo.DestroyObjectImmediate(pseg.gameObject);
                    }
                    else { len = G; sx = 0.5f; nNarrow++; }
                    // 그룹 기준점을 문 전체 폭 중앙으로
                    var center = v ? new Vector3(line, baseY, start + len / 2) : new Vector3(start + len / 2, baseY, line);
                    Undo.RecordObject(seg.transform, "Restyle");
                    seg.transform.position = center;
                    foreach (bool plus in new[] { true, false })
                    {
                        string sfx = plus ? "_A" : "_B";
                        var d = Place(doorP, lowDoor, seg.transform, v, plus, start, len, line, baseY, sx, "DoorWall" + sfx);
                        Place(plainP, len >= 5f ? upPlain50 : upPlain, seg.transform, v, plus, start, len, line, baseY + upOffset, len >= 5f ? 1f : 1f, "Upper" + sfx);
                        if (plus)
                        {
                            Leaf(leaf1P, d.transform, rel1, seg.transform, "DoorLeaf_L", sx);
                            Leaf(leaf2P, d.transform, rel2, seg.transform, "DoorLeaf_R", sx);
                        }
                    }
                    nDoor++;
                }
                Undo.RecordObject(seg.gameObject, "Restyle");
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"{rootName} Heist 스타일 적용: 일반벽 {nPlain}, 창문벽(외벽) {nWin}, 양문 {nDoor} (좁은 문 {nNarrow}), 문짝 위치 {(relFound ? "wall ex 기준" : "기본값")}";
        }

        // 원본 프리팹(머티리얼 유지) + 가공 메쉬로 한 조각 배치. 메쉬 x∈[-L,0], 앞면 +Z
        static GameObject Place(GameObject prefab, Mesh mesh, Transform parent, bool vertical, bool facePlus,
                                float start, float len, float line, float y, float sx, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            foreach (var mc in go.GetComponentsInChildren<MeshCollider>()) mc.sharedMesh = mesh;
            float meshLen = mesh.bounds.size.x * sx;
            float rot; Vector3 pos;
            if (!vertical)
            {
                rot = facePlus ? 0f : 180f;
                float px = facePlus ? start + meshLen : start;
                pos = new Vector3(px, y, line);
            }
            else
            {
                rot = facePlus ? 90f : -90f;
                float pz = facePlus ? start : start + meshLen;
                pos = new Vector3(line, y, pz);
            }
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rot, 0));
            go.transform.localScale = new Vector3(sx, 1, 1);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        static void Leaf(GameObject prefab, Transform doorPiece, Pose rel, Transform parent, string name, float sx)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            var lp = rel.position; lp.x *= 1f; // 문 조각 로컬 좌표 (스케일은 TransformPoint가 반영)
            go.transform.position = doorPiece.TransformPoint(lp);
            go.transform.rotation = doorPiece.rotation * rel.rotation;
            go.transform.localScale = new Vector3(sx, 1, 1);
        }

        /// wall ex 안의 양문 벽과 문짝으로부터 상대 위치 계산
        static bool TryLeafRel(ref Pose r1, ref Pose r2)
        {
            var ex = GameObject.Find("wall ex");
            if (ex == null) return false;
            Transform wall = null, l1 = null, l2 = null;
            foreach (Transform c in ex.transform)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject);
                var path = src != null ? AssetDatabase.GetAssetPath(src) : "";
                if (path.EndsWith("SM_Env_Wall_Interior_DoubleDoor_01.prefab") && wall == null) wall = c;
                else if (path.EndsWith("SM_Env_Door_01.prefab")) l1 = c;
                else if (path.EndsWith("SM_Env_Door_02.prefab")) l2 = c;
            }
            if (wall == null || l1 == null || l2 == null) return false;
            r1 = new Pose(wall.InverseTransformPoint(l1.position), Quaternion.Inverse(wall.rotation) * l1.rotation);
            r2 = new Pose(wall.InverseTransformPoint(l2.position), Quaternion.Inverse(wall.rotation) * l2.rotation);
            return true;
        }

        /// 메쉬 가공(비파괴: 이미 있으면 재사용). x는 minX 이하를 minX로, y는 [yMin, yMax]로 눌러 자름
        static Mesh Derived(Mesh src, string name, float minX, float yMin, float yMax)
        {
            string path = $"{Gen}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var m = UnityEngine.Object.Instantiate(src);
            m.name = name;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].x < minX) v[i].x = minX;
                if (v[i].y < yMin) v[i].y = yMin;
                if (v[i].y > yMax) v[i].y = yMax;
            }
            m.vertices = v;
            m.RecalculateBounds();
            if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder("Assets/BustedMap", "Generated");
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Mesh MeshOf(GameObject p) => p.GetComponentInChildren<MeshFilter>().sharedMesh;
        static T Load<T>(string p) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(p) ?? throw new Exception("에셋 없음: " + p);
    }
}
