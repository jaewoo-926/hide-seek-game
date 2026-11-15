using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 방 단위 그룹화: Room_방이름 (빈 오브젝트, 방 중심) 아래에 Walls / Doors / Floor 로 정리.
    /// arg: "rooms.json|루트이름|patch.json(선택)"
    ///  - patch.json: 누락된 벽 칸만 추가로 생성해 기존 맵에 합침 (기존 오브젝트는 건드리지 않음)
    /// </summary>
    public static class BustedRoomGrouper2
    {

        public static string Apply(string arg)
        {
            var parts = arg.Split('|');
            string roomsPath = parts[0];
            string rootName = parts[1];
            string patch = parts.Length > 2 ? parts[2] : "";
            var root = GameObject.Find(rootName) ?? throw new Exception("루트 없음: " + rootName);
            string log = "";

            // 1) 누락 벽 패치
            if (!string.IsNullOrEmpty(patch))
            {
                var off = root.transform.position;
                log += BustedFloorPipeline2.BuildFloor($"{patch}|__Patch|{off.x}|{off.z}|4.5|1") + "\n";
                var temp = GameObject.Find("__Patch");
                var walls = root.transform.Find("Walls") ?? new GameObject("Walls").transform;
                var doors = root.transform.Find("Doors") ?? new GameObject("Doors").transform;
                if (walls.parent == null) walls.SetParent(root.transform, false);
                if (doors.parent == null) doors.SetParent(root.transform, false);
                int moved = 0;
                foreach (var seg in temp.GetComponentsInChildren<Busted.Map.WallSegment>(true))
                {
                    var existing = FindSeg(root.transform, seg.name);
                    if (existing != null) { continue; } // 이미 있으면 건너뜀
                    seg.transform.SetParent(seg.isDoor ? doors : walls, true);
                    moved++;
                }
                UnityEngine.Object.DestroyImmediate(temp);
                log += $"패치 벽 {moved}칸 추가\n";
            }

            // 2) 방 데이터 (간단 JSON 파싱: cells 는 [[k,j],...] 형식이라 직접 파싱)
            var text = File.ReadAllText(roomsPath);
            var rooms = ParseRooms(text, out float G);

            // 이름 → 오브젝트 인덱스
            var segMap = new Dictionary<string, Transform>();
            foreach (var s in root.GetComponentsInChildren<Busted.Map.WallSegment>(true)) segMap[s.name] = s.transform;
            var floorMap = new Dictionary<string, Transform>();
            var floorRoot = root.transform.Find("Floor");
            if (floorRoot != null) foreach (Transform t in floorRoot) floorMap[t.name] = t;
            // 이전 실행으로 이미 방 그룹 안에 있는 바닥도 찾기
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Floor_") && !floorMap.ContainsKey(t.name)) floorMap[t.name] = t;

            int nRooms = 0, nSeg = 0, nFloor = 0;
            var origin = root.transform.position;
            foreach (var r in rooms)
            {
                string gname = "Room_" + r.name;
                var g = root.transform.Find(gname);
                if (g == null)
                {
                    float cx = 0, cz = 0;
                    foreach (var c in r.cells) { cx += (c.x + 0.5f) * G; cz += (c.y + 0.5f) * G; }
                    cx /= Mathf.Max(1, r.cells.Count); cz /= Mathf.Max(1, r.cells.Count);
                    var go = new GameObject(gname);
                    Undo.RegisterCreatedObjectUndo(go, "Room group");
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = origin + new Vector3(cx, 0, cz);
                    g = go.transform;
                }
                var gw = Sub(g, "Walls"); var gd = Sub(g, "Doors"); var gf = Sub(g, "Floor");
                foreach (var s in r.segs)
                    if (segMap.TryGetValue(s, out var t)) { Undo.SetTransformParent(t, s.StartsWith("Door") ? gd : gw, "Room group"); nSeg++; }
                foreach (var c in r.cells)
                    if (floorMap.TryGetValue($"Floor_{c.x}_{c.y}", out var t)) { Undo.SetTransformParent(t, gf, "Room group"); nFloor++; }
                nRooms++;
            }
            // 비어버린 기존 컨테이너 정리
            foreach (var n in new[] { "Walls", "Doors", "Floor" })
            {
                var t = root.transform.Find(n);
                if (t != null && t.childCount == 0) Undo.DestroyObjectImmediate(t.gameObject);
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return log + $"방 {nRooms}개 그룹화: 벽/문 {nSeg}칸, 바닥 {nFloor}장 이동";
        }

        static Transform Sub(Transform p, string n)
        {
            var t = p.Find(n);
            if (t != null) return t;
            var go = new GameObject(n);
            Undo.RegisterCreatedObjectUndo(go, "Room group");
            go.transform.SetParent(p, false);
            return go.transform;
        }

        static Transform FindSeg(Transform root, string name)
        {
            foreach (var s in root.GetComponentsInChildren<Busted.Map.WallSegment>(true)) if (s.name == name) return s.transform;
            return null;
        }

        class R { public string name; public List<Vector2Int> cells = new List<Vector2Int>(); public List<string> segs = new List<string>(); }

        // rooms.json 전용 미니 파서
        static List<R> ParseRooms(string json, out float grid)
        {
            grid = 2.5f;
            var gi = json.IndexOf("\"grid\"");
            if (gi >= 0)
            {
                int a = json.IndexOf(':', gi) + 1, b = json.IndexOfAny(new[] { ',', '}' }, a);
                float.TryParse(json.Substring(a, b - a).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out grid);
            }
            var list = new List<R>();
            int i = 0;
            while ((i = json.IndexOf("\"name\"", i)) >= 0)
            {
                var r = new R();
                int q1 = json.IndexOf('"', json.IndexOf(':', i) + 1), q2 = json.IndexOf('"', q1 + 1);
                r.name = json.Substring(q1 + 1, q2 - q1 - 1);
                int ci = json.IndexOf("\"cells\"", q2), si = json.IndexOf("\"segs\"", q2);
                int cs = json.IndexOf('[', ci), ce = MatchBracket(json, cs);
                var nums = new List<int>();
                foreach (var tok in json.Substring(cs, ce - cs + 1).Split(new[] { '[', ']', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    nums.Add(int.Parse(tok));
                for (int k = 0; k + 1 < nums.Count; k += 2) r.cells.Add(new Vector2Int(nums[k], nums[k + 1]));
                int ss = json.IndexOf('[', si), se = MatchBracket(json, ss);
                foreach (var tok in json.Substring(ss + 1, se - ss - 1).Split(','))
                {
                    var t = tok.Trim().Trim('"');
                    if (t.Length > 0) r.segs.Add(t);
                }
                list.Add(r);
                i = se;
            }
            return list;
        }

        static int MatchBracket(string s, int open)
        {
            int d = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '[') d++;
                else if (s[i] == ']' && --d == 0) return i;
            }
            return s.Length - 1;
        }
    }
}
