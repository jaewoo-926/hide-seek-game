using System;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    public static class BustedRoomStyleSetup
    {
        /// arg: 루트이름 — 층 루트(외벽 바깥면)와 모든 Room_ 에 RoomStyle 추가 + 방별 대상 개수 출력
        public static string Add(string rootName)
        {
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            var ext = root.GetComponent<Busted.Map.RoomStyle>() ?? root.AddComponent<Busted.Map.RoomStyle>();
            ext.exterior = true;
            var sb = new StringBuilder();
            int rooms = 0;
            foreach (Transform room in root.transform)
            {
                if (!room.name.StartsWith("Room_")) continue;
                var rs = room.GetComponent<Busted.Map.RoomStyle>() ?? room.gameObject.AddComponent<Busted.Map.RoomStyle>();
                rs.exterior = false; rooms++;
                sb.AppendLine($"  {room.name}: 벽면 {BustedWallSystem.FacesOf(rs).Count}, 바닥 {BustedWallSystem.FloorTiles(rs).Count}");
            }
            sb.Insert(0, $"{rootName}: 방 {rooms}개 + 외벽 바깥면 {BustedWallSystem.FacesOf(ext).Count}면\n");
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return sb.ToString();
        }
    }
}
