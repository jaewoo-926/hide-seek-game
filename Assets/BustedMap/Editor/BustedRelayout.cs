using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Busted.Map;

namespace Busted.Map.EditorTools
{
    /// <summary>
    /// 사용자가 손으로 바꾼 구조를 정리:
    ///  1) 벽/문은 "보이는 위치"(자식 Face/Frame 위치) 기준으로 오브젝트 위치를 맞춤 (보이는 모습은 그대로)
    ///  2) 문은 그대로 두고, 문 근처 벽 줄을 문 위치에 정확히 맞춤 + 문 구멍 다시 계산
    ///  3) 벽으로 막힌 공간을 다시 찾아 방 그룹(Walls/Doors/Floor) 재구성. 이름은 기존 방과 가장 많이 겹치는 것 유지
    /// arg: "루트|apply" (apply 가 아니면 미리보기만)
    /// </summary>
    public static class BustedRelayout
    {
        const float G = 2.5f, H = 3f, Thick = 0.33f;

        class Wl { public WallSegment s; public bool v; public float line, along, len; public bool dead; public int region = -1; }
        class Dr { public DoorUnit d; public Transform t; public bool v; public float line, center, width, height; public string Name => d != null ? d.name : (t != null ? t.name : "?"); }

        static bool IsV(Transform t) => Mathf.Abs(Mathf.Sin(t.eulerAngles.y * Mathf.Deg2Rad)) > 0.7f;

        public static string Apply(string arg)
        {
            var p = arg.Split('|');
            var map = (GameObject.Find(p[0]) ?? throw new Exception("루트 없음: " + p[0])).transform;
            bool apply = p.Length > 1 && p[1] == "apply";
            var o = map.position;
            var log = new StringBuilder();

            // ── 1) 문: 보이는 위치(Frame) 기준으로 정리 ──
            var doors = new List<Dr>();
            int doorFix = 0;
            foreach (var du in map.GetComponentsInChildren<DoorUnit>(true))
            {
                var frame = du.transform.Find("Frame");
                Vector3 delta = frame != null ? frame.localPosition : Vector3.zero;
                delta.y = 0;
                Vector3 pos = du.transform.position + du.transform.TransformVector(delta);
                if (delta.sqrMagnitude > 1e-6f)
                {
                    doorFix++;
                    if (apply)
                    {
                        du.transform.position = pos;
                        foreach (Transform c in du.transform) c.localPosition -= delta;
                    }
                }
                bool v = IsV(du.transform);
                var lp = pos - o;
                doors.Add(new Dr { d = du, v = v, line = v ? lp.x : lp.z, center = v ? lp.z : lp.x, width = du.width > 0 ? du.width * Mathf.Abs(du.transform.lossyScale.x) : 2.03f, height = du.height > 0 ? du.height : 2.27f });
            }
            // 벽에 끼워 넣는 오브젝트(엘리베이터 등, WallOpening): 앞면 위치 기준으로 그 폭만큼 벽을 비움
            int nOpen = 0;
            foreach (var wo in map.GetComponentsInChildren<WallOpening>(true))
            {
                var own = wo.GetComponent<Renderer>();
                var rs = wo.alignCenter && own != null ? new[] { own } : wo.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                var fwd = wo.transform.forward;
                bool v = Mathf.Abs(fwd.x) > Mathf.Abs(fwd.z);   // 앞이 x 방향을 보면 세로벽
                var front = b.center + (v ? new Vector3(Mathf.Sign(fwd.x) * b.extents.x, 0, 0) : new Vector3(0, 0, Mathf.Sign(fwd.z) * b.extents.z));
                var lp = (wo.alignCenter ? b.center : front) - o;
                doors.Add(new Dr { t = wo.transform, v = v, line = v ? lp.x : lp.z, center = v ? b.center.z - o.z : b.center.x - o.x, width = v ? b.size.z : b.size.x, height = wo.height });
                nOpen++;
            }
            log.AppendLine($"문 {doors.Count - nOpen}개 (보이는 위치로 오브젝트 정리 {doorFix}개)" + (nOpen > 0 ? $", 벽에 끼운 오브젝트 {nOpen}개" : ""));

            // ── 2) 벽: 보이는 위치(Face) 기준으로 정리 ──
            var walls = new List<Wl>();
            int wallFix = 0, dead = 0;
            foreach (var s in map.GetComponentsInChildren<WallSegment>(true))
            {
                if (s.GetComponent<WallCell>() == null) continue;
                var faces = new List<Transform>();
                foreach (Transform c in s.transform) if (c.name.StartsWith("Face_") && c.GetComponent<MeshRenderer>() != null) faces.Add(c);
                var w = new Wl { s = s, v = IsV(s.transform), len = G * Mathf.Abs(s.transform.lossyScale.x) };
                if (faces.Count == 0 || !s.gameObject.activeInHierarchy) { w.dead = true; dead++; walls.Add(w); continue; }
                Vector3 delta = Vector3.zero; foreach (var f in faces) delta += f.localPosition; delta /= faces.Count; delta.y = 0;
                Vector3 pos = s.transform.position + s.transform.TransformVector(delta);
                if (delta.sqrMagnitude > 1e-6f)
                {
                    wallFix++;
                    if (apply)
                    {
                        s.transform.position = pos;
                        foreach (var f in faces) { var lp0 = f.localPosition; f.localPosition = new Vector3(0, lp0.y, 0); }
                    }
                }
                var lp = pos - o;
                w.line = w.v ? lp.x : lp.z; w.along = w.v ? lp.z : lp.x;
                walls.Add(w);
            }
            // 겹친 중복 벽
            int dup = 0;
            for (int i = 0; i < walls.Count; i++)
                for (int j = i + 1; j < walls.Count; j++)
                {
                    var a = walls[i]; var b = walls[j];
                    if (a.dead || b.dead || a.v != b.v) continue;
                    if (Mathf.Abs(a.line - b.line) < 0.05f && Mathf.Abs(a.along - b.along) < 0.05f) { b.dead = true; dup++; }
                }
            log.AppendLine($"벽 {walls.Count}칸 (보이는 위치로 정리 {wallFix}칸, 면이 지워진 빈 벽 {dead}칸, 겹친 중복 {dup}칸 → 삭제)");
            var live = walls.Where(w => !w.dead).ToList();

            // ── 3) 문에 맞춰 벽 줄 이동 ──
            // 같은 방향·거의 같은 줄(8cm 이내)·이어진 벽들을 한 줄(cluster)로 묶음
            var clusterOf = new Dictionary<Wl, int>(); int nc = 0;
            foreach (var w0 in live)
            {
                if (clusterOf.ContainsKey(w0)) continue;
                var st = new Stack<Wl>(); st.Push(w0); clusterOf[w0] = nc;
                while (st.Count > 0)
                {
                    var u = st.Pop();
                    foreach (var w in live)
                        if (!clusterOf.ContainsKey(w) && w.v == u.v && Mathf.Abs(u.line - w.line) < 0.08f && Mathf.Abs(u.along - w.along) < (u.len + w.len) / 2 + 0.2f)
                        { clusterOf[w] = nc; st.Push(w); }
                }
                nc++;
            }
            int snapped = 0, straight = 0;
            var noWall = new List<string>();
            var doorsOfCluster = new Dictionary<int, List<Dr>>();
            foreach (var d in doors)
            {
                var hit = live.Where(w => w.v == d.v && Mathf.Abs(w.line - d.line) < 0.75f && Mathf.Abs(w.along - d.center) < w.len / 2 + d.width / 2 - 0.05f)
                              .OrderBy(w => Mathf.Abs(w.line - d.line)).ToList();
                if (hit.Count == 0) { noWall.Add($"{d.Name}({(d.v ? "세로" : "가로")} {d.line:0.##}, {d.center:0.##})"); continue; }
                int c = clusterOf[hit[0]];
                if (!doorsOfCluster.TryGetValue(c, out var l)) doorsOfCluster[c] = l = new List<Dr>();
                l.Add(d);
            }
            void SetLine(Wl w, float line)
            {
                if (Mathf.Abs(w.line - line) < 0.002f) return;
                w.line = line;
                if (apply)
                {
                    var pos = w.s.transform.position;
                    if (w.v) pos.x = o.x + line; else pos.z = o.z + line;
                    w.s.transform.position = pos;
                }
            }
            foreach (var grp in live.GroupBy(w => clusterOf[w]))
            {
                if (doorsOfCluster.TryGetValue(grp.Key, out var ds))
                {
                    foreach (var w in grp)
                    {
                        // 문과 겹치는 벽은 그 문 줄로, 나머지는 가장 가까운 문 줄로
                        var d = ds.OrderBy(x => Mathf.Max(0, Mathf.Abs(w.along - x.center) - x.width / 2 - w.len / 2)).ThenBy(x => Mathf.Abs(w.along - x.center)).First();
                        float before = w.line; SetLine(w, d.line); if (Mathf.Abs(before - w.line) > 0.002f) snapped++;
                    }
                }
                else
                {
                    // 문 없는 줄: 몇 cm 어긋난 것만 한 줄로 반듯하게
                    var lines = grp.Select(w => w.line).OrderBy(x => x).ToList();
                    float med = lines[lines.Count / 2];
                    if (lines.Last() - lines.First() < 0.1f)
                        foreach (var w in grp) { float before = w.line; SetLine(w, med); if (Mathf.Abs(before - w.line) > 0.002f) straight++; }
                }
            }
            log.AppendLine($"문 위치에 맞춰 옮긴 벽 {snapped}칸, 몇 cm 어긋나 반듯하게 맞춘 벽 {straight}칸" + (noWall.Count > 0 ? $"\n  ⚠ 근처에 벽이 없는 문: {string.Join(", ", noWall)}" : ""));

            // ── 4) 바닥 칸 정리 (같은 칸에 겹친 타일 / 비어 있는 칸) ──
            var byCell = new Dictionary<Vector2Int, List<Transform>>();
            foreach (var r in map.GetComponentsInChildren<MeshRenderer>(true))
            {
                var t = r.transform;
                if (t.parent == null || t.parent.name != "Floor") continue;
                var c = r.bounds.center - o;
                var key = new Vector2Int(Mathf.FloorToInt(c.x / G), Mathf.FloorToInt(c.z / G));
                if (!byCell.TryGetValue(key, out var l)) byCell[key] = l = new List<Transform>();
                l.Add(t);
            }
            var tiles = new Dictionary<Vector2Int, Transform>();
            var oldRoomOf = new Dictionary<Vector2Int, Transform>();
            Transform RoomOfTile(Transform t) { var room = t.parent.parent; return room != null && room.name.StartsWith("Room_") ? room : null; }
            int dupTiles = 0, tileSnap = 0;
            foreach (var kv in byCell)
            {
                var cc = new Vector3((kv.Key.x + .5f) * G, 0, (kv.Key.y + .5f) * G) + o;
                var keep = kv.Value.OrderBy(t => { var b = t.GetComponent<Renderer>().bounds.center; b.y = 0; return (b - cc).sqrMagnitude; }).First();
                foreach (var t in kv.Value)
                    if (t != keep) { dupTiles++; if (apply) UnityEngine.Object.DestroyImmediate(t.gameObject); }
                tiles[kv.Key] = keep;
                oldRoomOf[kv.Key] = RoomOfTile(keep);
                var kb = keep.GetComponent<Renderer>().bounds.center; var off = new Vector3(cc.x - kb.x, 0, cc.z - kb.z);
                if (off.sqrMagnitude > 1e-6f && off.sqrMagnitude < 0.25f) { tileSnap++; if (apply) keep.position += off; }
            }
            const int NX = 24, NZ = 16;
            var holesF = new List<Vector2Int>();
            for (int k = 0; k < NX; k++) for (int j = 0; j < NZ; j++) if (!tiles.ContainsKey(new Vector2Int(k, j))) holesF.Add(new Vector2Int(k, j));
            foreach (var h in holesF)
            {
                // 가장 가까운 타일을 복제해 빈 칸 채우기
                var n = tiles.Keys.Where(k => !holesF.Contains(k)).OrderBy(k => (k - h).sqrMagnitude).First();
                var src = tiles[n];
                Transform t = src;
                if (apply)
                {
                    var pre = PrefabUtility.GetCorrespondingObjectFromSource(src.gameObject);
                    var go = pre != null ? (GameObject)PrefabUtility.InstantiatePrefab(pre, src.parent) : UnityEngine.Object.Instantiate(src.gameObject, src.parent);
                    go.name = $"Floor_{h.x}_{h.y}";
                    go.transform.SetPositionAndRotation(src.position + new Vector3((h.x - n.x) * G, 0, (h.y - n.y) * G), src.rotation);
                    go.transform.localScale = src.localScale;
                    var mf = go.GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
                    var mr = go.GetComponent<MeshRenderer>(); if (mr != null) mr.sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
                    GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(src.gameObject));
                    t = go.transform;
                }
                tiles[h] = t; oldRoomOf[h] = null;
            }
            if (dupTiles > 0 || holesF.Count > 0 || tileSnap > 0) log.AppendLine($"바닥: 같은 칸에 겹친 타일 {dupTiles}장 삭제, 몇 cm 어긋난 타일 {tileSnap}장 칸에 맞춤, 빈 칸 {holesF.Count}개 채움" + (holesF.Count > 0 ? " (" + string.Join(" ", holesF.Select(h => $"{h.x},{h.y}")) + ")" : ""));
            bool Blocked(Vector2Int a, Vector2Int b)
            {
                var ca = new Vector2((a.x + .5f) * G, (a.y + .5f) * G); var cb = new Vector2((b.x + .5f) * G, (b.y + .5f) * G);
                foreach (var w in live)
                {
                    if (a.x != b.x && w.v && Mathf.Min(ca.x, cb.x) < w.line && w.line < Mathf.Max(ca.x, cb.x) && Mathf.Abs(ca.y - w.along) <= w.len / 2 + 1e-3f) return true;
                    if (a.y != b.y && !w.v && Mathf.Min(ca.y, cb.y) < w.line && w.line < Mathf.Max(ca.y, cb.y) && Mathf.Abs(ca.x - w.along) <= w.len / 2 + 1e-3f) return true;
                }
                return false;
            }
            var regionOf = new Dictionary<Vector2Int, int>();
            var regions = new List<List<Vector2Int>>();
            foreach (var c0 in tiles.Keys.OrderBy(k => k.x).ThenBy(k => k.y))
            {
                if (regionOf.ContainsKey(c0)) continue;
                int id = regions.Count; var cells = new List<Vector2Int> { c0 }; regionOf[c0] = id;
                var q = new Stack<Vector2Int>(); q.Push(c0);
                while (q.Count > 0)
                {
                    var a = q.Pop();
                    foreach (var dd in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                    {
                        var b = a + dd;
                        if (!tiles.ContainsKey(b) || regionOf.ContainsKey(b) || Blocked(a, b)) continue;
                        regionOf[b] = id; cells.Add(b); q.Push(b);
                    }
                }
                regions.Add(cells);
            }

            // ── 5) 이름 정하기: 기존 방과 겹치는 칸 수가 큰 순서로 1:1 배정 ──
            var pairs = new List<(int n, int reg, Transform room)>();
            for (int i = 0; i < regions.Count; i++)
                foreach (var g in regions[i].GroupBy(c => oldRoomOf[c]).Where(g => g.Key != null))
                    pairs.Add((g.Count(), i, g.Key));
            var nameOf = new Transform[regions.Count];
            var usedRoom = new HashSet<Transform>();
            foreach (var pr in pairs.OrderByDescending(x => x.n))
                if (nameOf[pr.reg] == null && !usedRoom.Contains(pr.room)) { nameOf[pr.reg] = pr.room; usedRoom.Add(pr.room); }
            var oldRooms = new List<Transform>();
            foreach (Transform r in map) if (r.name.StartsWith("Room_")) oldRooms.Add(r);

            log.AppendLine($"공간 {regions.Count}개 (기존 방 {oldRooms.Count}개)");
            for (int i = 0; i < regions.Count; i++)
            {
                var comp = string.Join(", ", regions[i].GroupBy(c => oldRoomOf[c]).OrderByDescending(g => g.Count()).Select(g => $"{(g.Key ? g.Key.name.Substring(5) : "?")} {g.Count()}"));
                string nm = nameOf[i] != null ? nameOf[i].name : $"Room_새공간_{i}";
                int before = nameOf[i] != null ? oldRoomOf.Count(kv => kv.Value == nameOf[i]) : 0;
                log.AppendLine($"  {nm}: 바닥 {regions[i].Count}칸 (전 {before}칸) ← {comp}");
            }
            var lost = oldRooms.Where(r => !usedRoom.Contains(r)).Select(r => r.name).ToList();
            if (lost.Count > 0) log.AppendLine($"  ⚠ 공간이 없어진 방: {string.Join(", ", lost)}");

            if (!apply) return log.ToString();

            // ── 6) 실제 적용 ──
            foreach (var w in walls.Where(w => w.dead)) UnityEngine.Object.DestroyImmediate(w.s.gameObject);

            // 문 구멍 + 충돌체
            foreach (var w in live)
            {
                var s = w.s; var t = s.transform;
                var holes = new List<Vector2>(); float hh = 2.27f;
                foreach (var d in doors)
                {
                    if (d.v != w.v || Mathf.Abs(d.line - w.line) > 0.05f) continue;
                    float s0 = Mathf.Max(w.along - w.len / 2, d.center - d.width / 2), s1 = Mathf.Min(w.along + w.len / 2, d.center + d.width / 2);
                    if (s1 - s0 < 0.01f) continue;
                    Vector3 W0 = w.v ? o + new Vector3(w.line, 0, s0) : o + new Vector3(s0, 0, w.line);
                    Vector3 W1 = w.v ? o + new Vector3(w.line, 0, s1) : o + new Vector3(s1, 0, w.line);
                    float l0 = t.InverseTransformPoint(W0).x, l1 = t.InverseTransformPoint(W1).x;
                    holes.Add(new Vector2(Mathf.Max(-G / 2, Mathf.Min(l0, l1)), Mathf.Min(G / 2, Mathf.Max(l0, l1))));
                    hh = Mathf.Min(hh, d.height);
                }
                var wc = s.GetComponent<WallCell>();
                wc.openings = holes.OrderBy(h => h.x).ToArray(); wc.openingHeight = hh;
                s.isDoor = holes.Count > 0; s.vertical = w.v;
                s.gridK = w.v ? Mathf.RoundToInt(w.line / G) : Mathf.FloorToInt(w.along / G);
                s.gridJ = w.v ? Mathf.FloorToInt(w.along / G) : Mathf.RoundToInt(w.line / G);
                foreach (var bc in s.GetComponents<BoxCollider>()) UnityEngine.Object.DestroyImmediate(bc);
                float x = -G / 2;
                foreach (var h in wc.openings)
                {
                    if (h.x - x > 0.01f) Box(s.gameObject, x, h.x, 0, H);
                    if (hh < H - 0.01f) Box(s.gameObject, h.x, h.y, hh, H);
                    x = h.y;
                }
                if (G / 2 - x > 0.01f) Box(s.gameObject, x, G / 2, 0, H);
            }

            // 방 그룹 만들기
            var roomOfRegion = new Transform[regions.Count];
            for (int i = 0; i < regions.Count; i++)
            {
                Transform r = nameOf[i];
                if (r == null)
                {
                    r = new GameObject($"Room_새공간_{i}").transform; r.SetParent(map, false);
                    r.gameObject.AddComponent<RoomStyle>();
                }
                roomOfRegion[i] = r;
            }
            Transform Folder(Transform room, string n)
            {
                var f = room.Find(n);
                if (f == null) { f = new GameObject(n).transform; f.SetParent(room, false); }
                return f;
            }
            // 일단 전부 루트로 꺼낸 뒤 방 위치를 공간 중심으로
            var tilesList = tiles.ToList();
            foreach (var kv in tilesList) kv.Value.SetParent(map, true);
            foreach (var w in live) w.s.transform.SetParent(map, true);
            foreach (var d in doors) if (d.d != null) d.d.transform.SetParent(map, true);
            for (int i = 0; i < regions.Count; i++)
            {
                var r = roomOfRegion[i];
                bool onlyFolders = true;
                foreach (Transform c in r) if (c.name != "Walls" && c.name != "Doors" && c.name != "Floor") onlyFolders = false;
                if (onlyFolders)
                {
                    var cs = regions[i];
                    var center = new Vector3((cs.Min(c => c.x) + cs.Max(c => c.x) + 1) * G / 2, 0, (cs.Min(c => c.y) + cs.Max(c => c.y) + 1) * G / 2);
                    r.position = o + center;
                    foreach (Transform c in r) { c.localPosition = Vector3.zero; c.localRotation = Quaternion.identity; }
                }
            }
            foreach (var kv in tilesList) kv.Value.SetParent(Folder(roomOfRegion[regionOf[kv.Key]], "Floor"), true);

            int Probe(Vector3 world)
            {
                var c = world - o;
                return regionOf.TryGetValue(new Vector2Int(Mathf.FloorToInt(c.x / G), Mathf.FloorToInt(c.z / G)), out int id) ? id : -1;
            }
            int Owner(Transform t, Vector3 normal)
            {
                int a = Probe(t.position + normal * 0.6f), b = Probe(t.position - normal * 0.6f);
                if (a < 0) return b; if (b < 0) return a;
                return regions[a].Count <= regions[b].Count ? a : b;   // 작은 방이 벽/문을 가짐
            }
            int nw = 0, nd = 0;
            foreach (var w in live)
            {
                int id = Owner(w.s.transform, w.s.transform.forward);
                if (id < 0) continue;
                w.s.transform.SetParent(Folder(roomOfRegion[id], "Walls"), true); nw++;
            }
            foreach (var d in doors)
            {
                if (d.d == null) continue;
                int id = Owner(d.d.transform, d.d.transform.forward);
                if (id < 0) continue;
                d.d.transform.SetParent(Folder(roomOfRegion[id], "Doors"), true); nd++;
                d.d.vertical = d.v; d.d.line = Mathf.RoundToInt(d.line / G); d.d.center = d.center;
                d.d.name = $"Door_{(d.v ? "v" : "h")}{Mathf.RoundToInt(d.line / G)}_{d.center:0.##}";
            }
            // 비어버린 기존 방 정리
            var removed = new List<string>();
            foreach (var r in oldRooms)
            {
                if (usedRoom.Contains(r)) continue;
                bool empty = true;
                foreach (Transform c in r)
                {
                    if ((c.name == "Walls" || c.name == "Doors" || c.name == "Floor") && c.childCount == 0) continue;
                    empty = false;
                }
                if (empty) { removed.Add(r.name); UnityEngine.Object.DestroyImmediate(r.gameObject); }
            }
            if (removed.Count > 0) log.AppendLine($"  비어서 지운 방 그룹: {string.Join(", ", removed)}");

            // 면 다시 만들기 (방 디자인 반영) + 바닥 디자인은 RoomStyle 에 값이 있는 방만 다시 적용
            BustedWallSystem.ApplyAllFaces(map);
            foreach (Transform r in map)
            {
                var rs = r.GetComponent<RoomStyle>();
                if (rs == null || rs.exterior) continue;
                if (rs.floorDesign != null || rs.floorMaterial != null) BustedWallSystem.Apply(rs, false);
            }
            log.AppendLine($"그룹 재구성: 벽 {nw}칸, 문 {nd}개, 바닥 {tiles.Count}장");

            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
            return log.ToString();
        }

        static void Box(GameObject go, float x0, float x1, float y0, float y1)
        {
            var b = go.AddComponent<BoxCollider>();
            b.center = new Vector3((x0 + x1) / 2, (y0 + y1) / 2, 0);
            b.size = new Vector3(x1 - x0, y1 - y0, Thick);
        }

        /// arg: 루트 — 방별 RoomStyle 값과 실제 벽면/바닥 머티리얼 (사용자가 손으로 바꾼 것이 있는지 확인용)
        public static string Styles(string root)
        {
            var map = GameObject.Find(root).transform;
            var sb = new StringBuilder();
            foreach (Transform r in map)
            {
                var rs = r.GetComponent<RoomStyle>(); if (rs == null) continue;
                sb.Append($"{r.name}: 벽={(rs.wallDesign ? rs.wallDesign.name : "-")}/{(rs.wallMaterial ? rs.wallMaterial.name : "-")} 바닥={(rs.floorDesign ? rs.floorDesign.name : "-")}/{(rs.floorMaterial ? rs.floorMaterial.name : "-")}");
                var fm = new HashSet<string>();
                var f = r.Find("Floor"); if (f != null) foreach (var mr in f.GetComponentsInChildren<MeshRenderer>()) fm.Add(mr.sharedMaterial ? mr.sharedMaterial.name : "null");
                sb.AppendLine($"  | 실제 바닥 머티리얼: {string.Join(",", fm)}");
            }
            var wm = new Dictionary<string, int>();
            foreach (var (mr, room) in BustedWallSystem.AllFaces(map))
            {
                string k = (room ? room.name : "외벽") + ":" + (mr.sharedMaterial ? mr.sharedMaterial.name : "null");
                wm[k] = wm.TryGetValue(k, out int n) ? n + 1 : 1;
            }
            sb.AppendLine("벽면 머티리얼: " + string.Join("; ", wm.Select(kv => $"{kv.Key}×{kv.Value}")));
            return sb.ToString();
        }
    }
}
