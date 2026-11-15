using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 2·3층용:
    ///  - 건물 외곽 벽 칸 → Busted_Wall_Plain 프리팹 (문 칸 포함, 외곽엔 문 없음)
    ///  - 내부 벽 칸 → Upper* 제거(높이 약 3m), 꼭대기 몰딩 복원 (디자인은 PolygonOffice 그대로)
    /// arg: 루트이름
    /// </summary>
    public static class BustedUpperFloorsRestyle
    {
        const string PlainPrefab = "Assets/BustedMap/Prefabs/Busted_Wall_Plain.prefab";
        const string Gen = "Assets/BustedMap/Generated";
        const int NX = 24, NZ = 16;

        public static string Apply(string rootName)
        {
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            var plain = AssetDatabase.LoadAssetAtPath<GameObject>(PlainPrefab) ?? throw new Exception("Busted_Wall_Plain 없음");
            var noBaseboard = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/SM_Bld_Wall_Baseboard_01_NoBaseboard.asset");

            int ext = 0, extDoor = 0, inner = 0, uppers = 0;
            var segs = new List<Busted.Map.WallSegment>(root.GetComponentsInChildren<Busted.Map.WallSegment>(true));
            foreach (var s in segs)
            {
                bool exterior = s.vertical ? (s.gridK == 0 || s.gridK == NX) : (s.gridJ == 0 || s.gridJ == NZ);
                if (exterior)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(plain, s.transform.parent);
                    inst.name = s.isDoor ? s.name.Replace("Door_", "Wall_") : s.name;
                    inst.transform.SetPositionAndRotation(s.transform.position, Quaternion.Euler(0, s.vertical ? 90f : 0f, 0));
                    inst.transform.SetSiblingIndex(s.transform.GetSiblingIndex());
                    var ws = inst.GetComponent<Busted.Map.WallSegment>();
                    ws.isDoor = false; ws.vertical = s.vertical; ws.gridK = s.gridK; ws.gridJ = s.gridJ;
                    GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    Undo.RegisterCreatedObjectUndo(inst, "Upper floors restyle");
                    Undo.DestroyObjectImmediate(s.gameObject);
                    ext++; if (s != null && false) { }
                    continue;
                }

                // 내부 벽: Upper 제거 + 꼭대기 몰딩 복원
                var kids = new List<Transform>();
                foreach (Transform c in s.transform) kids.Add(c);
                foreach (var c in kids)
                {
                    if (c.name.StartsWith("Upper")) { Undo.DestroyObjectImmediate(c.gameObject); uppers++; continue; }
                    var mf = c.GetComponent<MeshFilter>();
                    if (mf == null) continue;
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject);
                    var orig = src != null ? src.GetComponent<MeshFilter>()?.sharedMesh : null;
                    Mesh target = null;
                    if (c.name.StartsWith("Wall") || c.name.StartsWith("DoorWallL") || c.name.StartsWith("DoorWallR")) target = orig;   // 원본(걸레받이+몰딩)
                    else if (c.name.StartsWith("DoorWallTop")) target = noBaseboard;                                                   // 문 위: 몰딩만
                    if (target == null) continue;
                    Undo.RecordObject(mf, "Restore crown");
                    mf.sharedMesh = target;
                    var mc = c.GetComponent<MeshCollider>(); if (mc != null) { Undo.RecordObject(mc, "Restore crown"); mc.sharedMesh = target; }
                }
                inner++;
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"{rootName}: 외곽 벽 {ext}칸 → Busted_Wall_Plain, 내부 벽 {inner}칸 높이 축소 (Upper {uppers}개 제거)";
        }
    }
}
