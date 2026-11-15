using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 벽 메쉬 참조 복구(비파괴). 이미 있는 Generated 메쉬를 불러와 다시 연결만 한다.
    ///  - Wall_* (아래 3m)  → {원본}_NoCrown
    ///  - Upper* (위 1.5m)  → {원본}_NoBaseboard
    ///  - Door_* / DoorWall* 는 건드리지 않음
    /// arg: 루트이름
    /// </summary>
    public static class BustedMeshRepair
    {
        const string Dir = "Assets/BustedMap/Generated";

        public static string Repair(string rootName)
        {
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            int fixedLower = 0, fixedUpper = 0, missing = 0;
            foreach (var seg in root.GetComponentsInChildren<Busted.Map.WallSegment>(true))
            {
                foreach (Transform ch in seg.transform)
                {
                    bool upper = ch.name.StartsWith("Upper");
                    bool lower = ch.name.StartsWith("Wall");
                    if (!upper && !lower) continue;
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(ch.gameObject);
                    if (src == null) { missing++; continue; }
                    var srcMesh = src.GetComponent<MeshFilter>().sharedMesh;
                    var path = $"{Dir}/{srcMesh.name}{(upper ? "_NoBaseboard" : "_NoCrown")}.asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh == null) { missing++; continue; }
                    var mf = ch.GetComponent<MeshFilter>();
                    Undo.RecordObject(mf, "Mesh repair");
                    mf.sharedMesh = mesh;
                    var mc = ch.GetComponent<MeshCollider>();
                    if (mc != null) { Undo.RecordObject(mc, "Mesh repair"); mc.sharedMesh = mesh; }
                    if (upper) fixedUpper++; else fixedLower++;
                }
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"{rootName} 메쉬 복구: 아래 {fixedLower}, 위 {fixedUpper}, 실패 {missing}";
        }
    }
}
