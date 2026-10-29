using System.Collections.Generic;
using System.IO;
using Chase;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

// 메뉴: Chase > Build Prototype Scene
// 로우폴리 그레이박스 도시 맵 + 캐릭터 + 조명 + NavMesh 를 한 번에 생성한다.
public static class ChaseSceneBuilder
{
    const string Root = "Assets/Chase";
    const string ScenePath = Root + "/Scenes/ChasePrototype.unity";
    const string NavMeshPath = Root + "/Scenes/ChasePrototype_NavMesh.asset";
    const string MatDir = Root + "/Materials";

    const float Half = 40f;          // 맵 절반 크기 (80 x 80)
    const float CellSize = 15f;      // 블록 간격
    const float BlockSize = 10f;     // 블록 크기 (나머지 5m 는 도로)

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    [MenuItem("Chase/Build Prototype Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var info = BuildLevel();
        BakeNavMesh(info.root);
        BuildSingleplayer(info);
    }

    // 싱글/멀티 씬이 공유하는 맵 정보
    public class LevelInfo
    {
        public Transform root;
        public List<Vector3> itemSpots;
        public List<Vector3> prisonSpots;
        public List<Vector3> hideSpots;
        public Vector3 thiefSpawn = new Vector3(-37.5f, 0, -37.5f);
        public List<Vector3> copSpawnPoints = new List<Vector3>
        {
            new Vector3(0, 0, 37.5f), new Vector3(37.5f, 0, 0), new Vector3(37.5f, 0, 37.5f),
            new Vector3(-37.5f, 0, 37.5f), new Vector3(37.5f, 0, -37.5f),
        };
    }

    // 현재 열린 씬에 도시 맵(정적 지형 + 조명)을 만든다. 시드가 고정이라 항상 같은 맵이 나온다.
    public static LevelInfo BuildLevel()
    {
        Directory.CreateDirectory(Root + "/Scenes");
        Directory.CreateDirectory(MatDir);
        CreateMaterials();
        EnableAdditionalLightShadows();
        var rng = new System.Random(7);

        // ---------- 정적 레벨 ----------
        var level = new GameObject("Level").transform;
        Box(level, "Ground", new Vector3(0, -0.5f, 0), new Vector3(Half * 2, 1, Half * 2), M("Road"));
        for (int i = 0; i < 4; i++)
        {
            var rot = Quaternion.Euler(0, i * 90, 0);
            var wall = Box(level, "Boundary", rot * new Vector3(0, 1.5f, Half + 0.5f), new Vector3(Half * 2 + 2, 3, 1), M("Wall"));
            wall.transform.rotation = rot;
        }

        var prisonSpots = new List<Vector3>();
        var hideSpots = new List<Vector3>();
        int[] coords = { -2, -1, 0, 1, 2 };
        var plazaCells = new HashSet<(int, int)> { (0, 0), (-1, 1), (1, -1), (2, 2), (-2, 0) };

        foreach (int gx in coords)
        foreach (int gz in coords)
        {
            var c = new Vector3(gx * CellSize, 0, gz * CellSize);
            var block = new GameObject($"Block_{gx}_{gz}").transform;
            block.SetParent(level, false);
            block.position = c;

            // 인도 (살짝 높은 블록 바닥)
            Box(block, "Sidewalk", new Vector3(0, 0.05f, 0), new Vector3(BlockSize + 1.2f, 0.1f, BlockSize + 1.2f), M("Sidewalk"), false);

            if (plazaCells.Contains((gx, gz)))
            {
                BuildPlaza(block, rng, hideSpots);
                prisonSpots.Add(c);
            }
            else if (rng.NextDouble() < 0.5)
            {
                BuildBuilding(block, Vector3.zero, new Vector3(Rand(rng, 8f, 9.4f), Rand(rng, 2.6f, 4.5f), Rand(rng, 8f, 9.4f)), rng);
            }
            else
            {
                // 골목이 있는 2동짜리 블록
                bool alongX = rng.NextDouble() < 0.5;
                for (int s = -1; s <= 1; s += 2)
                {
                    var size = new Vector3(3.9f, Rand(rng, 2.4f, 3.8f), Rand(rng, 7.5f, 9.4f));
                    var pos = new Vector3(s * 2.95f, 0, 0);
                    if (!alongX)
                    {
                        size = new Vector3(size.z, size.y, size.x);
                        pos = new Vector3(0, 0, s * 2.95f);
                    }
                    BuildBuilding(block, pos, size, rng);
                }
                // 골목 안 쓰레기통 = 은신처
                if (rng.NextDouble() < 0.6)
                {
                    var p = alongX ? new Vector3(0, 0, Rand(rng, -3f, 3f)) : new Vector3(Rand(rng, -3f, 3f), 0, 0);
                    hideSpots.Add(c + p);
                    BuildDumpster(block, p, alongX ? 0 : 90);
                }
            }
        }

        Physics.SyncTransforms();

        // ---------- 아이템/금고가 생길 수 있는 빈 자리 샘플링 ----------
        var itemSpots = new List<Vector3>();
        for (float x = -Half + 2.5f; x <= Half - 2.5f; x += 2.5f)
        for (float z = -Half + 2.5f; z <= Half - 2.5f; z += 2.5f)
        {
            var p = new Vector3(x, 0, z);
            if (Physics.CheckBox(p + Vector3.up * 1.3f, new Vector3(1.3f, 1f, 1.3f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
            bool nearPrisonSpot = false;
            foreach (var ps in prisonSpots) if (Vector3.Distance(ps, p) < 3.6f) nearPrisonSpot = true;
            if (nearPrisonSpot) continue;
            // 건물 바로 옆(벽에 붙은 위치)을 선호: 사방 중 한 곳이라도 막혀 있으면 채택
            bool nearWall = Physics.CheckBox(p + Vector3.up * 1.3f, new Vector3(2.4f, 1f, 2.4f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (nearWall || rng.NextDouble() < 0.25) itemSpots.Add(p);
        }

        // ---------- 조명 ----------
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.17f, 0.24f);
        RenderSettings.skybox = null;
        var sun = new GameObject("Moon Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(0.55f, 0.62f, 0.9f);
        sun.intensity = 0.35f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(55, -35, 0);

        return new LevelInfo { root = level, itemSpots = itemSpots, prisonSpots = prisonSpots, hideSpots = hideSpots };
    }

    static void BakeNavMesh(Transform level)
    {
        var surface = level.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        AssetDatabase.DeleteAsset(NavMeshPath);
        AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
    }

    public static WorldSpawner AddSpawner(GameObject go)
    {
        var spawner = go.AddComponent<WorldSpawner>();
        spawner.safeBody = M("SafeBody");
        spawner.safeFull = M("SafeFull");
        spawner.safeEmpty = M("SafeEmpty");
        spawner.clue = M("Clue");
        spawner.drink = M("Drink");
        spawner.prisonFrame = M("PrisonFrame");
        spawner.prisonFloor = M("PrisonFloor");
        spawner.alarm = M("Alarm");
        spawner.exitFrame = M("ExitFrame");
        spawner.exitBeam = M("ExitBeam");
        return spawner;
    }

    static void BuildSingleplayer(LevelInfo info)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var prisonSpots = info.prisonSpots;
        var itemSpots = info.itemSpots;
        var hideSpots = info.hideSpots;

        // ---------- 게임 매니저 ----------
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gm.spawner = AddSpawner(gmGo);
        gm.thiefSpawn = info.thiefSpawn;
        gm.copSpawnPoints = info.copSpawnPoints;
        gm.prisonSpots = prisonSpots;
        gm.itemSpots = itemSpots;

        // ---------- 캐릭터 ----------
        var player = BuildPlayer(gm.thiefSpawn);
        gm.player = player.GetComponent<Thief>();
        gm.thieves.Add(gm.player);
        gm.thieves.Add(BuildBotThief("도둑 B", gm.thiefSpawn + new Vector3(2, 0, 0), M("BotThief")));
        gm.thieves.Add(BuildBotThief("도둑 C", gm.thiefSpawn + new Vector3(0, 0, 2), M("BotThief2")));
        for (int i = 0; i < 3; i++)
            gm.cops.Add(BuildCop($"경찰 {i + 1}", gm.copSpawnPoints[i]));

        // ---------- 카메라 ----------
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 35;
        cam.farClipPlane = 200;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
        camGo.AddComponent<AudioListener>();
        var qv = camGo.AddComponent<QuarterViewCamera>();
        qv.target = player.transform;
        camGo.transform.position = player.transform.position + Quaternion.Euler(qv.pitch, qv.yaw, 0) * Vector3.back * qv.distance;
        camGo.transform.rotation = Quaternion.Euler(qv.pitch, qv.yaw, 0);
        player.GetComponent<ThiefPlayer>().cam = qv;

        var hud = gmGo.AddComponent<HUD>();
        hud.playerControl = player.GetComponent<ThiefPlayer>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (buildScenes.TrueForAll(s => s.path != ScenePath))
        {
            buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }
        AssetDatabase.SaveAssets();

        Debug.Log($"[Chase] 프로토타입 씬 생성 완료: {ScenePath} (아이템 자리 {itemSpots.Count}, 은신처 {hideSpots.Count}, 감옥 후보 {prisonSpots.Count})");
    }

    // ================= 레벨 파츠 =================

    static void BuildBuilding(Transform parent, Vector3 localPos, Vector3 size, System.Random rng)
    {
        string[] palette = { "BuildingA", "BuildingB", "BuildingC", "BuildingD" };
        var mat = M(palette[rng.Next(palette.Length)]);
        var b = Box(parent, "Building", localPos + new Vector3(0, size.y / 2f, 0), size, mat);
        // 지붕 테두리 + 옥상 구조물 (로우폴리 느낌)
        Box(b.transform.parent, "Roof", localPos + new Vector3(0, size.y + 0.15f, 0), new Vector3(size.x + 0.3f, 0.3f, size.z + 0.3f), M("Roof"), false);
        if (rng.NextDouble() < 0.6)
            Box(b.transform.parent, "RoofBox", localPos + new Vector3(Rand(rng, -1f, 1f), size.y + 0.8f, Rand(rng, -1f, 1f)), new Vector3(1.6f, 1f, 1.6f), M("Roof"), false);
        // 1층 문
        Box(b.transform.parent, "Door", localPos + new Vector3(0, 1.1f, -size.z / 2f - 0.03f), new Vector3(1.2f, 2.2f, 0.08f), M("Door"), false);
    }

    static void BuildPlaza(Transform block, System.Random rng, List<Vector3> hideSpots)
    {
        Box(block, "Grass", new Vector3(0, 0.11f, 0), new Vector3(BlockSize, 0.02f, BlockSize), M("Grass"), false);
        // 모서리 나무 (감옥이 들어갈 중앙은 비워둔다)
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sz = -1; sz <= 1; sz += 2)
        {
            if (rng.NextDouble() < 0.25) continue;
            BuildTree(block, new Vector3(sx * 4.1f, 0, sz * 4.1f), rng);
        }
        // 가장자리 수풀 = 은신처
        int bushes = 1 + rng.Next(2);
        for (int i = 0; i < bushes; i++)
        {
            int side = rng.Next(4);
            var p = Quaternion.Euler(0, side * 90, 0) * new Vector3(Rand(rng, -2.5f, 2.5f), 0, 4.2f);
            BuildBush(block, p);
            hideSpots.Add(block.position + p);
        }
    }

    static void BuildTree(Transform parent, Vector3 p, System.Random rng)
    {
        var t = new GameObject("Tree").transform;
        t.SetParent(parent, false);
        t.localPosition = p;
        Box(t, "Trunk", new Vector3(0, 0.9f, 0), new Vector3(0.4f, 1.8f, 0.4f), M("Trunk"));
        float h = Rand(rng, 1.6f, 2.4f);
        var a = Box(t, "Leaves", new Vector3(0, 1.8f + h * 0.5f, 0), new Vector3(1.9f, h, 1.9f), M("Leaves"), false);
        a.transform.localRotation = Quaternion.Euler(0, 45, 0);
        Box(t, "LeavesTop", new Vector3(0, 1.8f + h + 0.4f, 0), new Vector3(1.1f, 0.8f, 1.1f), M("Leaves"), false);
    }

    static void BuildBush(Transform parent, Vector3 p)
    {
        var root = new GameObject("Bush (HideSpot)");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = p;
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.6f, 0);
        col.size = new Vector3(1.8f, 1.2f, 1.4f);
        var a = Box(root.transform, "Leaf", new Vector3(-0.35f, 0.55f, 0), new Vector3(1.1f, 1.1f, 1.1f), M("Bush"), false);
        a.transform.localRotation = Quaternion.Euler(0, 30, 0);
        var b = Box(root.transform, "Leaf", new Vector3(0.45f, 0.45f, 0.1f), new Vector3(0.95f, 0.9f, 0.95f), M("Bush"), false);
        b.transform.localRotation = Quaternion.Euler(0, 65, 0);
        var spot = root.AddComponent<HideSpot>();
        spot.range = 2.2f;
    }

    static void BuildDumpster(Transform parent, Vector3 p, float yaw)
    {
        var root = new GameObject("Dumpster (HideSpot)");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = p;
        root.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.6f, 0);
        col.size = new Vector3(1f, 1.2f, 1.6f);
        Box(root.transform, "Body", new Vector3(0, 0.55f, 0), new Vector3(1f, 1.1f, 1.6f), M("Dumpster"), false);
        Box(root.transform, "Lid", new Vector3(0, 1.15f, 0), new Vector3(1.05f, 0.1f, 1.65f), M("Roof"), false);
        var spot = root.AddComponent<HideSpot>();
        spot.range = 2f;
    }

    // ================= 캐릭터 =================

    static GameObject BuildPlayer(Vector3 pos)
    {
        var go = new GameObject("Player (Thief)");
        go.transform.position = pos;
        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0, 0.9f, 0);
        BuildBody(go.transform, M("Player"), true);

        var thief = go.AddComponent<Thief>();
        thief.displayName = "나";
        thief.isPlayer = true;
        thief.energyDrinks = 1;
        go.AddComponent<ThiefPlayer>();
        go.AddComponent<VisionSystem>();

        // 정면 시야 스포트라이트 + 주변 은은한 빛
        var spot = new GameObject("Vision Spot").AddComponent<Light>();
        spot.transform.SetParent(go.transform, false);
        spot.transform.localPosition = new Vector3(0, 1.6f, -0.3f);
        spot.transform.localRotation = Quaternion.Euler(14, 0, 0);
        spot.type = LightType.Spot;
        spot.spotAngle = 110;
        spot.innerSpotAngle = 80;
        spot.range = 24;
        spot.intensity = 140;
        spot.color = new Color(1f, 0.95f, 0.85f);
        spot.shadows = LightShadows.Soft;

        var aura = new GameObject("Aura").AddComponent<Light>();
        aura.transform.SetParent(go.transform, false);
        aura.transform.localPosition = new Vector3(0, 3.5f, 0);
        aura.type = LightType.Point;
        aura.range = 7;
        aura.intensity = 4;
        aura.color = new Color(1f, 0.9f, 0.8f);
        return go;
    }

    static Thief BuildBotThief(string name, Vector3 pos, Material mat)
    {
        var go = new GameObject($"Bot {name}");
        go.transform.position = pos;
        var col = go.AddComponent<CapsuleCollider>();
        col.height = 1.8f;
        col.radius = 0.4f;
        col.center = new Vector3(0, 0.9f, 0);
        var agent = go.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.8f;
        agent.acceleration = 30;
        agent.angularSpeed = 720;
        agent.stoppingDistance = 0.3f;
        BuildBody(go.transform, mat, true);
        var thief = go.AddComponent<Thief>();
        thief.displayName = name;
        go.AddComponent<ThiefBot>();
        return thief;
    }

    static Cop BuildCop(string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        var col = go.AddComponent<CapsuleCollider>();
        col.height = 1.9f;
        col.radius = 0.4f;
        col.center = new Vector3(0, 0.95f, 0);
        var agent = go.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.9f;
        agent.acceleration = 30;
        agent.angularSpeed = 540;
        BuildBody(go.transform, M("Cop"), false);
        // 경찰 모자 + 배지
        Box(go.transform, "Cap", new Vector3(0, 1.88f, 0), new Vector3(0.75f, 0.18f, 0.75f), M("CopCap"), false);
        Box(go.transform, "CapBrim", new Vector3(0, 1.8f, 0.3f), new Vector3(0.6f, 0.05f, 0.35f), M("CopCap"), false);
        Box(go.transform, "Badge", new Vector3(0.15f, 1.25f, 0.4f), new Vector3(0.16f, 0.16f, 0.05f), M("SafeFull"), false);
        var fx = Box(go.transform, "StunFx", new Vector3(0, 2.4f, 0), new Vector3(0.35f, 0.35f, 0.35f), M("Clue"), false);
        fx.transform.localRotation = Quaternion.Euler(45, 45, 0);
        var cop = go.AddComponent<Cop>();
        cop.displayName = name;
        cop.stunFx = fx;
        fx.SetActive(false);
        return cop;
    }

    // 캡슐 몸통 + 진행 방향 표시(도둑 = 눈가리개, 경찰 = 얼굴)
    static void BuildBody(Transform parent, Material bodyMat, bool thiefMask)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.name = "Body";
        body.transform.SetParent(parent, false);
        body.transform.localPosition = new Vector3(0, 0.9f, 0);
        body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        body.GetComponent<Renderer>().sharedMaterial = bodyMat;
        if (thiefMask)
            Box(parent, "Mask", new Vector3(0, 1.45f, 0.2f), new Vector3(0.74f, 0.18f, 0.45f), M("Mask"), false);
        else
            Box(parent, "Face", new Vector3(0, 1.45f, 0.33f), new Vector3(0.45f, 0.2f, 0.12f), M("Skin"), false);
    }

    // ================= 유틸 =================

    public static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static float Rand(System.Random rng, float min, float max) => (float)(min + rng.NextDouble() * (max - min));

    public static Material M(string name) => mats[name];

    static void CreateMaterials()
    {
        mats.Clear();
        Mat("Road", "#3A3F4B");
        Mat("Sidewalk", "#6B7080");
        Mat("Wall", "#2A2E38");
        Mat("Grass", "#4E8C4A");
        Mat("BuildingA", "#C98B6B");
        Mat("BuildingB", "#8FA6C1");
        Mat("BuildingC", "#D9C38C");
        Mat("BuildingD", "#9C8BB8");
        Mat("Roof", "#4A4F5C");
        Mat("Door", "#5A3E2B");
        Mat("Trunk", "#6B4A2F");
        Mat("Leaves", "#5FA052");
        Mat("Bush", "#3F7A3A");
        Mat("Dumpster", "#2F6B5A");
        Mat("Player", "#FF8A3D");
        Mat("BotThief", "#E05A5A");
        Mat("BotThief2", "#D46BB0");
        Mat("Mask", "#151515");
        Mat("Cop", "#2F5BD3");
        Mat("CopCap", "#1A2550");
        Mat("Skin", "#F0C9A0");
        Mat("SafeBody", "#55606E");
        Mat("SafeFull", "#FFC93C", 1.6f);
        Mat("SafeEmpty", "#3A3A3A");
        Mat("Clue", "#FFF27A", 2.2f);
        Mat("Drink", "#3CE0FF", 2f);
        Mat("PrisonFrame", "#2B2B30");
        Mat("PrisonFloor", "#5A2E2E");
        Mat("Alarm", "#FF3B30", 3f);
        Mat("ExitFrame", "#3CFF7A", 1.5f);
        Mat("ExitBeam", "#3CFF7A", 3f);
    }

    static void Mat(string name, string hex, float emission = 0f)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        ColorUtility.TryParseHtmlString(hex, out var color);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.12f);
        if (emission > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(mat);
        mats[name] = mat;
    }

    // 시야 스포트라이트가 벽에 가려지도록 URP 추가 광원 그림자 활성화
    static void EnableAdditionalLightShadows()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) return;
        var so = new SerializedObject(rp);
        var prop = so.FindProperty("m_AdditionalLightShadowsSupported");
        if (prop != null && !prop.boolValue)
        {
            prop.boolValue = true;
            so.ApplyModifiedProperties();
        }
    }
}
