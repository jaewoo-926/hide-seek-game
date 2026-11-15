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
    /// 통합 벽 시스템.
    ///  - 모든 벽 칸(2.5m)은 Face_A / Face_B 두 면. 각 면은 "디자인 메쉬"를 2.5m×3m 로 맞춰 생성
    ///  - 문 달린 칸은 같은 디자인 메쉬에서 문 구멍만 잘라냄 → 옆 벽과 패턴이 이어짐
    ///  - 각 면은 바라보는 방의 RoomStyle(디자인/머티리얼)을 따름 → 방 디자인 변경 시 문 벽도 같이 바뀜
    ///  - 문(DoorUnit)은 벽과 분리된 문틀 + 양문
    /// </summary>
    public static class BustedWallSystem
    {
        public const float G = 2.5f, H = 3f, Thick = 0.33f;
        const string FaceDir = "Assets/BustedMap/Generated/Faces";
        const string PlainPrefab = "Assets/BustedMap/Prefabs/Busted_Wall_Plain.prefab";
        const string DoorPrefab = "Assets/BustedMap/Prefabs/Busted_Wall_Door.prefab";

        // ───────────────────────── 버텍스 / 클리핑 ─────────────────────────
        struct V
        {
            public Vector3 p, n; public Vector4 t; public Vector2 uv, uv2; public Color c;
            public float Get(int a) => a == 0 ? p.x : a == 1 ? p.y : p.z;
            public static V Lerp(V a, V b, float k) => new V
            {
                p = Vector3.Lerp(a.p, b.p, k), n = Vector3.Lerp(a.n, b.n, k).normalized,
                t = Vector4.Lerp(a.t, b.t, k), uv = Vector2.Lerp(a.uv, b.uv, k), uv2 = Vector2.Lerp(a.uv2, b.uv2, k), c = Color.Lerp(a.c, b.c, k)
            };
        }

        class Norm
        {
            public List<V[]>[] subs; // 서브메쉬별 삼각형
            public bool hasT, hasUV2, hasC;
            public float thickness;
            public string key;
        }

        static List<V> Clip(List<V> poly, int axis, float val, bool keepGreater)
        {
            var o = new List<V>(poly.Count + 2);
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                float da = keepGreater ? a.Get(axis) - val : val - a.Get(axis);
                float db = keepGreater ? b.Get(axis) - val : val - b.Get(axis);
                bool ina = da >= -1e-5f, inb = db >= -1e-5f;
                if (ina) o.Add(a);
                if (ina != inb) o.Add(V.Lerp(a, b, da / (da - db)));
            }
            return o;
        }

        static List<V> ClipRect(V[] tri, int ax, int ay, float x0, float x1, float y0, float y1)
        {
            var p = new List<V>(tri);
            p = Clip(p, ax, x0, true); if (p.Count < 3) return p;
            p = Clip(p, ax, x1, false); if (p.Count < 3) return p;
            p = Clip(p, ay, y0, true); if (p.Count < 3) return p;
            p = Clip(p, ay, y1, false);
            return p;
        }

        // ───────────────────────── 디자인 정규화 ─────────────────────────
        static readonly Dictionary<string, Norm> cache = new Dictionary<string, Norm>();

        public static MeshFilter DesignFilter(GameObject design)
        {
            if (design == null) return null;
            MeshFilter best = null; float bs = -1;
            foreach (var mf in design.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name.Contains("Glass")) continue;
                float s = mf.sharedMesh.bounds.size.sqrMagnitude;
                if (s > bs) { bs = s; best = mf; }
            }
            return best;
        }

        static float Fit(float len, float target) => (Mathf.Abs(len - target) <= 0.35f || len < target) ? target / Mathf.Max(len, 1e-4f) : 1f;

        static Norm Normalize(Mesh m, bool wall, bool flip)
        {
            string key = MeshKey(m) + (wall ? "|w" : "|f") + (flip ? "|flip" : "");
            if (cache.TryGetValue(key, out var hit)) return hit;

            var vs = m.vertices; var ns = m.normals; var ts = m.tangents; var uv = m.uv; var uv2 = m.uv2; var cs = m.colors;
            var b0 = m.bounds;
            Quaternion q = Quaternion.identity;
            if (wall && b0.size.z > b0.size.x) q = Quaternion.Euler(0, 90, 0);
            if (flip) q = Quaternion.Euler(0, 180, 0) * q;
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (var v in vs) { var r = q * v; mn = Vector3.Min(mn, r); mx = Vector3.Max(mx, r); }
            var size = mx - mn;
            Vector3 s = wall ? new Vector3(Fit(size.x, G), Fit(size.y, H), 1) : new Vector3(Fit(size.x, G), 1, Fit(size.z, G));

            var nm = new Norm { hasT = ts.Length == vs.Length, hasUV2 = uv2.Length == vs.Length, hasC = cs.Length == vs.Length, key = key };
            nm.thickness = wall ? size.z : size.y;
            V Make(int i)
            {
                var r = q * vs[i];
                Vector3 p = wall ? new Vector3((r.x - mn.x) * s.x, (r.y - mn.y) * s.y, r.z - mn.z)
                                 : new Vector3((r.x - mn.x) * s.x, r.y - mx.y, (r.z - mn.z) * s.z);
                var n = ns.Length == vs.Length ? q * ns[i] : Vector3.up;
                n = new Vector3(n.x / s.x, n.y / s.y, n.z / s.z).normalized;
                Vector4 t = Vector4.zero;
                if (nm.hasT) { var t3 = q * new Vector3(ts[i].x, ts[i].y, ts[i].z); t = new Vector4(t3.x, t3.y, t3.z, ts[i].w); }
                return new V { p = p, n = n, t = t, uv = uv.Length == vs.Length ? uv[i] : Vector2.zero, uv2 = nm.hasUV2 ? uv2[i] : Vector2.zero, c = nm.hasC ? cs[i] : Color.white };
            }
            nm.subs = new List<V[]>[m.subMeshCount];
            for (int sm = 0; sm < m.subMeshCount; sm++)
            {
                var tri = m.GetTriangles(sm);
                var list = new List<V[]>(tri.Length / 3);
                for (int i = 0; i < tri.Length; i += 3) list.Add(new[] { Make(tri[i]), Make(tri[i + 1]), Make(tri[i + 2]) });
                nm.subs[sm] = list;
            }
            cache[key] = nm;
            return nm;
        }

        static string MeshKey(Mesh m)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string guid, out long lid)) return guid + ":" + lid;
            return m.name + ":" + m.vertexCount + ":" + m.bounds.size.ToString("F3");
        }

        /// <summary>정규화된 디자인을 여러 사각형 영역으로 잘라 한 메쉬로 합침. ax/ay = 사각형 축, shift = 위치 이동</summary>
        static Mesh BuildMesh(Norm nm, List<Rect> rects, int ax, int ay, Vector3 shift, string name)
        {
            var P = new List<Vector3>(); var N = new List<Vector3>(); var T = new List<Vector4>(); var U = new List<Vector2>(); var U2 = new List<Vector2>(); var C = new List<Color>();
            var subsIdx = new List<int>[nm.subs.Length];
            for (int sm = 0; sm < nm.subs.Length; sm++)
            {
                var idx = new List<int>();
                foreach (var tri in nm.subs[sm])
                {
                    var shifted = new V[3];
                    for (int i = 0; i < 3; i++) { shifted[i] = tri[i]; shifted[i].p += shift; }
                    foreach (var r in rects)
                    {
                        var poly = ClipRect(shifted, ax, ay, r.xMin, r.xMax, r.yMin, r.yMax);
                        if (poly.Count < 3) continue;
                        int b = P.Count;
                        foreach (var v in poly) { P.Add(v.p); N.Add(v.n); T.Add(v.t); U.Add(v.uv); U2.Add(v.uv2); C.Add(v.c); }
                        for (int i = 1; i + 1 < poly.Count; i++) { idx.Add(b); idx.Add(b + i); idx.Add(b + i + 1); }
                    }
                }
                subsIdx[sm] = idx;
            }
            var m = new Mesh { name = name };
            if (P.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(P); m.SetNormals(N); m.SetUVs(0, U);
            if (nm.hasT) m.SetTangents(T);
            if (nm.hasUV2) m.SetUVs(1, U2);
            if (nm.hasC) m.SetColors(C);
            m.subMeshCount = subsIdx.Length;
            for (int sm = 0; sm < subsIdx.Length; sm++) m.SetTriangles(subsIdx[sm], sm);
            m.RecalculateBounds();
            return m;
        }

        static Mesh SaveOrLoad(string key, Func<string, Mesh> make)
        {
            if (!AssetDatabase.IsValidFolder(FaceDir)) AssetDatabase.CreateFolder("Assets/BustedMap/Generated", "Faces");
            string h = Hash128.Compute(key).ToString().Substring(0, 16);
            string path = $"{FaceDir}/{h}.asset";
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (ex != null) return ex;
            var m = make(h);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ───────────────────────── 면 메쉬 ─────────────────────────
        /// <summary>면 로컬 기준 문 구멍(x0,x1)들을 뺀 벽면 메쉬</summary>
        public static Mesh WallFaceMesh(Mesh design, bool flip, List<Vector2> holes, float holeH)
        {
            var hs = holes.Select(v => new Vector2(Mathf.Max(-G / 2, Mathf.Min(v.x, v.y)), Mathf.Min(G / 2, Mathf.Max(v.x, v.y))))
                          .Where(v => v.y - v.x > 0.01f).OrderBy(v => v.x).ToList();
            string key = "W|" + MeshKey(design) + "|" + flip + "|" + holeH.ToString("0.###") + "|" + string.Join(",", hs.Select(v => $"{v.x:0.###}:{v.y:0.###}"));
            return SaveOrLoad(key, h =>
            {
                var nm = Normalize(design, true, flip);
                var rects = new List<Rect>();
                float x = -G / 2;
                foreach (var o in hs)
                {
                    if (o.x - x > 0.001f) rects.Add(Rect.MinMaxRect(x, 0, o.x, H));
                    rects.Add(Rect.MinMaxRect(o.x, holeH, o.y, H));
                    x = o.y;
                }
                if (G / 2 - x > 0.001f) rects.Add(Rect.MinMaxRect(x, 0, G / 2, H));
                return BuildMesh(nm, rects, 0, 1, new Vector3(-G / 2, 0, 0), "BW_" + design.name + "_" + h);
            });
        }

        public static Mesh FloorMesh(Mesh design)
        {
            string key = "F|" + MeshKey(design);
            return SaveOrLoad(key, h => BuildMesh(Normalize(design, false, false), new List<Rect> { Rect.MinMaxRect(0, 0, G, G) }, 0, 2, Vector3.zero, "BF_" + design.name + "_" + h));
        }

        public static float DesignThickness(Mesh design, bool flip) => Normalize(design, true, flip).thickness;

        // ───────────────────────── 방 / 면 소유 ─────────────────────────
        public static Dictionary<Vector2Int, Transform> CellRooms(Transform map)
        {
            var d = new Dictionary<Vector2Int, Transform>();
            var o = map.position;
            foreach (Transform room in map)
            {
                if (!room.name.StartsWith("Room_")) continue;
                var f = room.Find("Floor"); if (f == null) continue;
                foreach (Transform t in f)
                {
                    var r = t.GetComponent<Renderer>();
                    var c = (r != null ? r.bounds.center : t.position) - o;
                    d[new Vector2Int(Mathf.FloorToInt(c.x / G), Mathf.FloorToInt(c.z / G))] = room;
                }
            }
            return d;
        }

        /// <summary>층 전체 벽면 → 바라보는 방 (null = 건물 바깥)</summary>
        public static List<(MeshRenderer r, Transform room)> AllFaces(Transform map)
        {
            var cells = CellRooms(map);
            var o = map.position;
            var list = new List<(MeshRenderer, Transform)>();
            foreach (var seg in map.GetComponentsInChildren<WallSegment>(true))
            {
                if (seg.GetComponent<WallCell>() == null) continue;
                foreach (Transform c in seg.transform)
                {
                    if (!c.name.StartsWith("Face_")) continue;
                    var r = c.GetComponent<MeshRenderer>(); if (r == null) continue;
                    var p = seg.transform.position + c.forward * 0.6f - o;
                    cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / G), Mathf.FloorToInt(p.z / G)), out var room);
                    list.Add((r, room));
                }
            }
            return list;
        }

        public static Transform MapRoot(RoomStyle rs) => rs.exterior ? rs.transform : rs.transform.parent;

        public static List<MeshRenderer> FacesOf(RoomStyle rs)
        {
            var map = MapRoot(rs); var res = new List<MeshRenderer>();
            if (map == null) return res;
            foreach (var (r, room) in AllFaces(map)) if (rs.exterior ? room == null : room == rs.transform) res.Add(r);
            return res;
        }

        public static List<Transform> FloorTiles(RoomStyle rs)
        {
            var f = rs.transform.Find("Floor"); var l = new List<Transform>();
            if (f != null) foreach (Transform t in f) if (t.GetComponent<MeshFilter>() != null) l.Add(t);
            return l;
        }

        // ───────────────────────── 적용 ─────────────────────────
        static (GameObject design, Material mat, bool flip) WallStyleFor(Transform map, Transform room)
        {
            var rootRs = map.GetComponent<RoomStyle>();
            var plain = AssetDatabase.LoadAssetAtPath<GameObject>(PlainPrefab);
            if (room == null)
                return (rootRs != null && rootRs.wallDesign != null ? rootRs.wallDesign : plain, rootRs != null ? rootRs.wallMaterial : null, rootRs != null && rootRs.flipWallDesign);
            var rs = room.GetComponent<RoomStyle>();
            GameObject d = rs != null && rs.wallDesign != null ? rs.wallDesign : (rootRs != null && rootRs.defaultInteriorWall != null ? rootRs.defaultInteriorWall : plain);
            bool flip = rs != null && rs.wallDesign != null ? rs.flipWallDesign : false;
            return (d, rs != null ? rs.wallMaterial : null, flip);
        }

        static void ApplyFace(MeshRenderer r, Transform map, Transform room, bool undo)
        {
            var (design, mat, flip) = WallStyleFor(map, room);
            var mf = DesignFilter(design);
            if (mf == null) return;
            var seg = r.transform.parent;
            var cell = seg.GetComponent<WallCell>();
            bool isB = r.name == "Face_B";
            var holes = new List<Vector2>();
            if (cell != null) foreach (var o in cell.openings) holes.Add(isB ? new Vector2(-o.y, -o.x) : o);
            var mesh = WallFaceMesh(mf.sharedMesh, flip, holes, cell != null ? cell.openingHeight : 2.4f);
            var filter = r.GetComponent<MeshFilter>();
            var srcR = mf.GetComponent<MeshRenderer>();
            var mats = srcR != null ? (Material[])srcR.sharedMaterials.Clone() : new Material[mesh.subMeshCount];
            if (mats.Length != mesh.subMeshCount) Array.Resize(ref mats, mesh.subMeshCount);
            if (mat != null) for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            if (undo) { Undo.RecordObject(filter, "방 디자인"); Undo.RecordObject(r, "방 디자인"); }
            filter.sharedMesh = mesh;
            r.sharedMaterials = mats;
        }

        static void ApplyFloor(Transform tile, Transform map, RoomStyle rs, bool undo)
        {
            var rootRs = map.GetComponent<RoomStyle>();
            GameObject design = rs != null && rs.floorDesign != null ? rs.floorDesign : null;
            var filter = tile.GetComponent<MeshFilter>(); var r = tile.GetComponent<MeshRenderer>();
            bool generated = filter.sharedMesh != null && filter.sharedMesh.name.StartsWith("BF_");
            if (design == null && generated && rootRs != null) design = rootRs.defaultFloor;
            if (undo) { Undo.RecordObject(filter, "방 디자인"); Undo.RecordObject(r, "방 디자인"); Undo.RecordObject(tile, "방 디자인"); }
            Material[] mats;
            if (design != null)
            {
                var mf = DesignFilter(design); if (mf == null) return;
                // 타일 이름 Floor_k_j 로 칸 위치
                var parts = tile.name.Split('_');
                var c = r.bounds.center - map.position;
                int k = Mathf.FloorToInt(c.x / G), j = Mathf.FloorToInt(c.z / G);
                if (parts.Length == 3 && int.TryParse(parts[1], out int pk) && int.TryParse(parts[2], out int pj)) { k = pk; j = pj; }
                filter.sharedMesh = FloorMesh(mf.sharedMesh);
                tile.SetPositionAndRotation(map.position + new Vector3(k * G, 0, j * G), Quaternion.identity);
                tile.localScale = Vector3.one;
                var sr = mf.GetComponent<MeshRenderer>();
                mats = sr != null ? (Material[])sr.sharedMaterials.Clone() : new Material[1];
            }
            else
            {
                // 원래 프리팹 바닥 그대로 (머티리얼만 원래 값으로)
                var src = PrefabUtility.IsPartOfPrefabInstance(r) ? PrefabUtility.GetCorrespondingObjectFromSource(r) : null;
                mats = src != null ? (Material[])src.sharedMaterials.Clone() : r.sharedMaterials;
            }
            int sub = filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : mats.Length;
            if (mats.Length != sub) Array.Resize(ref mats, sub);
            if (rs != null && rs.floorMaterial != null) for (int i = 0; i < mats.Length; i++) mats[i] = rs.floorMaterial;
            r.sharedMaterials = mats;
        }

        /// <summary>RoomStyle 하나 적용 (Inspector 에서 호출)</summary>
        public static void Apply(RoomStyle rs, bool undo = true)
        {
            var map = MapRoot(rs); if (map == null) return;
            if (rs.exterior)
            {
                // 층 루트: 외벽 바깥면 + (기본값이 바뀌었을 수 있으므로) 개별 디자인 없는 방들 전부
                foreach (var (r, room) in AllFaces(map))
                {
                    if (room == null) { ApplyFace(r, map, null, undo); continue; }
                    var rrs = room.GetComponent<RoomStyle>();
                    if (rrs == null || rrs.wallDesign == null) ApplyFace(r, map, room, undo);
                }
                foreach (Transform room in map)
                {
                    var rrs = room.GetComponent<RoomStyle>();
                    if (rrs == null || rrs.exterior || rrs.floorDesign != null) continue;
                    foreach (var t in FloorTiles(rrs)) ApplyFloor(t, map, rrs, undo);
                }
            }
            else
            {
                foreach (var r in FacesOf(rs)) ApplyFace(r, map, rs.transform, undo);
                foreach (var t in FloorTiles(rs)) ApplyFloor(t, map, rs, undo);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(rs.gameObject.scene);
        }

        public static void ApplyAllFaces(Transform map)
        {
            foreach (var (r, room) in AllFaces(map)) ApplyFace(r, map, room, false);
            AssetDatabase.SaveAssets();
        }

        // ───────────────────────── 재구성 ─────────────────────────
        [Serializable] public class DoorDef { public string o; public int line; public float c; public int pairs = 1; }
        [Serializable] class DoorList { public DoorDef[] doors; }

        struct CellInfo { public Transform walls; public bool door; }

        /// <summary>
        /// 층의 벽을 통합 벽 시스템으로 다시 만든다. 방 그룹/바닥은 유지.
        /// arg: "루트이름|문JSON(선택)|기본내부벽프리팹경로(선택)"
        ///  문JSON 이 없으면 기존 문 칸(isDoor) 위치에 문 1세트씩.
        /// </summary>
        public static string Rebuild(string arg)
        {
            cache.Clear();
            var p = arg.Split('|');
            var map = (GameObject.Find(p[0]) ?? throw new Exception("루트 없음: " + p[0])).transform;
            string doorsJson = p.Length > 1 ? p[1] : "";
            string interiorPath = p.Length > 2 ? p[2] : "";
            var o = map.position;
            var log = new StringBuilder();

            // 0) 루트 RoomStyle 기본값
            var rootRs = map.GetComponent<RoomStyle>() ?? map.gameObject.AddComponent<RoomStyle>();
            rootRs.exterior = true;
            var plain = AssetDatabase.LoadAssetAtPath<GameObject>(PlainPrefab);
            if (rootRs.wallDesign == null) rootRs.wallDesign = plain;
            if (!string.IsNullOrEmpty(interiorPath) && rootRs.defaultInteriorWall == null)
                rootRs.defaultInteriorWall = AssetDatabase.LoadAssetAtPath<GameObject>(interiorPath) ?? throw new Exception("내부 벽 프리팹 없음: " + interiorPath);
            if (rootRs.defaultInteriorWall == null) rootRs.defaultInteriorWall = plain;
            if (rootRs.defaultFloor == null) rootRs.defaultFloor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/03.Models/Ofiice/PolygonOffice/Prefabs/Buildings/SM_Bld_Floor_Concrete_01.prefab");
            foreach (Transform room in map)
                if (room.name.StartsWith("Room_") && room.GetComponent<RoomStyle>() == null) room.gameObject.AddComponent<RoomStyle>();

            // 1) 기존 벽 → 2.5m 칸 목록
            var cells = new Dictionary<(bool v, int k, int j), CellInfo>();
            var old = new List<GameObject>();
            foreach (var s in map.GetComponentsInChildren<WallSegment>(true))
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(s.gameObject);
                if (src != null && src.name == "Busted_Wall_Window") continue;     // 사용자가 따로 둔 창문 벽
                if (s.name == "Wall_window") continue;
                var c = s.transform.position - o;
                bool v = s.vertical;
                float lineCoord = v ? c.x : c.z, along = v ? c.z : c.x;
                int line = Mathf.RoundToInt(lineCoord / G);
                if (Mathf.Abs(lineCoord - line * G) > 0.4f) continue;              // 격자 밖
                float len = G;
                var rs = s.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    len = Mathf.Max(G, Mathf.Round((v ? b.size.z : b.size.x) / G) * G);
                }
                if (s.GetComponent<WallCell>() != null) len = G;
                int n = Mathf.RoundToInt(len / G);
                int first = Mathf.RoundToInt((along - len / 2) / G);
                var folder = s.transform.parent;
                if (folder != null && folder.name == "Doors" && folder.parent != null)
                {
                    var w = folder.parent.Find("Walls");
                    if (w == null) { w = new GameObject("Walls").transform; w.SetParent(folder.parent, false); }
                    folder = w;
                }
                for (int i = 0; i < n; i++)
                {
                    var key = v ? (true, line, first + i) : (false, first + i, line);
                    var info = new CellInfo { walls = folder, door = s.isDoor && n == 1 };
                    if (cells.TryGetValue(key, out var prev)) info.door |= prev.door;
                    cells[key] = info;
                }
                old.Add(s.gameObject);
            }

            // 2) 문 목록
            var doors = new List<DoorDef>();
            if (!string.IsNullOrEmpty(doorsJson))
                doors.AddRange(JsonUtility.FromJson<DoorList>(System.IO.File.ReadAllText(doorsJson)).doors);
            else
                foreach (var kv in cells)
                    if (kv.Value.door)
                        doors.Add(new DoorDef { o = kv.Key.v ? "v" : "h", line = kv.Key.v ? kv.Key.k : kv.Key.j, c = ((kv.Key.v ? kv.Key.j : kv.Key.k) + 0.5f) * G, pairs = 1 });

            // 3) 기존 벽 / 기존 문 삭제
            foreach (var g in old) UnityEngine.Object.DestroyImmediate(g);
            foreach (var du in map.GetComponentsInChildren<DoorUnit>(true)) UnityEngine.Object.DestroyImmediate(du.gameObject);

            // 4) 문짝 템플릿 (Busted_Wall_Door 프리팹의 DoorLeaf_L/R)
            var tmpl = DoorTemplate(out float pairW, out float leafTop);
            float openH = Mathf.Min(leafTop + 0.03f, H - 0.3f);

            // 5) 벽 칸 생성
            var cellRooms = CellRooms(map);
            int nSeg = 0;
            foreach (var kv in cells)
            {
                var (v, k, j) = kv.Key;
                var pos = v ? o + new Vector3(k * G, 0, (j + 0.5f) * G) : o + new Vector3((k + 0.5f) * G, 0, j * G);
                var go = new GameObject($"Wall_{(v ? "v" : "h")}{k}_{j}");
                go.transform.SetParent(kv.Value.walls != null ? kv.Value.walls : map, false);
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, v ? 90 : 0, 0));
                var ws = go.AddComponent<WallSegment>(); ws.vertical = v; ws.gridK = k; ws.gridJ = j;
                var wc = go.AddComponent<WallCell>(); wc.openingHeight = openH;
                // 이 칸에 걸친 문 구멍
                var holes = new List<Vector2>();
                int line = v ? k : j; float a0 = (v ? j : k) * G, a1 = a0 + G;
                foreach (var d in doors)
                {
                    if ((d.o == "v") != v || d.line != line) continue;
                    float w = d.pairs * pairW + 0.04f;
                    float s0 = Mathf.Max(a0, d.c - w / 2), s1 = Mathf.Min(a1, d.c + w / 2);
                    if (s1 - s0 < 0.01f) continue;
                    var w0 = v ? o + new Vector3(k * G, 0, s0) : o + new Vector3(s0, 0, j * G);
                    var w1 = v ? o + new Vector3(k * G, 0, s1) : o + new Vector3(s1, 0, j * G);
                    float l0 = go.transform.InverseTransformPoint(w0).x, l1 = go.transform.InverseTransformPoint(w1).x;
                    holes.Add(new Vector2(Mathf.Min(l0, l1), Mathf.Max(l0, l1)));
                }
                wc.openings = holes.ToArray();
                ws.isDoor = holes.Count > 0;
                foreach (var side in new[] { "Face_A", "Face_B" })
                {
                    var f = new GameObject(side);
                    f.transform.SetParent(go.transform, false);
                    f.transform.localRotation = Quaternion.Euler(0, side == "Face_A" ? 0 : 180, 0);
                    f.AddComponent<MeshFilter>(); f.AddComponent<MeshRenderer>();
                }
                // 충돌: 구멍 뺀 사각형들
                var sorted = holes.OrderBy(h => h.x).ToList();
                float x = -G / 2;
                foreach (var h in sorted)
                {
                    if (h.x - x > 0.01f) Box(go, x, h.x, 0, H);
                    Box(go, h.x, h.y, openH, H);
                    x = h.y;
                }
                if (G / 2 - x > 0.01f) Box(go, x, G / 2, 0, H);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                foreach (Transform c in go.transform) GameObjectUtility.SetStaticEditorFlags(c.gameObject, GameObjectUtility.GetStaticEditorFlags(go));
                nSeg++;
            }

            // 6) 문 생성
            foreach (var d in doors)
            {
                bool v = d.o == "v";
                var pos = v ? o + new Vector3(d.line * G, 0, d.c) : o + new Vector3(d.c, 0, d.line * G);
                var center = pos - o + (v ? new Vector3(-0.6f, 0, 0) : new Vector3(0, 0, -0.6f));
                Transform room = null;
                cellRooms.TryGetValue(new Vector2Int(Mathf.FloorToInt(center.x / G), Mathf.FloorToInt(center.z / G)), out room);
                if (room == null)
                {
                    var c2 = pos - o + (v ? new Vector3(0.6f, 0, 0) : new Vector3(0, 0, 0.6f));
                    cellRooms.TryGetValue(new Vector2Int(Mathf.FloorToInt(c2.x / G), Mathf.FloorToInt(c2.z / G)), out room);
                }
                Transform parent = map;
                if (room != null)
                {
                    parent = room.Find("Doors");
                    if (parent == null) { parent = new GameObject("Doors").transform; parent.SetParent(room, false); }
                }
                MakeDoor(parent, pos, v, d, tmpl, pairW, openH);
            }
            UnityEngine.Object.DestroyImmediate(tmpl);

            // 7) 면 메쉬 + 디자인 적용
            ApplyAllFaces(map);

            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            EditorSceneManager.SaveScene(map.gameObject.scene);
            log.Insert(0, $"{map.name}: 벽 {old.Count}개 → 2.5m 칸 {nSeg}개, 문 {doors.Count}개 (문 폭 {pairW:0.00}m/세트, 높이 {openH:0.00}m)\n");
            // 방별 문 개수
            foreach (Transform room in map)
            {
                if (!room.name.StartsWith("Room_")) continue;
                int nd = 0;
                foreach (var du in map.GetComponentsInChildren<DoorUnit>(true)) if (TouchesRoom(du, room, map)) nd++;
                log.AppendLine($"  {room.name}: 문 {nd}");
            }
            return log.ToString();
        }

        static bool TouchesRoom(DoorUnit du, Transform room, Transform map)
        {
            var cells = CellRooms(map);
            foreach (var s in new[] { -0.6f, 0.6f })
            {
                var p = du.transform.position + du.transform.forward * s - map.position;
                if (cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / G), Mathf.FloorToInt(p.z / G)), out var r) && r == room) return true;
            }
            return false;
        }

        static void Box(GameObject go, float x0, float x1, float y0, float y1)
        {
            var b = go.AddComponent<BoxCollider>();
            b.center = new Vector3((x0 + x1) / 2, (y0 + y1) / 2, 0);
            b.size = new Vector3(x1 - x0, y1 - y0, Thick);
        }

        /// <summary>Busted_Wall_Door 프리팹에서 문짝 2개를 꺼내 템플릿(원점 중심)으로</summary>
        static GameObject DoorTemplate(out float pairW, out float top)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefab) ?? throw new Exception("Busted_Wall_Door 프리팹 없음");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var t = new GameObject("__DoorTemplate");
            var leaves = new List<Transform>();
            foreach (Transform c in inst.transform) if (c.name.StartsWith("DoorLeaf")) leaves.Add(c);
            if (leaves.Count == 0) throw new Exception("Busted_Wall_Door 에 DoorLeaf 가 없음");
            Bounds b = default; bool first = true;
            foreach (var l in leaves)
                foreach (var r in l.GetComponentsInChildren<Renderer>())
                { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            foreach (var l in leaves)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(l.gameObject);
                var copy = src != null ? (GameObject)PrefabUtility.InstantiatePrefab(src) : UnityEngine.Object.Instantiate(l.gameObject);
                copy.name = l.name;
                copy.transform.SetParent(t.transform, false);
                copy.transform.localPosition = l.localPosition - new Vector3(b.center.x, 0, 0);
                copy.transform.localRotation = l.localRotation;
                copy.transform.localScale = l.localScale;
                foreach (var c in copy.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            }
            pairW = b.size.x; top = b.max.y;
            UnityEngine.Object.DestroyImmediate(inst);
            return t;
        }

        static void MakeDoor(Transform parent, Vector3 pos, bool v, DoorDef d, GameObject tmpl, float pairW, float openH)
        {
            var go = new GameObject($"Door_{d.o}{d.line}_{d.c:0.##}");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, v ? 90 : 0, 0));
            var du = go.AddComponent<DoorUnit>();
            du.vertical = v; du.line = d.line; du.center = d.c; du.pairs = d.pairs;
            du.width = d.pairs * pairW + 0.04f; du.height = openH;
            Material leafMat = null; Vector2 frameUV = Vector2.zero;
            for (int i = 0; i < d.pairs; i++)
            {
                float off = (i - (d.pairs - 1) / 2f) * pairW;
                foreach (Transform l in tmpl.transform)
                {
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(l.gameObject);
                    var c = src != null ? (GameObject)PrefabUtility.InstantiatePrefab(src) : UnityEngine.Object.Instantiate(l.gameObject);
                    c.name = $"{l.name}_{i}";
                    c.transform.SetParent(go.transform, false);
                    c.transform.localPosition = l.localPosition + new Vector3(off, 0, 0);
                    c.transform.localRotation = l.localRotation;
                    c.transform.localScale = l.localScale;
                    foreach (var col in c.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(col);
                    if (leafMat == null)
                    {
                        var mr = c.GetComponentInChildren<MeshRenderer>(); var mf = c.GetComponentInChildren<MeshFilter>();
                        if (mr != null && mf != null && mf.sharedMesh != null)
                        {
                            leafMat = mr.sharedMaterial;
                            var vs = mf.sharedMesh.vertices; var uv = mf.sharedMesh.uv; int bi = 0;
                            for (int q = 1; q < vs.Length; q++) if (vs[q].y > vs[bi].y) bi = q; // 문짝 맨 위(테두리) 색
                            if (uv.Length == vs.Length) frameUV = uv[bi];
                        }
                    }
                }
            }
            // 문틀 (구멍 옆면을 가려 줌)
            var frame = new GameObject("Frame");
            frame.transform.SetParent(go.transform, false);
            float w = du.width, fw = 0.08f, dz = 0.22f;
            frame.AddComponent<MeshFilter>().sharedMesh = FrameMesh(w, openH, fw, dz, frameUV);
            frame.AddComponent<MeshRenderer>().sharedMaterial = leafMat;
            GameObjectUtility.SetStaticEditorFlags(frame, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        static Mesh FrameMesh(float w, float h, float fw, float dz, Vector2 uv)
        {
            string key = $"FR|{w:0.###}|{h:0.###}|{fw}|{dz}|{uv.x:0.####},{uv.y:0.####}";
            return SaveOrLoad(key, hash =>
            {
                var P = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var I = new List<int>();
                void Cube(Vector3 mn, Vector3 mx)
                {
                    Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                    foreach (var n in dirs)
                    {
                        var c = (mn + mx) / 2; var e = (mx - mn) / 2;
                        Vector3 a = n.x != 0 ? Vector3.up : Vector3.right, b = Vector3.Cross(n, a);
                        int s = P.Count;
                        foreach (var (sa, sb) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                        {
                            var p = c + Vector3.Scale(n, e) + Vector3.Scale(a * sa, e) + Vector3.Scale(b * sb, e);
                            P.Add(p); N.Add(n); U.Add(uv);
                        }
                        I.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                    }
                }
                Cube(new Vector3(-w / 2 - fw, 0, -dz), new Vector3(-w / 2, h + fw, dz));
                Cube(new Vector3(w / 2, 0, -dz), new Vector3(w / 2 + fw, h + fw, dz));
                Cube(new Vector3(-w / 2 - fw, h, -dz), new Vector3(w / 2 + fw, h + fw, dz));
                var m = new Mesh { name = "DoorFrame_" + hash };
                m.SetVertices(P); m.SetNormals(N); m.SetUVs(0, U); m.SetTriangles(I, 0);
                // 면 방향 보정: 모든 삼각형이 바깥(normal)쪽을 보게
                var tris = m.triangles; var vs = m.vertices; var ns = m.normals;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var fn = Vector3.Cross(vs[tris[i + 1]] - vs[tris[i]], vs[tris[i + 2]] - vs[tris[i]]);
                    if (Vector3.Dot(fn, ns[tris[i]]) < 0) { var t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
                }
                m.triangles = tris;
                m.RecalculateBounds();
                return m;
            });
        }

        // ───────────────────────── 확인용 ─────────────────────────
        /// arg: "루트|방이름|디자인프리팹경로(빈칸=해제)" — 확인용으로 한 방에 디자인 적용 (저장 안 함)
        public static string TestDesign(string arg)
        {
            var p = arg.Split('|');
            var map = GameObject.Find(p[0]).transform;
            var rs = map.Find(p[1]).GetComponent<RoomStyle>();
            rs.wallDesign = string.IsNullOrEmpty(p[2]) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(p[2]);
            Apply(rs, false);
            return $"{p[1]} 벽 디자인 = {(rs.wallDesign ? rs.wallDesign.name : "(기본)")}, 벽면 {FacesOf(rs).Count}";
        }

        public static string Save(string root)
        {
            var map = GameObject.Find(root);
            EditorSceneManager.SaveScene(map.scene);
            return "저장: " + map.scene.path;
        }
    }
}
