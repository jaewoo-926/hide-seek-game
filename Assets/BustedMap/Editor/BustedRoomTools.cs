using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 방 그룹 풀기 / 다시 묶기 메뉴.
    ///  - Busted ▸ 방 그룹 ▸ 선택한 방 그룹 풀기 : Hierarchy에서 Room_xxx 를 선택하고 실행
    ///  - Busted ▸ 방 그룹 ▸ 이 층 전체 풀기      : 1F_Map 같은 층 루트(또는 그 안의 아무 오브젝트)를 선택하고 실행
    ///  - Busted ▸ 방 그룹 ▸ 이 층 다시 묶기      : 층 루트를 선택하고 실행 (설계도 기준으로 다시 그룹화)
    /// 모두 Ctrl+Z 로 되돌릴 수 있다. 벽 칸(안/바깥면) 그룹은 유지된다.
    /// </summary>
    public static class BustedRoomTools
    {
        [MenuItem("Busted/방 그룹/선택한 방 그룹 풀기", priority = 20)]
        static void UngroupSelected()
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
                if (go != null && go.name.StartsWith("Room_")) { UngroupRoom(go.transform); n++; }
            if (n == 0) EditorUtility.DisplayDialog("방 그룹 풀기", "Hierarchy에서 Room_ 으로 시작하는 오브젝트를 선택하세요.", "확인");
            else Debug.Log($"[Busted] 방 그룹 {n}개를 풀었습니다.");
        }

        [MenuItem("Busted/방 그룹/이 층 전체 풀기", priority = 21)]
        static void UngroupFloorMenu()
        {
            var root = SelectedFloorRoot();
            if (root == null) { EditorUtility.DisplayDialog("방 그룹 풀기", "1F_Map 같은 층 오브젝트를 선택하세요.", "확인"); return; }
            Debug.Log("[Busted] " + UngroupFloor(root.name));
        }

        [MenuItem("Busted/방 그룹/이 층 다시 묶기", priority = 22)]
        static void RegroupFloorMenu()
        {
            var root = SelectedFloorRoot();
            if (root == null) { EditorUtility.DisplayDialog("방 그룹 묶기", "1F_Map 같은 층 오브젝트를 선택하세요.", "확인"); return; }
            string f = root.name.Substring(0, 1); // "1F_Map" → "1"
            string json = System.IO.Path.GetFullPath($"Assets/BustedMap/F{f}_rooms.json");
            Debug.Log("[Busted] " + BustedRoomGrouper2.Apply($"{json}|{root.name}"));
        }

        /// <summary>브리지/스크립트용: 층 전체 방 그룹 풀기</summary>
        public static string UngroupFloor(string rootName)
        {
            var root = GameObject.Find(rootName);
            if (root == null) return "루트 없음: " + rootName;
            var rooms = new List<Transform>();
            foreach (Transform ch in root.transform) if (ch.name.StartsWith("Room_")) rooms.Add(ch);
            foreach (var r in rooms) UngroupRoom(r);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return $"{rootName}: 방 그룹 {rooms.Count}개 해제";
        }

        static void UngroupRoom(Transform room)
        {
            var root = room.parent;
            foreach (var kind in new[] { "Walls", "Doors", "Floor" })
            {
                var src = room.Find(kind);
                if (src == null) continue;
                var dst = root.Find(kind);
                if (dst == null)
                {
                    var go = new GameObject(kind);
                    Undo.RegisterCreatedObjectUndo(go, "Ungroup room");
                    go.transform.SetParent(root, false);
                    dst = go.transform;
                }
                var kids = new List<Transform>();
                foreach (Transform k in src) kids.Add(k);
                foreach (var k in kids) Undo.SetTransformParent(k, dst, "Ungroup room");
            }
            // 사용자가 직접 넣은 다른 오브젝트는 층 루트로 옮김
            var rest = new List<Transform>();
            foreach (Transform k in room) if (k.name != "Walls" && k.name != "Doors" && k.name != "Floor") rest.Add(k);
            foreach (var k in rest) Undo.SetTransformParent(k, root, "Ungroup room");
            Undo.DestroyObjectImmediate(room.gameObject);
        }

        static Transform SelectedFloorRoot()
        {
            var t = Selection.activeTransform;
            while (t != null && !t.name.EndsWith("F_Map")) t = t.parent;
            return t;
        }
    }
}
