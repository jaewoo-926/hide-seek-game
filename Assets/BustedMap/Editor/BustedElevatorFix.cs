using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    /// <summary>엘리베이터 앞 벽 줄에 빠진 벽 칸을 채우고, 엘리베이터 방 바닥을 Room_엘리베이터 로 되돌림 (이후 BustedRelayout → FillElevatorRoom)</summary>
    public static class BustedElevatorFix
    {
        const float G = 2.5f;

        public static string Ensure(string root)
        {
            var map = GameObject.Find(root).transform; var o = map.position;
            var room = map.Cast<Transform>().First(t => t.name.StartsWith("Room_") && t.name.Contains("엘리베이터"));
            var elevs = room.GetComponentsInChildren<WallOpening>(true).ToList();
            if (elevs.Count == 0) throw new Exception("엘리베이터(WallOpening) 없음");
            var bs = elevs.Select(e => { var rs = e.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }).ToList();
            var fwd = elevs[0].transform.forward;
            if (Mathf.Abs(fwd.x) > Mathf.Abs(fwd.z)) throw new Exception("세로벽 엘리베이터는 아직 지원 안 함");
            float sgn = Mathf.Sign(fwd.z);                                   // 앞(복도) 방향
            float front = bs.Average(b => (sgn < 0 ? b.min.z : b.max.z) - o.z);
            float ex0 = bs.Min(b => b.min.x) - o.x, ex1 = bs.Max(b => b.max.x) - o.x;

            var walls = map.GetComponentsInChildren<WallSegment>(true).Where(s => s.GetComponent<WallCell>() != null).ToList();
            bool IsV(Transform t) => Mathf.Abs(Mathf.Sin(t.eulerAngles.y * Mathf.Deg2Rad)) > 0.7f;
            // 방 양옆 세로벽: 엘리베이터 바깥쪽으로 가장 가까운, 앞줄~뒤쪽 구간에 걸친 세로벽
            float zMid = front - sgn * 1.25f;
            var vx = walls.Where(w => IsV(w.transform) && Mathf.Abs(w.transform.position.z - o.z - zMid) < 1.3f).Select(w => w.transform.position.x - o.x).ToList();
            float left = vx.Where(x => x <= ex0 + 0.05f).DefaultIfEmpty(Mathf.Floor(ex0 / G) * G).Max();
            float right = vx.Where(x => x >= ex1 - 0.05f).DefaultIfEmpty(Mathf.Ceil(ex1 / G) * G).Min();
            // 뒤쪽 가로벽
            var hz = walls.Where(w => !IsV(w.transform)).Select(w => (z: w.transform.position.z - o.z, x: w.transform.position.x - o.x))
                          .Where(w => w.x > left && w.x < right && (sgn < 0 ? w.z > front + 0.5f : w.z < front - 0.5f)).Select(w => w.z).ToList();
            float back = hz.Count > 0 ? (sgn < 0 ? hz.Min() : hz.Max()) : front - sgn * G;

            // 앞줄 빠진 칸 채우기
            var frontWalls = walls.Where(w => !IsV(w.transform) && Mathf.Abs(w.transform.position.z - o.z - front) < 0.35f).ToList();
            var template = frontWalls.FirstOrDefault() ?? walls.First(w => !IsV(w.transform));
            int added = 0;
            for (float x = left + G / 2; x < right; x += G)
            {
                if (frontWalls.Any(w => Mathf.Abs(w.transform.position.x - o.x - x) < 0.6f)) continue;
                var go = new GameObject($"Wall_h{Mathf.FloorToInt(x / G)}_{Mathf.RoundToInt(front / G)}");
                go.transform.SetParent(room.Find("Walls") ?? room, false);
                go.transform.SetPositionAndRotation(o + new Vector3(x, 0, front), Quaternion.identity);
                var ws = go.AddComponent<WallSegment>(); ws.vertical = false; ws.gridK = Mathf.FloorToInt(x / G); ws.gridJ = Mathf.RoundToInt(front / G);
                go.AddComponent<WallCell>();
                foreach (var side in new[] { "Face_A", "Face_B" })
                {
                    var f = new GameObject(side); f.transform.SetParent(go.transform, false);
                    f.transform.localRotation = Quaternion.Euler(0, side == "Face_A" ? 0 : 180, 0);
                    f.AddComponent<MeshFilter>(); var mr = f.AddComponent<MeshRenderer>();
                    var tf = template.transform.Find(side); if (tf != null) mr.sharedMaterials = tf.GetComponent<MeshRenderer>().sharedMaterials;
                }
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(template.gameObject));
                added++;
            }
            // 엘리베이터 방 바닥 되돌리기
            var floor = room.Find("Floor") ?? new GameObject("Floor").transform;
            floor.SetParent(room, true);
            float z0 = Mathf.Min(front, back), z1 = Mathf.Max(front, back);
            int moved = 0;
            foreach (var r in map.GetComponentsInChildren<MeshRenderer>(true).ToList())
            {
                if (r.transform.parent == null || r.transform.parent.name != "Floor" || r.transform.parent == floor) continue;
                var c = r.bounds.center - o;
                if (c.x > left && c.x < right && c.z > z0 && c.z < z1) { r.transform.SetParent(floor, true); moved++; }
            }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene); EditorSceneManager.SaveScene(map.gameObject.scene);
            return $"{root}: 엘리베이터 방 x {left:0.##}~{right:0.##}, 앞줄 z {front:0.###}, 뒤 z {back:0.##} — 앞 벽 {added}칸 추가, 바닥 {moved}장 되돌림";
        }
    }
}
