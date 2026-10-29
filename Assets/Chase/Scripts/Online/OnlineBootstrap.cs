using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chase.Online
{
    // 인게임 씬 진입 시 서버(방장)가 게임 오브젝트와 플레이어들을 스폰한다.
    // 모든 클라이언트가 씬 로드를 마친 뒤(OnLoadEventCompleted) 스폰해야 안전하다.
    public class OnlineBootstrap : MonoBehaviour
    {
        public GameObject gamePrefab;
        public GameObject playerPrefab;

        NetworkManager net;
        bool spawned;

        void Start()
        {
            net = NetworkManager.Singleton;
            if (net == null || !net.IsServer) return;
            net.SceneManager.OnLoadEventCompleted += OnLoadCompleted;
        }

        void OnDestroy()
        {
            if (net != null && net.SceneManager != null) net.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;
        }

        void OnLoadCompleted(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> completed, System.Collections.Generic.List<ulong> timedOut)
        {
            if (spawned || sceneName != gameObject.scene.name) return;
            spawned = true;

            Instantiate(gamePrefab).GetComponent<NetworkObject>().Spawn(true);

            var map = FindAnyObjectByType<OnlineMapData>();
            int thiefIndex = 0, copIndex = 0;
            foreach (var id in net.ConnectedClientsIds)
            {
                MatchSetup.Players.TryGetValue(id, out var entry);
                if (string.IsNullOrEmpty(entry.nickname)) entry.nickname = $"Player{id}";

                bool cop = entry.role == Role.Cop;
                Vector3 basePos = cop ? map.copWaitPoint : map.thiefSpawn;
                int i = cop ? copIndex++ : thiefIndex++;
                Vector3 pos = basePos + new Vector3((i % 3) * 1.5f, 0.05f, (i / 3) * 1.5f);

                var go = Instantiate(playerPrefab, pos, Quaternion.identity);
                go.GetComponent<NetworkObject>().SpawnAsPlayerObject(id, true);
                var p = go.GetComponent<OnlinePlayer>();
                p.RoleValue.Value = (int)entry.role;
                p.Nickname.Value = new Unity.Collections.FixedString64Bytes(entry.nickname);
                p.ColorIndex.Value = entry.color;
                p.StateValue.Value = (int)(cop ? PState.Waiting : PState.Free);
                p.HasCuffs.Value = true;
                // 소유자 권한 NetworkTransform 이라 소유자 쪽에서도 시작 위치를 확정해 준다
                p.ServerTeleport(pos);
            }
        }
    }
}
