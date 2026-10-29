using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chase.Net
{
    // 대기방 씬: 서버(방장)가 접속한 플레이어마다 캐릭터를 스폰한다
    public class LobbyRoom : MonoBehaviour
    {
        public Transform[] spawnPoints;

        NetworkManager net;
        bool ready;

        void Start()
        {
            net = NetworkManager.Singleton;
            if (net == null || !net.IsServer) return;

            net.OnClientConnectedCallback += OnClientConnected;
            if (net.ConnectedClientsIds.Count <= 1)
            {
                // 방을 막 만든 경우: 방장 혼자라 바로 스폰
                SpawnAll();
            }
            else
            {
                // 인게임에서 돌아온 경우: 모든 클라이언트가 씬 로드를 마친 뒤 스폰
                net.SceneManager.OnLoadEventCompleted += OnLoadCompleted;
            }
        }

        void OnDestroy()
        {
            if (net == null) return;
            net.OnClientConnectedCallback -= OnClientConnected;
            if (net.SceneManager != null) net.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;
        }

        void OnLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (sceneName == gameObject.scene.name) SpawnAll();
        }

        void OnClientConnected(ulong clientId)
        {
            if (ready) SpawnPlayer(clientId);
        }

        void SpawnAll()
        {
            ready = true;
            foreach (var id in net.ConnectedClientsIds) SpawnPlayer(id);
        }

        void SpawnPlayer(ulong clientId)
        {
            if (net.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null &&
                client.PlayerObject.GetComponent<LobbyPlayer>() != null) return;

            var prefab = SessionManager.I.lobbyPlayerPrefab;
            var spawn = spawnPoints[(int)(clientId % (ulong)spawnPoints.Length)];
            var go = Instantiate(prefab, spawn.position, spawn.rotation);
            int color = NextFreeColor();
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
            go.GetComponent<LobbyPlayer>().ColorIndex.Value = color;
        }

        static int NextFreeColor()
        {
            var used = new HashSet<int>();
            foreach (var p in LobbyPlayer.All) used.Add(p.ColorIndex.Value);
            for (int i = 0; i < LobbyPlayer.Palette.Length; i++)
                if (!used.Contains(i)) return i;
            return 0;
        }
    }
}
