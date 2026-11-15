using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    public static class BustedDoorAlign
    {
        /// arg: "루트|옮길문|기준문" — 옮길 문을 벽에 수직 방향으로만 이동해 기준 문과 같은 벽 줄에 맞춤 (벽 방향 위치는 그대로)
        public static string Align(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform;
            DoorUnit a = null, b = null;
            foreach (var d in map.GetComponentsInChildren<DoorUnit>(true)) { if (d.name == p[1]) a = d; if (d.name == p[2]) b = d; }
            if (a == null || b == null) throw new Exception("문을 찾지 못함");
            var pos = a.transform.position; var before = pos;
            if (a.vertical) pos.x = b.transform.position.x; else pos.z = b.transform.position.z;
            a.transform.position = pos;
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{a.name}: {before} → {pos}";
        }
    }
}
