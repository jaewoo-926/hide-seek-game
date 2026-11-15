using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 벽 프리팹 3종 생성 + 층의 모든 벽 칸을 프리팹으로 교체.
    ///  Assets/BustedMap/Prefabs/Busted_Wall_Plain.prefab   (2.5m, 양면 일반 벽)
    ///  Assets/BustedMap/Prefabs/Busted_Wall_Window.prefab  (2.5m, 양면 창문 벽)
    ///  Assets/BustedMap/Prefabs/Busted_Wall_Door.prefab    (5m,   양면 양문 벽 + 문짝 2개)
    /// 각 면은 메쉬 1개(아래+위를 합친 약 4.37m 메쉬) → 머티리얼 교체가 한 번에 됨.
    /// 프리팹 기준점 = 벽 중앙 바닥, 로컬 X = 벽 길이 방향, Face_A 는 +Z 쪽.
    /// arg: 루트이름
    /// </summary>
    public static class BustedWallPrefabs3
    {
        const string Env = "Assets/03.Models/Heist/PolygonHeist/Prefab/Environment/";
        const string Gen = "Assets/BustedMap/Generated";
        const string PrefabDir = "Assets/BustedMap/Prefabs";
        const float CutLow = 2.738f, CutHigh = 1.369f, Thick = 0.164f;

        public static string Build(string rootName)
        {
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/BustedMap", "Prefabs");

            var plainSrc = AssetDatabase.LoadAssetAtPath<GameObject>(Env + "SM_Env_Wall_Interior_Ground_01.prefab");
            var mat = plainSrc.GetComponentInChildren<MeshRenderer>().sharedMaterials;
            // 창문/문 벽은 원본 프리팹의 머티리얼(유리 색 포함)을 사용
            var matWin = AssetDatabase.LoadAssetAtPath<GameObject>(Env + "SM_Env_Wall_Interior_Window_Ground_01.prefab").GetComponentInChildren<MeshRenderer>().sharedMaterials;
            var matDoor = AssetDatabase.LoadAssetAtPath<GameObject>(Env + "SM_Env_Wall_Interior_DoubleDoor_01.prefab").GetComponentInChildren<MeshRenderer>().sharedMaterials;
            float up = CutLow - CutHigh;

            // 합친 메쉬 (아래 + 위) — 기존 Heist 가공 메쉬 재사용
            Mesh plain = Combine("H_Plain25_Tall", Load("H_LowerPlain25"), Load("H_UpperPlain25"), up);
            Mesh window = Combine("H_Window25_Tall", Load("H_LowerWindow25"), Load("H_UpperPlain25"), up);
            Mesh door = Combine("H_Door50_Tall", Load("H_LowerDoor50"), Load("H_UpperPlain50"), up);
            float H = plain.bounds.max.y;

            // 문짝 상대 위치 (wall ex 기준, 문 조각 로컬)
            GameObject leaf1 = AssetDatabase.LoadAssetAtPath<GameObject>(Env + "SM_Env_Door_01.prefab");
            GameObject leaf2 = AssetDatabase.LoadAssetAtPath<GameObject>(Env + "SM_Env_Door_02.prefab");
            Pose r1 = new Pose(new Vector3(-3.493f, 0, 0.05f), Quaternion.Euler(0, 180, 0)), r2 = new Pose(new Vector3(-1.508f, 0, 0.06f), Quaternion.Euler(0, 180, 0));
            LeafRel(ref r1, ref r2);

            var pPlain = MakePrefab("Busted_Wall_Plain", plain, mat, 2.5f, H, false, null, null, r1, r2);
            var pWindow = MakePrefab("Busted_Wall_Window", window, matWin, 2.5f, H, false, null, null, r1, r2, true);
            var pDoor = MakePrefab("Busted_Wall_Door", door, matDoor, 5f, H, true, leaf1, leaf2, r1, r2);

            // 교체
            int nP = 0, nW = 0, nD = 0;
            var segs = new List<Busted.Map.WallSegment>(root.GetComponentsInChildren<Busted.Map.WallSegment>(true));
            foreach (var s in segs)
            {
                if (s == null || PrefabUtility.IsAnyPrefabInstanceRoot(s.gameObject) &&
                    PrefabUtility.GetCorrespondingObjectFromSource(s.gameObject) is GameObject src && src.name.StartsWith("Busted_Wall_")) continue;
                GameObject prefab;
                if (s.isDoor || s.transform.Find("DoorWall_A") != null) { prefab = pDoor; nD++; }
                else if (IsWindow(s.transform)) { prefab = pWindow; nW++; }
                else { prefab = pPlain; nP++; }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, s.transform.parent);
                inst.name = s.name;
                inst.transform.SetPositionAndRotation(new Vector3(s.transform.position.x, s.transform.position.y, s.transform.position.z),
                                                      Quaternion.Euler(0, s.vertical ? 90f : 0f, 0));
                inst.transform.SetSiblingIndex(s.transform.GetSiblingIndex());
                var ws = inst.GetComponent<Busted.Map.WallSegment>();
                ws.isDoor = prefab == pDoor; ws.vertical = s.vertical; ws.gridK = s.gridK; ws.gridJ = s.gridJ;
                GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                Undo.RegisterCreatedObjectUndo(inst, "Wall prefabs");
                Undo.DestroyObjectImmediate(s.gameObject);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"[머티리얼] 일반={Names(mat)} / 창문={Names(matWin)} / 문={Names(matDoor)}\n{rootName}: 일반 {nP}, 창문 {nW}, 문 {nD} 칸을 프리팹으로 교체 (벽 높이 {H:0.00}m)";
        }

        static string Names(Material[] m) { var s = ""; foreach (var x in m) s += (x ? x.name : "null") + ","; return s; }

        static bool IsWindow(Transform seg)
        {
            foreach (var mf in seg.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.sharedMesh.name.Contains("Window")) return true;
            return false;
        }

        static GameObject MakePrefab(string name, Mesh mesh, Material[] mat, float L, float H, bool isDoor,
                                     GameObject leaf1, GameObject leaf2, Pose r1, Pose r2, bool glass = false)
        {
            var go = new GameObject(name);
            go.AddComponent<Busted.Map.WallSegment>().isDoor = isDoor;
            // 면 A (+Z), 면 B (-Z) — 메쉬 x∈[-L,0]
            var a = Face(go.transform, "Face_A", mesh, mat, new Vector3(L / 2, 0, 0), 0f);
            var b = Face(go.transform, "Face_B", mesh, mat, new Vector3(-L / 2, 0, 0), 180f);
            if (glass)
            {
                var gp = AssetDatabase.LoadAssetAtPath<GameObject>(Env + "SM_Env_Wall_Interior_Window_Ground_01.prefab");
                var gt = gp.transform.Find("SM_Env_Wall_Interior_Window_Ground_Glass_01");
                var gMesh = ClampX(gt.GetComponent<MeshFilter>().sharedMesh, "H_WindowGlass25", -2.5f);
                var gMat = gt.GetComponent<MeshRenderer>().sharedMaterials;
                foreach (var f in new[] { a, b })
                {
                    var g = new GameObject("Glass");
                    g.transform.SetParent(f.transform, false);
                    g.AddComponent<MeshFilter>().sharedMesh = gMesh;
                    g.AddComponent<MeshRenderer>().sharedMaterials = gMat;
                }
            }
            if (isDoor)
            {
                // 문짝 (면 A 기준)
                AddLeaf(go.transform, leaf1, a.transform, r1, "DoorLeaf_L");
                AddLeaf(go.transform, leaf2, a.transform, r2, "DoorLeaf_R");
                // 충돌: 문 개구부(가운데 약 2m)는 비워둠
                float half = L / 2, open = 1.0f, top = 2.35f;
                Box(go, new Vector3(-(half + open) / 2, H / 2, 0), new Vector3(half - open, H, Thick * 2));
                Box(go, new Vector3((half + open) / 2, H / 2, 0), new Vector3(half - open, H, Thick * 2));
                Box(go, new Vector3(0, (top + H) / 2, 0), new Vector3(open * 2, H - top, Thick * 2));
            }
            else Box(go, new Vector3(0, H / 2, 0), new Vector3(L, H, Thick * 2));

            string path = $"{PrefabDir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject Face(Transform parent, string name, Mesh mesh, Material[] mat, Vector3 pos, float rotY)
        {
            var f = new GameObject(name);
            f.transform.SetParent(parent, false);
            f.transform.localPosition = pos;
            f.transform.localRotation = Quaternion.Euler(0, rotY, 0);
            f.AddComponent<MeshFilter>().sharedMesh = mesh;
            f.AddComponent<MeshRenderer>().sharedMaterials = mat;
            return f;
        }

        static void AddLeaf(Transform parent, GameObject prefab, Transform face, Pose rel, string name)
        {
            if (prefab == null) return;
            var l = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            l.name = name;
            l.transform.localPosition = face.localPosition + face.localRotation * rel.position;
            l.transform.localRotation = face.localRotation * rel.rotation;
            foreach (var c in l.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
        }

        static void Box(GameObject go, Vector3 c, Vector3 s) { var b = go.AddComponent<BoxCollider>(); b.center = c; b.size = s; }

        static void LeafRel(ref Pose r1, ref Pose r2)
        {
            var ex = GameObject.Find("wall ex");
            if (ex == null) return;
            Transform wall = null, l1 = null, l2 = null;
            foreach (Transform c in ex.transform)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject);
                var p = src != null ? AssetDatabase.GetAssetPath(src) : "";
                if (p.EndsWith("SM_Env_Wall_Interior_DoubleDoor_01.prefab") && wall == null) wall = c;
                else if (p.EndsWith("SM_Env_Door_01.prefab")) l1 = c;
                else if (p.EndsWith("SM_Env_Door_02.prefab")) l2 = c;
            }
            if (wall == null || l1 == null || l2 == null) return;
            r1 = new Pose(wall.InverseTransformPoint(l1.position), Quaternion.Inverse(wall.rotation) * l1.rotation);
            r2 = new Pose(wall.InverseTransformPoint(l2.position), Quaternion.Inverse(wall.rotation) * l2.rotation);
        }

        static Mesh ClampX(Mesh src, string name, float minX)
        {
            string path = $"{Gen}/{name}.asset";
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (ex != null) return ex;
            var m = UnityEngine.Object.Instantiate(src); m.name = name;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) if (v[i].x < minX) v[i].x = minX;
            m.vertices = v; m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Mesh Load(string n) => AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/{n}.asset") ?? throw new Exception($"{n} 메쉬 없음");

        static Mesh Combine(string name, Mesh lower, Mesh upper, float upOffset)
        {
            string path = $"{Gen}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var m = new Mesh { name = name };
            m.CombineMeshes(new[]
            {
                new CombineInstance { mesh = lower, transform = Matrix4x4.identity },
                new CombineInstance { mesh = upper, transform = Matrix4x4.Translate(new Vector3(0, upOffset, 0)) }
            }, true, true);
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
