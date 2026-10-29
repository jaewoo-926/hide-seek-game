using System.Collections.Generic;
using System.IO;
using Chase;
using Chase.Net;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// 메뉴: Chase > Build Network Scenes
// Title(방 만들기/참가) → Lobby(대기방) 씬과 NetworkManager / 대기방 플레이어 프리팹을 생성한다.
public static class ChaseNetworkSetup
{
    const string Root = "Assets/Chase";
    const string PrefabDir = Root + "/Prefabs";
    const string SceneDir = Root + "/Scenes";
    const string TitlePath = SceneDir + "/Title.unity";
    const string LobbyPath = SceneDir + "/Lobby.unity";
    const string GamePath = SceneDir + "/ChasePrototype.unity";
    const string OnlinePath = SceneDir + "/ChaseOnline.unity";
    const string MatDir = Root + "/Materials";

    [MenuItem("Chase/Build Network Scenes")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
    }

    public static void Build()
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(SceneDir);

        // 인게임 씬을 먼저 만들어 머티리얼을 준비한다
        var onlinePlayerPrefab = BuildOnlinePlayerPrefabSafe();
        var onlineGamePrefab = BuildOnlineGamePrefabSafe();
        BuildOnlineScene(onlineGamePrefab, onlinePlayerPrefab);

        var playerPrefab = BuildLobbyPlayerPrefab();
        var networkManagerPrefab = BuildNetworkManagerPrefab();
        BuildLobbyScene();
        BuildTitleScene(networkManagerPrefab, playerPrefab, new[] { onlineGamePrefab, onlinePlayerPrefab });

        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(TitlePath, true),
            new EditorBuildSettingsScene(LobbyPath, true),
            new EditorBuildSettingsScene(OnlinePath, true),
        };
        if (File.Exists(GamePath)) scenes.Add(new EditorBuildSettingsScene(GamePath, true));
        foreach (var s in EditorBuildSettings.scenes)
            if (scenes.TrueForAll(x => x.path != s.path)) scenes.Add(s);
        EditorBuildSettings.scenes = scenes.ToArray();

        EditorSceneManager.OpenScene(TitlePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[Chase] 네트워크 씬 생성 완료: Title → Lobby");
    }

    // ---------------- 프리팹 ----------------

    static GameObject BuildLobbyPlayerPrefab()
    {
        var go = new GameObject("LobbyPlayer");
        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0, 0.9f, 0);

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localPosition = new Vector3(0, 0.9f, 0);
        body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        body.GetComponent<Renderer>().sharedMaterial = Mat("Player");

        var (thiefParts, copParts) = BuildRoleParts(go.transform);

        go.AddComponent<NetworkObject>();
        var nt = go.AddComponent<NetworkTransform>();
        nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        nt.SyncRotAngleX = false;
        nt.SyncRotAngleZ = false;
        nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        nt.Interpolate = true;

        var lp = go.AddComponent<LobbyPlayer>();
        lp.bodyRenderer = body.GetComponent<Renderer>();
        lp.thiefParts = thiefParts;
        lp.copParts = copParts;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/LobbyPlayer.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // 도둑: 눈가리개 + 가방 / 경찰: 모자 + 배지
    static (GameObject thief, GameObject cop) BuildRoleParts(Transform root)
    {
        var thief = new GameObject("ThiefParts");
        thief.transform.SetParent(root, false);
        Cube(thief.transform, "Mask", new Vector3(0, 1.45f, 0.2f), new Vector3(0.74f, 0.18f, 0.45f), Mat("Mask"), false);
        Cube(thief.transform, "Backpack", new Vector3(0, 1.0f, -0.42f), new Vector3(0.5f, 0.6f, 0.25f), Mat("Door"), false);

        var cop = new GameObject("CopParts");
        cop.transform.SetParent(root, false);
        Cube(cop.transform, "Cap", new Vector3(0, 1.88f, 0), new Vector3(0.75f, 0.18f, 0.75f), Mat("CopCap"), false);
        Cube(cop.transform, "CapBrim", new Vector3(0, 1.8f, 0.3f), new Vector3(0.6f, 0.05f, 0.35f), Mat("CopCap"), false);
        Cube(cop.transform, "Badge", new Vector3(0.15f, 1.25f, 0.4f), new Vector3(0.16f, 0.16f, 0.05f), Mat("SafeFull"), false);
        Cube(cop.transform, "Face", new Vector3(0, 1.45f, 0.33f), new Vector3(0.45f, 0.2f, 0.12f), Mat("Skin"), false);
        cop.SetActive(false);
        return (thief, cop);
    }

    static GameObject BuildOnlinePlayerPrefab()
    {
        var go = new GameObject("OnlinePlayer");
        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0, 0.9f, 0);

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localPosition = new Vector3(0, 0.9f, 0);
        body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        body.GetComponent<Renderer>().sharedMaterial = Mat("Player");

        var (thiefParts, copParts) = BuildRoleParts(go.transform);
        var fx = Cube(go.transform, "StunFx", new Vector3(0, 2.4f, 0), new Vector3(0.35f, 0.35f, 0.35f), Mat("Clue"), false);
        fx.transform.localRotation = Quaternion.Euler(45, 45, 0);
        fx.SetActive(false);

        // 정면 시야 스포트라이트 + 주변 빛 (소유자만 켬)
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

        go.AddComponent<NetworkObject>();
        var nt = go.AddComponent<NetworkTransform>();
        nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        nt.SyncRotAngleX = false;
        nt.SyncRotAngleZ = false;
        nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        nt.Interpolate = true;

        var op = go.AddComponent<Chase.Online.OnlinePlayer>();
        op.bodyRenderer = body.GetComponent<Renderer>();
        op.thiefParts = thiefParts;
        op.copParts = copParts;
        op.stunFx = fx;
        op.visionLight = spot;
        op.auraLight = aura;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/OnlinePlayer.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject BuildOnlineGamePrefab()
    {
        var go = new GameObject("OnlineGame");
        go.AddComponent<NetworkObject>();
        ChaseSceneBuilder.AddSpawner(go);
        go.AddComponent<Chase.Online.OnlineGame>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/OnlineGame.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static void BuildOnlineScene(GameObject gamePrefab, GameObject playerPrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var info = ChaseSceneBuilder.BuildLevel();

        // 경찰 대기 장소 (맵 밖 경찰서 앞마당)
        var station = new GameObject("Police Station").transform;
        var copWait = new Vector3(0, 0, -52f);
        Cube(station, "Yard", copWait + new Vector3(0, -0.5f, 0), new Vector3(10, 1, 6), Mat("Sidewalk"));
        Cube(station, "Building", copWait + new Vector3(0, 1.5f, -4f), new Vector3(10, 3, 2), Mat("Cop"));

        var center = new GameObject("Map Center").transform;
        center.position = Vector3.zero;

        var data = new GameObject("Map Data").AddComponent<Chase.Online.OnlineMapData>();
        data.thiefSpawn = info.thiefSpawn;
        data.copWaitPoint = copWait;
        data.mapCenter = center;
        data.copSpawnPoints = info.copSpawnPoints;
        data.prisonSpots = info.prisonSpots;
        data.itemSpots = info.itemSpots;

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 35;
        cam.farClipPlane = 250;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
        camGo.AddComponent<AudioListener>();
        var qv = camGo.AddComponent<QuarterViewCamera>();
        qv.maxDistance = 90f;
        camGo.transform.rotation = Quaternion.Euler(qv.pitch, qv.yaw, 0);
        camGo.transform.position = -(camGo.transform.rotation * Vector3.forward) * qv.distance;

        var boot = new GameObject("Online Bootstrap");
        var b = boot.AddComponent<Chase.Online.OnlineBootstrap>();
        b.gamePrefab = gamePrefab;
        b.playerPrefab = playerPrefab;
        boot.AddComponent<Chase.Online.OnlineHUD>();

        EditorSceneManager.SaveScene(scene, OnlinePath);
    }

    static GameObject BuildNetworkManagerPrefab()
    {
        var go = new GameObject("NetworkManager");
        var transport = go.AddComponent<UnityTransport>();
        var nm = go.AddComponent<NetworkManager>();
        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.PlayerPrefab = null;          // 대기방 씬에서 직접 스폰
        nm.NetworkConfig.EnableSceneManagement = true;
        nm.NetworkConfig.ConnectionApproval = false;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/NetworkManager.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ---------------- 씬 ----------------

    // 머티리얼은 BuildLevel 에서 갱신되므로, 프리팹 생성 전에 한 번 준비해 둔다
    static GameObject BuildOnlinePlayerPrefabSafe()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ChaseSceneBuilder.BuildLevel();
        return BuildOnlinePlayerPrefab();
    }

    static GameObject BuildOnlineGamePrefabSafe() => BuildOnlineGamePrefab();

    static void BuildTitleScene(GameObject networkManagerPrefab, GameObject playerPrefab, GameObject[] extraPrefabs)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.12f);
        cam.gameObject.AddComponent<AudioListener>();

        var boot = new GameObject("Session Bootstrap");
        var sm = boot.AddComponent<SessionManager>();
        sm.networkManagerPrefab = networkManagerPrefab;
        sm.lobbyPlayerPrefab = playerPrefab;
        sm.extraNetworkPrefabs = extraPrefabs;

        new GameObject("Title UI").AddComponent<TitleUI>();

        EditorSceneManager.SaveScene(scene, TitlePath);
    }

    static void BuildLobbyScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.48f);
        RenderSettings.skybox = null;
        var sun = new GameObject("Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.93f, 0.82f);
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(55, -30, 0);

        // 은신처 느낌의 아지트 대기방 (24 x 18)
        var room = new GameObject("Hideout Room").transform;
        const float w = 24f, d = 18f, wallH = 2.6f;
        Cube(room, "Floor", new Vector3(0, -0.5f, 0), new Vector3(w, 1, d), Mat("Sidewalk"));
        Cube(room, "Carpet", new Vector3(0, 0.01f, 0), new Vector3(10, 0.02f, 7), Mat("PrisonFloor"), false);
        Cube(room, "Wall N", new Vector3(0, wallH / 2, d / 2 + 0.25f), new Vector3(w + 1, wallH, 0.5f), Mat("BuildingA"));
        Cube(room, "Wall S", new Vector3(0, wallH / 2, -d / 2 - 0.25f), new Vector3(w + 1, wallH, 0.5f), Mat("BuildingA"));
        Cube(room, "Wall E", new Vector3(w / 2 + 0.25f, wallH / 2, 0), new Vector3(0.5f, wallH, d), Mat("BuildingA"));
        Cube(room, "Wall W", new Vector3(-w / 2 - 0.25f, wallH / 2, 0), new Vector3(0.5f, wallH, d), Mat("BuildingA"));

        // 중앙 작전 테이블 + 도시 지도
        Cube(room, "Table", new Vector3(0, 0.45f, 0), new Vector3(4.5f, 0.9f, 2.6f), Mat("Trunk"));
        Cube(room, "Map", new Vector3(0, 0.91f, 0), new Vector3(4f, 0.02f, 2.1f), Mat("BuildingB"), false);
        Cube(room, "Pin", new Vector3(0.9f, 1.0f, 0.4f), new Vector3(0.15f, 0.15f, 0.15f), Mat("Alarm"), false);
        Cube(room, "Pin", new Vector3(-1.2f, 1.0f, -0.5f), new Vector3(0.15f, 0.15f, 0.15f), Mat("SafeFull"), false);

        // 소품: 금고, 상자, 소파, 화분, 출구 문
        Cube(room, "Safe", new Vector3(-10.5f, 0.6f, 7.5f), new Vector3(1.2f, 1.2f, 1f), Mat("SafeBody"));
        Cube(room, "SafeDoor", new Vector3(-10.5f, 0.65f, 6.98f), new Vector3(0.8f, 0.8f, 0.05f), Mat("SafeFull"), false);
        for (int i = 0; i < 4; i++)
            Cube(room, "Crate", new Vector3(9.8f - (i % 2) * 1.3f, 0.5f + (i / 2) * 1f, 7.2f - (i % 2) * 0.3f), new Vector3(1.1f, 1f, 1.1f), Mat("Door"));
        Cube(room, "Sofa", new Vector3(-9.5f, 0.35f, -6.8f), new Vector3(4f, 0.7f, 1.4f), Mat("BotThief"));
        Cube(room, "SofaBack", new Vector3(-9.5f, 0.9f, -7.4f), new Vector3(4f, 1.1f, 0.4f), Mat("BotThief"));
        foreach (var p in new[] { new Vector3(11, 0, -8), new Vector3(-11, 0, 0), new Vector3(11, 0, 0) })
        {
            Cube(room, "Pot", p + new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.6f, 0.7f), Mat("Roof"));
            Cube(room, "Plant", p + new Vector3(0, 1.0f, 0), new Vector3(0.9f, 0.9f, 0.9f), Mat("Leaves"), false).transform.rotation = Quaternion.Euler(0, 45, 0);
        }
        Cube(room, "Exit Door", new Vector3(0, 1.2f, d / 2 - 0.02f), new Vector3(2.2f, 2.4f, 0.1f), Mat("ExitFrame"), false);

        // 스폰 위치: 테이블 주변 원형
        var spawnRoot = new GameObject("Spawn Points").transform;
        var spawns = new List<Transform>();
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * Mathf.PI * 2f;
            var t = new GameObject($"Spawn {i}").transform;
            t.SetParent(spawnRoot, false);
            t.position = new Vector3(Mathf.Cos(a) * 4.8f, 0.05f, Mathf.Sin(a) * 3.8f);
            t.rotation = Quaternion.LookRotation(-t.position.normalized);
            spawns.Add(t);
        }

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 35;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.12f);
        camGo.AddComponent<AudioListener>();
        var qv = camGo.AddComponent<QuarterViewCamera>();
        qv.distance = 24f;
        qv.minDistance = 14f;
        qv.maxDistance = 34f;
        camGo.transform.rotation = Quaternion.Euler(qv.pitch, qv.yaw, 0);
        camGo.transform.position = -(camGo.transform.rotation * Vector3.forward) * qv.distance;

        var lobby = new GameObject("Lobby");
        lobby.AddComponent<LobbyRoom>().spawnPoints = spawns.ToArray();
        lobby.AddComponent<LobbyUI>();

        EditorSceneManager.SaveScene(scene, LobbyPath);
    }

    // ---------------- 유틸 ----------------

    static GameObject Cube(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Material Mat(string name)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");
        if (m == null) Debug.LogWarning($"[Chase] 머티리얼 없음: {name} (먼저 Chase > Build Prototype Scene 실행)");
        return m;
    }
}
