using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chase.Net
{
    // 방(세션) 생성/참가/목록/나가기 담당. 씬이 바뀌어도 유지된다.
    // 방을 만들면 Relay 를 통해 NGO 호스트가 시작되고, 참가하면 NGO 클라이언트로 접속한다.
    public class SessionManager : MonoBehaviour
    {
        public const string TitleScene = "Title";
        public const string LobbyScene = "Lobby";
        public const int NicknameMaxLength = 12;

        public static SessionManager I { get; private set; }

        public const string GameScene = "ChaseOnline";

        public GameObject networkManagerPrefab;
        public GameObject lobbyPlayerPrefab;
        public GameObject[] extraNetworkPrefabs;

        // 대기방에서 고른 팀 (0 = 도둑, 1 = 경찰). 대기방으로 돌아와도 유지
        public int PreferredRole { get; set; }

        public ISession Session { get; private set; }
        public bool IsSignedIn { get; private set; }
        public bool Busy { get; private set; }
        public string Status { get; private set; } = "서버 연결 중...";
        public string Error { get; set; }
        public List<ISessionInfo> Rooms { get; } = new List<ISessionInfo>();

        string nickname;
        bool leaving;

        public string Nickname
        {
            get => nickname;
            set
            {
                nickname = string.IsNullOrWhiteSpace(value) ? "도둑" : value.Trim();
                if (nickname.Length > NicknameMaxLength) nickname = nickname.Substring(0, NicknameMaxLength);
                PlayerPrefs.SetString("chase.nickname", nickname);
            }
        }

        void Awake()
        {
            // 타이틀로 돌아올 때 생기는 중복 방지
            if (I != null && I != this)
            {
                Destroy(gameObject);
                return;
            }
            I = this;
            DontDestroyOnLoad(gameObject);
            // 창이 포커스를 잃어도 계속 통신해야 Relay 타임아웃으로 튕기지 않는다
            Application.runInBackground = true;
            Nickname = PlayerPrefs.GetString("chase.nickname", "도둑" + UnityEngine.Random.Range(100, 1000));

            if (NetworkManager.Singleton == null)
            {
                var nm = Instantiate(networkManagerPrefab).GetComponent<NetworkManager>();
                nm.name = "NetworkManager";
            }
            var net = NetworkManager.Singleton;
            if (!net.NetworkConfig.Prefabs.Contains(lobbyPlayerPrefab))
                net.AddNetworkPrefab(lobbyPlayerPrefab);
            foreach (var prefab in extraNetworkPrefabs)
                if (prefab && !net.NetworkConfig.Prefabs.Contains(prefab))
                    net.AddNetworkPrefab(prefab);
            net.OnClientStopped += OnNetworkStopped;
        }

        async void Start()
        {
            try
            {
                // 같은 PC 에서 여러 창으로 테스트할 수 있도록 실행마다 다른 익명 계정 사용
                string profile = "p" + Guid.NewGuid().ToString("N").Substring(0, 12);
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    var options = new InitializationOptions();
                    options.SetProfile(profile);
                    await UnityServices.InitializeAsync(options);
                }
                else
                {
                    // Enter Play Mode 에서 도메인 리로드가 꺼져 있으면 이전 플레이의 세션/로그인이 남아 있다
                    await LeaveStaleSessionsAsync();
                    if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(true);
                    AuthenticationService.Instance.SwitchProfile(profile);
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                IsSignedIn = true;
                Status = "";
                if (!HandleCommandLine()) await RefreshRoomsAsync();
            }
            catch (Exception e)
            {
                Status = "";
                Error = "서버 연결 실패: " + e.Message;
                Debug.LogException(e);
            }
        }

        public Task CreateRoomAsync(string roomName, int maxPlayers, bool isPrivate) =>
            Run("방 만드는 중...", async () =>
            {
                var options = new SessionOptions
                {
                    Name = string.IsNullOrWhiteSpace(roomName) ? $"{Nickname}의 방" : roomName.Trim(),
                    MaxPlayers = maxPlayers,
                    IsPrivate = isPrivate,
                }.WithRelayNetwork();

                Session = await MultiplayerService.Instance.CreateSessionAsync(options);
                // 호스트가 대기방 씬을 로드하면 접속한 클라이언트들도 자동으로 따라온다
                NetworkManager.Singleton.SceneManager.LoadScene(LobbyScene, LoadSceneMode.Single);
            });

        public Task JoinByCodeAsync(string code) =>
            Run("방 참가 중...", async () =>
            {
                string clean = SanitizeCode(code);
                if (clean.Length == 0) throw new ArgumentException("참가 코드를 확인해 주세요.");
                Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(clean);
            });

        // 테스트용 실행 인자: -nick 이름  -role cop|thief  -join 참가코드
        bool HandleCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            var nick = Arg("-nick");
            if (nick != null) Nickname = nick;
            var role = Arg("-role");
            if (role != null) PreferredRole = role == "cop" ? 1 : 0;
            var code = Arg("-join");
            if (code == null) return false;
            _ = JoinByCodeAsync(code);
            return true;
        }

        // 참가 코드는 영문 대문자/숫자만 사용
        public static string SanitizeCode(string code)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (code ?? "").ToUpperInvariant())
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
            return sb.ToString();
        }

        static async Task LeaveStaleSessionsAsync()
        {
            foreach (var s in new List<ISession>(MultiplayerService.Instance.Sessions.Values))
            {
                try { await s.LeaveAsync(); }
                catch (Exception e) { Debug.LogWarning("이전 세션 정리 실패: " + e.Message); }
            }
        }

        // 도메인 리로드 없이 Play 모드에 들어가도 정적 상태가 남지 않도록 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            I = null;
            LobbyPlayer.All.Clear();
            Interactable.All.Clear();
            Pickup.All.Clear();
            Chase.Online.OnlinePlayer.All.Clear();
        }

        public Task JoinByIdAsync(string sessionId) =>
            Run("방 참가 중...", async () =>
            {
                Session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId);
            });

        public Task RefreshRoomsAsync() =>
            Run("방 목록 불러오는 중...", async () =>
            {
                var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions { Count = 20 });
                Rooms.Clear();
                Rooms.AddRange(results.Sessions);
            });

        // 방장: 대기방 → 인게임. 게임 중에는 새 참가를 막는다.
        public void StartGame()
        {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsHost) return;
            SetLocked(true);
            net.SceneManager.LoadScene(GameScene, LoadSceneMode.Single);
        }

        // 방장: 인게임 → 대기방
        public void ReturnToLobby()
        {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsHost) return;
            SetLocked(false);
            net.SceneManager.LoadScene(LobbyScene, LoadSceneMode.Single);
        }

        async void SetLocked(bool locked)
        {
            if (Session == null || !Session.IsHost) return;
            try
            {
                var host = Session.AsHost();
                host.IsLocked = locked;
                await host.SavePropertiesAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("방 잠금 변경 실패: " + e.Message);
            }
        }

        public async void LeaveRoom()
        {
            leaving = true;
            try
            {
                if (Session != null) await Session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("세션 나가기 실패: " + e.Message);
            }
            Session = null;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene(TitleScene);
            leaving = false;
        }

        // 호스트가 나가거나 연결이 끊기면 타이틀로 복귀
        void OnNetworkStopped(bool wasHost)
        {
            if (leaving) return;
            Session = null;
            if (SceneManager.GetActiveScene().name != TitleScene)
            {
                Error = wasHost ? "방이 종료되었습니다." : "방과의 연결이 끊어졌습니다.";
                SceneManager.LoadScene(TitleScene);
            }
        }

        async Task Run(string status, Func<Task> action)
        {
            if (Busy) return;
            Busy = true;
            Status = status;
            Error = null;
            try
            {
                await action();
            }
            catch (SessionException e)
            {
                Error = $"실패 ({e.Error}): {e.Message}";
                Debug.LogWarning(e);
            }
            catch (Exception e)
            {
                Error = "실패: " + e.Message;
                Debug.LogException(e);
            }
            finally
            {
                Busy = false;
                Status = "";
            }
        }

        void OnDestroy()
        {
            if (I != this) return;
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped -= OnNetworkStopped;
        }
    }
}
