using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    public static class BustedPrefabSwap
    {
        /// arg: 프리팹 이름 → 프로젝트 내 경로와, 현재 씬의 인스턴스 목록(위치/회전/부모) 출력
        public static string Find(string name)
        {
            var sb = new StringBuilder();
            foreach (var g in AssetDatabase.FindAssets(name + " t:Prefab"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) != name) continue;
                sb.AppendLine("asset: " + p);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    var mf = t.GetComponent<MeshFilter>();
                    sb.AppendLine($"   {t.name} lp={t.localPosition} lr={t.localEulerAngles}" + (mf ? $" mesh={mf.sharedMesh?.name} b={mf.sharedMesh?.bounds}" : ""));
                }
            }
            int n = 0;
            foreach (var r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                {
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                    if (src == null || src.name != name) continue;
                    n++;
                    if (n <= 40) sb.AppendLine($"instance: {Path(t)} pos={t.position} rot={t.eulerAngles} scl={t.localScale}");
                }
            sb.AppendLine($"instances in scene: {n}");
            return sb.ToString();
        }

        /// arg: "원본프리팹이름|새프리팹경로" — 씬의 원본 인스턴스를 같은 위치/회전/부모로 새 프리팹으로 교체
        public static string Swap(string arg)
        {
            var p = arg.Split('|');
            string fromName = p[0];
            var to = AssetDatabase.LoadAssetAtPath<GameObject>(p[1]) ?? throw new Exception("새 프리팹 없음: " + p[1]);
            var list = new List<Transform>();
            foreach (var r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                {
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                    if (src != null && src.name == fromName) list.Add(t);
                }
            foreach (var t in list)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(to, t.parent);
                inst.name = t.name;
                inst.transform.SetPositionAndRotation(t.position, t.rotation);
                inst.transform.SetSiblingIndex(t.GetSiblingIndex());
                var oldSeg = t.GetComponent<Busted.Map.WallSegment>();
                var newSeg = inst.GetComponent<Busted.Map.WallSegment>();
                if (oldSeg != null && newSeg != null) { newSeg.vertical = oldSeg.vertical; newSeg.gridK = oldSeg.gridK; newSeg.gridJ = oldSeg.gridJ; }
                GameObjectUtility.SetStaticEditorFlags(inst, GameObjectUtility.GetStaticEditorFlags(t.gameObject));
                Undo.RegisterCreatedObjectUndo(inst, "Prefab swap");
                Undo.DestroyObjectImmediate(t.gameObject);
            }
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"{fromName} → {to.name}: {list.Count}개 교체";
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
