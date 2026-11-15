using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    /// <summary>씬의 벽 칸과 설계도 격자(grid.json + patch)를 비교 / 어긋난 칸 옮기기</summary>
    public static class BustedWallDiff
    {
        [Serializable] class Edge { public string o; public int k, j; public string t; }
        [Serializable] class Grid { public Edge[] edges; }

        static HashSet<string> Load(string paths)
        {
            var set = new HashSet<string>();
            foreach (var p in paths.Split(','))
            {
                if (string.IsNullOrEmpty(p)) continue;
                foreach (var e in JsonUtility.FromJson<Grid>(File.ReadAllText(p)).edges) set.Add($"{e.o}{e.k}_{e.j}");
            }
            return set;
        }

        /// arg: "루트|grid.json,patch.json"
        public static string Compare(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform;
            var grid = Load(p[1]);
            var scene = new HashSet<string>();
            foreach (var s in map.GetComponentsInChildren<WallSegment>(true))
                if (s.GetComponent<WallCell>() != null) scene.Add($"{(s.vertical ? "v" : "h")}{s.gridK}_{s.gridJ}");
            var sb = new StringBuilder();
            sb.Append("씬에만 있음: ");
            foreach (var k in scene) if (!grid.Contains(k)) sb.Append(k + " ");
            sb.Append("\n설계도에만 있음: ");
            foreach (var k in grid) if (!scene.Contains(k)) sb.Append(k + " ");
            return sb.ToString();
        }

        /// arg: "루트|from1>to1,from2>to2" 예: "1F_Map|v15_0>v16_0" — 벽 칸을 다른 격자 칸으로 옮김 (문 구멍 다시 계산은 Rebuild 로)
        public static string Move(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform;
            var o = map.position; const float G = 2.5f;
            var byKey = new Dictionary<string, WallSegment>();
            foreach (var s in map.GetComponentsInChildren<WallSegment>(true))
                if (s.GetComponent<WallCell>() != null) byKey[$"{(s.vertical ? "v" : "h")}{s.gridK}_{s.gridJ}"] = s;
            int n = 0; var sb = new StringBuilder();
            foreach (var pair in p[1].Split(','))
            {
                var ft = pair.Split('>');
                if (!byKey.TryGetValue(ft[0], out var s)) { sb.Append($"없음:{ft[0]} "); continue; }
                if (ft[1] == "x") { UnityEngine.Object.DestroyImmediate(s.gameObject); sb.Append($"삭제:{ft[0]} "); n++; continue; }
                bool v = ft[1][0] == 'v';
                var kj = ft[1].Substring(1).Split('_');
                int k = int.Parse(kj[0]), j = int.Parse(kj[1]);
                if (byKey.ContainsKey(ft[1])) { UnityEngine.Object.DestroyImmediate(s.gameObject); sb.Append($"중복삭제:{ft[0]} "); n++; continue; }
                s.vertical = v; s.gridK = k; s.gridJ = j;
                s.name = $"Wall_{ft[1]}";
                s.transform.SetPositionAndRotation(v ? o + new Vector3(k * G, 0, (j + 0.5f) * G) : o + new Vector3((k + 0.5f) * G, 0, j * G), Quaternion.Euler(0, v ? 90 : 0, 0));
                n++;
            }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{n}칸 옮김 {sb}";
        }
    }
}
