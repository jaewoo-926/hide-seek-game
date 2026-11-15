using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>방 디자인 Inspector: 벽/바닥 디자인(메쉬)과 머티리얼을 넣는 즉시 그 방에만 적용.</summary>
    [CustomEditor(typeof(Busted.Map.RoomStyle))]
    public class RoomStyleEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var rs = (Busted.Map.RoomStyle)target;
            EditorGUILayout.HelpBox(rs.exterior
                ? "층 전체 설정입니다.\n· 외벽 바깥면 디자인/머티리얼: 건물 바깥쪽 면만\n· 기본 내부 벽 / 기본 바닥: 방에 따로 넣지 않은 곳 전부"
                : "이 방 안쪽을 바라보는 벽면(문 달린 벽 포함)과 이 방 바닥만 바뀝니다.\n디자인 칸에는 벽/바닥 프리팹이나 모델을 끌어다 넣으세요. 비우면 층 기본값으로 돌아갑니다.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            GameObject wallD, floorD = rs.floorDesign, defIn = rs.defaultInteriorWall, defFl = rs.defaultFloor;
            Material wallM, floorM = rs.floorMaterial;
            bool flip;
            if (rs.exterior)
            {
                EditorGUILayout.LabelField("외벽 바깥면", EditorStyles.boldLabel);
                wallD = (GameObject)EditorGUILayout.ObjectField("디자인", rs.wallDesign, typeof(GameObject), false);
                wallM = (Material)EditorGUILayout.ObjectField("머티리얼", rs.wallMaterial, typeof(Material), false);
                flip = EditorGUILayout.Toggle("앞뒤 뒤집기", rs.flipWallDesign);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("층 기본값", EditorStyles.boldLabel);
                defIn = (GameObject)EditorGUILayout.ObjectField("기본 내부 벽 디자인", rs.defaultInteriorWall, typeof(GameObject), false);
                defFl = (GameObject)EditorGUILayout.ObjectField("기본 바닥 디자인", rs.defaultFloor, typeof(GameObject), false);
            }
            else
            {
                EditorGUILayout.LabelField("벽", EditorStyles.boldLabel);
                wallD = (GameObject)EditorGUILayout.ObjectField("디자인 (메쉬)", rs.wallDesign, typeof(GameObject), false);
                wallM = (Material)EditorGUILayout.ObjectField("머티리얼", rs.wallMaterial, typeof(Material), false);
                flip = EditorGUILayout.Toggle("앞뒤 뒤집기", rs.flipWallDesign);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("바닥", EditorStyles.boldLabel);
                floorD = (GameObject)EditorGUILayout.ObjectField("디자인 (메쉬)", rs.floorDesign, typeof(GameObject), false);
                floorM = (Material)EditorGUILayout.ObjectField("머티리얼", rs.floorMaterial, typeof(Material), false);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rs, "방 디자인 변경");
                rs.wallDesign = wallD; rs.wallMaterial = wallM; rs.flipWallDesign = flip;
                rs.floorDesign = floorD; rs.floorMaterial = floorM;
                rs.defaultInteriorWall = defIn; rs.defaultFloor = defFl;
                if (wallD != null && BustedWallSystem.DesignFilter(wallD) == null)
                    EditorUtility.DisplayDialog("방 디자인", "넣은 오브젝트에 메쉬가 없습니다.", "확인");
                else BustedWallSystem.Apply(rs);
            }

            var faces = BustedWallSystem.FacesOf(rs);
            int floors = rs.exterior ? 0 : BustedWallSystem.FloorTiles(rs).Count;
            EditorGUILayout.LabelField($"대상: 벽면 {faces.Count}개" + (rs.exterior ? "" : $", 바닥 {floors}장"), EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("다시 적용")) BustedWallSystem.Apply(rs);
            if (GUILayout.Button("대상 벽면 선택")) Selection.objects = faces.Select(r => (Object)r.gameObject).ToArray();
            if (!rs.exterior && GUILayout.Button("원래 디자인으로"))
            {
                Undo.RecordObject(rs, "방 디자인 초기화");
                rs.wallDesign = null; rs.wallMaterial = null; rs.flipWallDesign = false; rs.floorDesign = null; rs.floorMaterial = null;
                BustedWallSystem.Apply(rs);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    public static class RoomStyleMenu
    {
        [MenuItem("Busted/방 디자인/선택한 벽·바닥이 속한 방 선택 %#r", priority = 40)]
        static void SelectRoom()
        {
            var go = Selection.activeGameObject; if (go == null) return;
            var t = go.transform;
            Transform map = t; while (map.parent != null) map = map.parent;
            // 벽면(Face_A/B)을 고른 경우: 그 면이 바라보는 방
            if (t.name.StartsWith("Face_"))
                foreach (var (r, room) in BustedWallSystem.AllFaces(map))
                    if (r.transform == t) { Selection.activeObject = room != null ? room.gameObject : map.gameObject; return; }
            while (t != null && !t.name.StartsWith("Room_")) t = t.parent;
            if (t != null) Selection.activeObject = t.gameObject;
        }
    }
}
