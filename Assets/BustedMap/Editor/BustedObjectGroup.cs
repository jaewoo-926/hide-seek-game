using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 방 안 오브젝트를 각 Room_xxx/Objects 로 묶기. 위치·회전·크기·개수는 절대 바꾸지 않음(부모만 바꿈, worldPositionStays).
    /// 씬 최상위 오브젝트(카메라·조명·맵·벽/문 조각 제외)를 바닥 칸 기준으로 방에 배정.
    /// </summary>
    public static class BustedObjectGroup
    {
        const float G = 2.5f;
        static readonly string[] Skip = { "Main Camera", "Directional Light" };

        static Vector3 Center(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return t.position;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.center;
        }

        /// arg: "루트|dry" 또는 "루트|apply" (+ 선택: "|감옥xMin,xMax,zMin,zMax" 월드 좌표)
        public static string Run(string arg)
        {
            var p = arg.Split('|');
            bool apply = p.Length > 1 && p[1] == "apply";
            var jail = p.Length > 2 ? p[2].Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray() : new[] { 70.5f, 82f, 6f, 19f };
            var map = GameObject.Find(p[0]).transform; var o = map.position;
            var scene = map.gameObject.scene;
            var cells = BustedWallSystem.CellRooms(map);

            // 검증용: 씬 전체 Transform 의 월드 상태 기록
            var before = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                              .ToDictionary(t => t, t => (t.position, t.rotation, t.lossyScale, t.gameObject.activeInHierarchy));

            var roomBounds = new Dictionary<Transform, Bounds>();
            foreach (Transform room in map)
            {
                if (!room.name.StartsWith("Room_")) continue;
                var rs = room.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                roomBounds[room] = b;
            }
            var plan = new Dictionary<string, List<Transform>>();
            var left = new List<Transform>();
            void Add(string room, Transform t) { if (!plan.TryGetValue(room, out var l)) plan[room] = l = new List<Transform>(); l.Add(t); }

            // 1) 씬 최상위 오브젝트
            foreach (var g in scene.GetRootGameObjects())
            {
                var t = g.transform;
                if (t == map || Skip.Contains(g.name)) continue;
                if (g.GetComponentInChildren<WallSegment>(true) != null || g.GetComponentInChildren<DoorUnit>(true) != null) { left.Add(t); continue; }
                var c = Center(t);
                if (c.x >= jail[0] && c.x <= jail[1] && c.z >= jail[2] && c.z <= jail[3]) { Add("Room_감옥", t); continue; }
                var q = c - o;
                if (cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(q.x / G), Mathf.FloorToInt(q.z / G)), out var room)) { Add(room.name, t); continue; }
                // 바닥 타일이 없는 칸(계단 구멍, 외벽 바로 바깥 간판 등): 벽 포함 방 범위(+0.3m) 안에 있으면 가장 작은 방으로
                Transform best = null; float bestA = float.MaxValue;
                foreach (var rb in roomBounds)
                    if (c.x >= rb.Value.min.x - 0.3f && c.x <= rb.Value.max.x + 0.3f && c.z >= rb.Value.min.z - 0.3f && c.z <= rb.Value.max.z + 0.3f)
                    { float a = rb.Value.size.x * rb.Value.size.z; if (a < bestA) { bestA = a; best = rb.Key; } }
                if (best != null) Add(best.name, t); else left.Add(t);
            }
            // 2) 방 안에 이미 있는 오브젝트 묶음(Walls/Doors/Floor/Objects/Fill 외) — 예: Elevators
            foreach (Transform room in map)
            {
                if (!room.name.StartsWith("Room_")) continue;
                foreach (Transform c in room)
                    if (c.name != "Walls" && c.name != "Doors" && c.name != "Floor" && c.name != "Objects" && c.name != "Fill") Add(room.name, c);
            }

            var sb = new StringBuilder();
            int total = plan.Values.Sum(l => l.Count);
            sb.AppendLine($"{(apply ? "적용" : "미리보기")}: {total}개 → {plan.Count}개 방");
            foreach (var kv in plan.OrderBy(k => k.Key))
                sb.AppendLine($"  {kv.Key}: {kv.Value.Count}개 (하위 포함 {kv.Value.Sum(t => t.GetComponentsInChildren<Transform>(true).Length)})" + (kv.Key == "Room_감옥" || kv.Value.Count <= 6 ? " — " + string.Join(", ", kv.Value.Select(t => t.name)) : ""));
            sb.AppendLine($"  그대로 둠(방 밖/벽·문 조각): {left.Count}개 — {string.Join(", ", left.Select(t => $"{t.name}@({t.position.x:0.#},{t.position.z:0.#})"))}");

            if (!apply) return sb.ToString();

            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("방별 Objects 묶기");
            foreach (var kv in plan)
            {
                var room = map.Find(kv.Key);
                if (room == null)
                {
                    room = new GameObject(kv.Key).transform;
                    Undo.RegisterCreatedObjectUndo(room.gameObject, "Room");
                    room.SetParent(map, false);
                    var bb = new Bounds(Center(kv.Value[0]), Vector3.zero); foreach (var t in kv.Value) bb.Encapsulate(Center(t));
                    room.position = new Vector3(bb.center.x, 0, bb.center.z); room.rotation = Quaternion.identity; room.localScale = Vector3.one;
                }
                var objs = room.Find("Objects");
                if (objs == null)
                {
                    objs = new GameObject("Objects").transform;
                    Undo.RegisterCreatedObjectUndo(objs.gameObject, "Objects");
                    objs.SetParent(room, false);
                    objs.localPosition = Vector3.zero; objs.localRotation = Quaternion.identity; objs.localScale = Vector3.one;
                }
                foreach (var t in kv.Value) Undo.SetTransformParent(t, objs, "Objects");
            }

            // 검증: 모든 기존 오브젝트가 남아 있고 월드 위치/회전/크기/활성 상태가 같아야 함
            int missing = 0, moved = 0; float maxD = 0;
            foreach (var kv in before)
            {
                if (kv.Key == null) { missing++; continue; }
                var t = kv.Key;
                float d = Mathf.Max((t.position - kv.Value.position).magnitude, Quaternion.Angle(t.rotation, kv.Value.rotation) * 0.001f, (t.lossyScale - kv.Value.lossyScale).magnitude);
                maxD = Mathf.Max(maxD, d);
                if (d > 0.0005f || t.gameObject.activeInHierarchy != kv.Value.activeInHierarchy) moved++;
            }
            int after = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Count();
            sb.AppendLine($"검증: 이전 {before.Count}개 / 이후 {after}개 (새로 만든 묶음 {after - before.Count}개), 사라짐 {missing}, 위치·회전·크기 바뀜 {moved}, 최대 오차 {maxD:0.######}");
            if (missing == 0 && moved == 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); sb.AppendLine("저장함"); }
            else { Undo.PerformUndo(); sb.AppendLine("문제가 있어 되돌림(저장 안 함)"); }
            return sb.ToString();
        }
    }
}
