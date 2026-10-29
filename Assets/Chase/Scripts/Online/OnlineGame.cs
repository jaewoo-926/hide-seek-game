using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Chase.Online
{
    public enum OnlinePhase { Heist, LastTime, ThievesWin, CopsWin }
    public enum InteractKind { None, Safe, Hide, Prison, Exit }

    public struct Interaction
    {
        public InteractKind kind;
        public int index;
        public string prompt;
        public float holdTime;

        public bool IsValid => kind != InteractKind.None;
        public bool SameTarget(Interaction o) => kind == o.kind && index == o.index;
    }

    // 인게임 규칙 + 월드 동기화.
    // 금고/단서/아이템/감옥/탈출구 배치는 서버가 정한 시드로 모든 클라이언트가 똑같이 생성하고,
    // 털림/획득 같은 변화는 비트마스크 NetworkVariable 로 맞춘다.
    [RequireComponent(typeof(WorldSpawner))]
    public class OnlineGame : NetworkBehaviour
    {
        public static OnlineGame I { get; private set; }

        [Header("규칙")]
        public int targetMoney = 1000;
        public float copEntryDelay = 20f;
        public float lastTimeDuration = 120f;
        public int safeCount = 10;
        public int drinkCount = 6;
        public float safeRespawnDelay = 45f;
        public float hideSafeTime = 8f;
        public float hideMaxTime = 11f;
        public float drinkDuration = 4f;
        public float rescueBuff = 4f;
        public float clubCooldown = 6f;
        public float clubRange = 2.3f;
        public float clubStun = 2.5f;
        public float catchRange = 1.7f;
        public float batonSlow = 2f;

        [Header("시야")]
        public float viewAngle = 110f;
        public float viewRange = 20f;
        public float nearRange = 3.5f;

        // ---- 동기화 상태 ----
        public readonly NetworkVariable<int> Seed = new NetworkVariable<int>();
        public readonly NetworkVariable<int> PhaseValue = new NetworkVariable<int>();
        public readonly NetworkVariable<double> CopEntryAt = new NetworkVariable<double>();
        public readonly NetworkVariable<double> LastTimeEndsAt = new NetworkVariable<double>();
        public readonly NetworkVariable<bool> CopsDeployed = new NetworkVariable<bool>();
        public readonly NetworkVariable<int> RobbedMask = new NetworkVariable<int>();
        public readonly NetworkVariable<int> PickedMask = new NetworkVariable<int>();   // 0~3 단서, 4~ 드링크
        public readonly NetworkVariable<int> KnownDigits = new NetworkVariable<int>();
        public readonly NetworkVariable<bool> PrisonKnown = new NetworkVariable<bool>();
        public readonly NetworkVariable<FixedString128Bytes> EndReason = new NetworkVariable<FixedString128Bytes>();

        public OnlinePhase Phase => (OnlinePhase)PhaseValue.Value;
        public bool IsPlaying => Phase == OnlinePhase.Heist || Phase == OnlinePhase.LastTime;
        public double Now => NetworkManager.ServerTime.Time;
        public float CopEntryRemaining => (float)System.Math.Max(0, CopEntryAt.Value - Now);
        public float LastTimeRemaining => (float)System.Math.Max(0, LastTimeEndsAt.Value - Now);
        public int Gauge => OnlinePlayer.All.Where(p => p.IsThief && p.State != PState.Eliminated).Sum(p => p.Money.Value);
        public float GaugeRatio => Mathf.Clamp01((float)Gauge / targetMoney);
        public bool PasswordComplete => KnownDigits.Value == 0b1111;
        public string Password { get; private set; } = "????";
        public string PasswordDisplay =>
            string.Join(" ", Enumerable.Range(0, 4).Select(i => (KnownDigits.Value & (1 << i)) != 0 ? Password[i].ToString() : "_"));
        public int CopSpawnCount => map ? map.copSpawnPoints.Count : 0;
        public Transform MapCenter => map ? map.mapCenter : transform;

        public readonly List<(string text, float time)> messages = new List<(string, float)>();
        public Vector3? PingPosition { get; private set; }
        public float PingUntil { get; private set; }

        OnlineMapData map;
        WorldSpawner spawner;
        bool worldBuilt;
        readonly List<Vector3> safePositions = new List<Vector3>();
        readonly List<int> safeMoney = new List<int>();
        readonly List<Safe> safeObjects = new List<Safe>();
        readonly List<Renderer> safeDoors = new List<Renderer>();
        readonly List<GameObject> pickupObjects = new List<GameObject>();
        readonly List<Vector3> pickupPositions = new List<Vector3>();
        readonly List<Vector3> exitPositions = new List<Vector3>();
        readonly List<ExitPoint> exitObjects = new List<ExitPoint>();
        HideSpot[] hideSpots = new HideSpot[0];
        Prison prison;
        readonly double[] safeRobbedAt = new double[32];
        Color baseAmbient;

        void Awake()
        {
            I = this;
            spawner = GetComponent<WorldSpawner>();
        }

        public override void OnNetworkSpawn()
        {
            map = FindAnyObjectByType<OnlineMapData>();
            baseAmbient = RenderSettings.ambientLight;
            hideSpots = FindObjectsByType<HideSpot>()
                .OrderBy(h => h.transform.position.x).ThenBy(h => h.transform.position.z).ToArray();

            if (IsServer)
            {
                Seed.Value = Random.Range(1, int.MaxValue);
                PhaseValue.Value = (int)OnlinePhase.Heist;
                CopEntryAt.Value = Now + copEntryDelay;
            }

            Seed.OnValueChanged += (_, __) => BuildWorld();
            RobbedMask.OnValueChanged += (_, __) => RefreshSafes();
            PickedMask.OnValueChanged += (_, __) => RefreshPickups();
            PhaseValue.OnValueChanged += OnPhaseChanged;
            BuildWorld();
            if (Phase == OnlinePhase.LastTime) SpawnExits();
        }

        public override void OnNetworkDespawn()
        {
            if (I == this) I = null;
            RenderSettings.ambientLight = baseAmbient;
        }

        // ================= 월드 생성 (모든 클라이언트 동일) =================

        void BuildWorld()
        {
            if (worldBuilt || Seed.Value == 0 || map == null) return;
            worldBuilt = true;

            var rng = new System.Random(Seed.Value);
            Random.InitState(Seed.Value); // 오브젝트 회전 등 시각 요소도 동일하게
            Password = string.Concat(Enumerable.Range(0, 4).Select(_ => rng.Next(10)));

            var prisonCandidates = map.prisonSpots.Where(p => Flat(p, map.thiefSpawn) > 20f).ToList();
            if (prisonCandidates.Count == 0) prisonCandidates = map.prisonSpots;
            Vector3 prisonPos = prisonCandidates[rng.Next(prisonCandidates.Count)];
            prison = spawner.SpawnPrison(prisonPos);

            var pool = map.itemSpots.OrderBy(_ => rng.Next()).ToList();
            var used = new List<Vector3> { prisonPos, map.thiefSpawn };

            Vector3 Take(float gap, System.Func<Vector3, bool> extra = null)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    Vector3 s = pool[i];
                    if (used.Any(u => Flat(u, s) < gap)) continue;
                    if (extra != null && !extra(s)) continue;
                    pool.RemoveAt(i);
                    used.Add(s);
                    return s;
                }
                Vector3 fallback = pool[0];
                pool.RemoveAt(0);
                used.Add(fallback);
                return fallback;
            }

            Vector3 e1 = Take(8f, s => Flat(s, map.thiefSpawn) > 25f);
            Vector3 e2 = Take(8f, s => Flat(s, e1) > 35f);
            exitPositions.Add(e1);
            exitPositions.Add(e2);

            for (int i = 0; i < safeCount; i++)
            {
                Vector3 p = Take(7f, s => Flat(s, map.thiefSpawn) > 6f);
                int money = rng.Next(10, 19) * 10;
                var safe = spawner.SpawnSafe(p, money);
                safePositions.Add(p);
                safeMoney.Add(money);
                safeObjects.Add(safe);
                safeDoors.Add(safe.transform.GetChild(1).GetComponent<Renderer>());
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = Take(6f);
                spawner.SpawnClue(p, i);
                pickupPositions.Add(p);
            }
            for (int i = 0; i < drinkCount; i++)
            {
                Vector3 p = Take(5f);
                spawner.SpawnDrink(p);
                pickupPositions.Add(p);
            }
            // 스포너가 만든 픽업 오브젝트를 생성 순서대로 수집 (Pickup.All 은 OnEnable 순서)
            pickupObjects.AddRange(Pickup.All.Select(p => p.gameObject));
            // 금고의 싱글플레이 로직은 끄고 표시용으로만 사용 (픽업은 GameManager 가 없으면 회전만 한다)
            foreach (var s in safeObjects) s.enabled = false;

            RefreshSafes();
            RefreshPickups();
        }

        void RefreshSafes()
        {
            for (int i = 0; i < safeDoors.Count; i++)
                safeDoors[i].sharedMaterial = IsRobbed(i) ? spawner.safeEmpty : spawner.safeFull;
        }

        void RefreshPickups()
        {
            for (int i = 0; i < pickupObjects.Count; i++)
                if (pickupObjects[i]) pickupObjects[i].SetActive((PickedMask.Value & (1 << i)) == 0);
        }

        void OnPhaseChanged(int previous, int current)
        {
            if ((OnlinePhase)current == OnlinePhase.LastTime)
            {
                SpawnExits();
                AddMessage("게이지 100%! 라스트 타임 - 초록 빛기둥 탈출구가 열렸다!");
            }
        }

        void SpawnExits()
        {
            if (exitObjects.Count > 0) return;
            foreach (var p in exitPositions)
            {
                var exit = spawner.SpawnExit(p);
                exit.enabled = false;
                exitObjects.Add(exit);
            }
        }

        bool IsRobbed(int i) => (RobbedMask.Value & (1 << i)) != 0;

        // ================= 서버 루프 =================

        void Update()
        {
            var local = OnlinePlayer.Local;
            // 대기 중인 경찰은 맵을 미리 볼 수 있도록 밝게
            RenderSettings.ambientLight = local && local.State == PState.Waiting ? new Color(0.55f, 0.55f, 0.62f) : baseAmbient;

            if (!IsSpawned || !IsServer || !IsPlaying || !worldBuilt) return;
            double now = Now;

            if (!CopsDeployed.Value && now >= CopEntryAt.Value) DeployCops();

            var thieves = OnlinePlayer.All.Where(p => p.IsThief).ToList();

            // 금고 재충전
            for (int i = 0; i < safeCount; i++)
                if (IsRobbed(i) && now - safeRobbedAt[i] >= safeRespawnDelay)
                    RobbedMask.Value &= ~(1 << i);

            // 아이템 줍기
            foreach (var t in thieves)
            {
                if (t.State != PState.Free) continue;
                for (int i = 0; i < pickupPositions.Count; i++)
                {
                    if ((PickedMask.Value & (1 << i)) != 0 || Flat(t.transform.position, pickupPositions[i]) > 1.4f) continue;
                    PickedMask.Value |= 1 << i;
                    if (i < 4)
                    {
                        KnownDigits.Value |= 1 << i;
                        Log($"{t.DisplayName}: 비밀번호 단서 발견!");
                    }
                    else
                    {
                        t.Drinks.Value++;
                        Log($"{t.DisplayName}: 에너지 드링크 획득");
                    }
                }

                if (!PrisonKnown.Value && Flat(t.transform.position, prison.transform.position) < 9f)
                {
                    PrisonKnown.Value = true;
                    Log($"{t.DisplayName}: 감옥 위치 발견! (도둑 팀 공유)");
                }
            }

            // 오래 숨어 있으면 들킨다
            foreach (var t in thieves)
            {
                if (t.State == PState.Hidden && now - t.hideEnteredAt > hideMaxTime)
                {
                    int idx = t.HideIndex.Value;
                    LeaveHide(t);
                    Log($"{t.DisplayName}: 은신처에서 소리가 났다!");
                    PingRpc(hideSpots[idx].transform.position);
                }
            }

            if (Phase == OnlinePhase.Heist && Gauge >= targetMoney)
            {
                LastTimeEndsAt.Value = now + lastTimeDuration;
                PhaseValue.Value = (int)OnlinePhase.LastTime;
                Log("경찰 버프 발동: 이속 증가 + 시야 해제. 이제 잡히면 즉시 탈락!");
            }

            if (Phase == OnlinePhase.LastTime && now >= LastTimeEndsAt.Value)
                End(OnlinePhase.CopsWin, "라스트 타임 종료 - 아무도 탈출하지 못했다");
            else if (thieves.Count == 0 || thieves.All(t => !t.IsActiveThief))
                End(OnlinePhase.CopsWin, "모든 도둑이 제압되었다");
            else if (!OnlinePlayer.All.Any(p => p.IsCop))
                End(OnlinePhase.ThievesWin, "경찰이 모두 나갔다");
        }

        void DeployCops()
        {
            CopsDeployed.Value = true;
            var taken = new HashSet<int>();
            foreach (var cop in OnlinePlayer.All.Where(p => p.IsCop))
            {
                int idx = cop.ChosenSpawn.Value;
                if (idx < 0 || idx >= map.copSpawnPoints.Count)
                {
                    idx = Random.Range(0, map.copSpawnPoints.Count);
                    for (int k = 0; k < map.copSpawnPoints.Count && taken.Contains(idx); k++)
                        idx = (idx + 1) % map.copSpawnPoints.Count;
                }
                taken.Add(idx);
                cop.StateValue.Value = (int)PState.Free;
                cop.ServerTeleport(map.copSpawnPoints[idx] + Random.insideUnitSphere.With(y: 0) * 1.5f);
            }
            Log("경찰 진입!");
        }

        // ================= 서버: 행동 처리 =================

        public void ServerInteract(OnlinePlayer p, InteractKind kind, int index)
        {
            if (!IsPlaying) return;
            const float slack = 1.5f; // 소유자 이동 지연 허용치

            switch (kind)
            {
                case InteractKind.Safe:
                    if (!p.IsThief || p.State != PState.Free || Phase != OnlinePhase.Heist) return;
                    if (index < 0 || index >= safeCount || IsRobbed(index)) return;
                    if (Flat(p.transform.position, safePositions[index]) > 2.1f + slack) return;
                    p.Money.Value += safeMoney[index];
                    RobbedMask.Value |= 1 << index;
                    safeRobbedAt[index] = Now;
                    Log($"{p.DisplayName}: 금고에서 {safeMoney[index]}원 획득!");
                    break;

                case InteractKind.Hide:
                    if (index < 0 || index >= hideSpots.Length) return;
                    if (Flat(p.transform.position, hideSpots[index].transform.position) > 2.2f + slack) return;
                    var occupant = OnlinePlayer.All.FirstOrDefault(o => o.State == PState.Hidden && o.HideIndex.Value == index);
                    if (p.IsCop)
                    {
                        // 경찰의 은신처 수색
                        if (p.State != PState.Free) return;
                        if (occupant != null)
                        {
                            LeaveHide(occupant);
                            Log($"{p.DisplayName}: 은신처에서 {occupant.DisplayName}을(를) 찾아냈다!");
                        }
                        else
                        {
                            NotifyRpc("아무도 없다...", RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
                        }
                    }
                    else if (p.State == PState.Hidden && p.HideIndex.Value == index)
                    {
                        LeaveHide(p);
                    }
                    else if (p.State == PState.Free && occupant == null)
                    {
                        p.StateValue.Value = (int)PState.Hidden;
                        p.HideIndex.Value = index;
                        p.hideEnteredAt = Now;
                    }
                    break;

                case InteractKind.Prison:
                    if (!p.IsThief || p.State != PState.Free) return;
                    if (Flat(p.transform.position, prison.transform.position) > prison.range + slack) return;
                    RescueAll(p);
                    break;

                case InteractKind.Exit:
                    if (!p.IsThief || p.State != PState.Free || Phase != OnlinePhase.LastTime) return;
                    if (index < 0 || index >= exitPositions.Count) return;
                    if (Flat(p.transform.position, exitPositions[index]) > 2.4f + slack) return;
                    if (!PasswordComplete)
                    {
                        NotifyRpc("비밀번호가 틀렸다! 단서를 더 찾아야 한다.", RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
                        return;
                    }
                    p.StateValue.Value = (int)PState.Escaped;
                    p.ServerTeleport(new Vector3(0, -50, 0));
                    End(OnlinePhase.ThievesWin, $"{p.DisplayName} 탈출 성공!");
                    break;
            }
        }

        public void ServerClub(OnlinePlayer p)
        {
            if (!IsPlaying || !p.IsThief || p.State != PState.Free || Now < p.ClubReadyAt.Value) return;
            p.ClubReadyAt.Value = Now + clubCooldown;
            bool hit = false;
            foreach (var cop in OnlinePlayer.All.Where(o => o.IsCop && o.State == PState.Free))
            {
                Vector3 to = cop.transform.position - p.transform.position;
                to.y = 0f;
                if (to.magnitude > clubRange || Vector3.Angle(p.transform.forward, to) > 80f) continue;
                cop.StunUntil.Value = Now + clubStun;
                Log($"{p.DisplayName} → {cop.DisplayName} 몽둥이 명중! (기절)");
                hit = true;
            }
            if (!hit) NotifyRpc("몽둥이 헛스윙!", RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        public void ServerCatch(OnlinePlayer cop)
        {
            if (!IsPlaying || !cop.IsCop || cop.State != PState.Free || cop.IsStunned) return;
            OnlinePlayer target = null;
            float best = catchRange;
            foreach (var t in OnlinePlayer.All)
            {
                if (!t.IsThief || t.State != PState.Free) continue;
                float d = Flat(t.transform.position, cop.transform.position);
                if (d > best) continue;
                best = d;
                target = t;
            }
            if (target == null) return;

            if (Phase == OnlinePhase.LastTime)
            {
                target.StateValue.Value = (int)PState.Eliminated;
                target.ServerTeleport(new Vector3(0, -50, 0));
                Log($"{cop.DisplayName} → {target.DisplayName} 제압! (탈락)");
            }
            else if (cop.HasCuffs.Value)
            {
                int lost = target.Money.Value;
                int jailedCount = OnlinePlayer.All.Count(o => o.State == PState.Jailed);
                target.Money.Value = 0;
                target.StateValue.Value = (int)PState.Jailed;
                target.jailedBy = cop.OwnerClientId;
                cop.HasCuffs.Value = false;
                target.ServerTeleport(prison.CellPoint(jailedCount % 4));
                Log($"{cop.DisplayName} → {target.DisplayName} 체포! 감옥 이송 (게이지 -{lost})");
            }
            else if (Now >= cop.batonReadyAt)
            {
                cop.batonReadyAt = Now + 1.2;
                target.SlowUntil.Value = Now + batonSlow;
                Log($"{cop.DisplayName}: 곤봉 타격! {target.DisplayName} 둔화");
            }
        }

        public void ServerDrink(OnlinePlayer p)
        {
            if (!IsPlaying || !p.IsThief || p.State != PState.Free || p.Drinks.Value <= 0) return;
            p.Drinks.Value--;
            p.BuffUntil.Value = System.Math.Max(p.BuffUntil.Value, Now + drinkDuration);
        }

        void RescueAll(OnlinePlayer rescuer)
        {
            int n = 0;
            foreach (var t in OnlinePlayer.All.Where(o => o.State == PState.Jailed).ToList())
            {
                var jailer = OnlinePlayer.All.FirstOrDefault(o => o.OwnerClientId == t.jailedBy && o.IsCop);
                if (jailer) jailer.HasCuffs.Value = true;
                t.jailedBy = ulong.MaxValue;
                t.StateValue.Value = (int)PState.Free;
                t.BuffUntil.Value = Now + rescueBuff;
                t.ServerTeleport(prison.ReleasePoint(n));
                n++;
            }
            if (n > 0) Log($"{rescuer.DisplayName}: 동료 {n}명 구출!");
        }

        void LeaveHide(OnlinePlayer t)
        {
            t.StateValue.Value = (int)PState.Free;
            t.HideIndex.Value = -1;
        }

        void End(OnlinePhase result, string reason)
        {
            if (!IsPlaying) return;
            EndReason.Value = new FixedString128Bytes(reason);
            PhaseValue.Value = (int)result;
        }

        public void Log(string text) => LogRpc(text);

        [Rpc(SendTo.ClientsAndHost)]
        void LogRpc(string text) => AddMessage(text);

        [Rpc(SendTo.SpecifiedInParams)]
        void NotifyRpc(string text, RpcParams rpcParams) => AddMessage(text);

        // 은신처 소음 위치를 경찰에게 표시
        [Rpc(SendTo.ClientsAndHost)]
        void PingRpc(Vector3 position)
        {
            PingPosition = position;
            PingUntil = Time.time + 4f;
        }

        void AddMessage(string text)
        {
            messages.Add((text, Time.time));
            if (messages.Count > 8) messages.RemoveAt(0);
        }

        // ================= 클라이언트: 상호작용/시야 =================

        public Interaction FindInteraction(OnlinePlayer me)
        {
            var none = new Interaction();
            if (!worldBuilt || !IsPlaying) return none;
            Vector3 pos = me.transform.position;

            if (me.State == PState.Hidden)
                return new Interaction { kind = InteractKind.Hide, index = me.HideIndex.Value, prompt = "나오기", holdTime = 0f };
            if (me.State != PState.Free) return none;

            Interaction best = none;
            float bestDist = float.MaxValue;
            void Consider(InteractKind kind, int index, Vector3 at, float range, string prompt, float hold)
            {
                float d = Flat(pos, at);
                if (d > range || d >= bestDist) return;
                bestDist = d;
                best = new Interaction { kind = kind, index = index, prompt = prompt, holdTime = hold };
            }

            for (int i = 0; i < hideSpots.Length; i++)
            {
                bool occupied = OnlinePlayer.All.Any(o => o.State == PState.Hidden && o.HideIndex.Value == i);
                if (me.IsCop) Consider(InteractKind.Hide, i, hideSpots[i].transform.position, 2.2f, "은신처 수색", 1f);
                else if (!occupied) Consider(InteractKind.Hide, i, hideSpots[i].transform.position, 2.2f, "숨기", 0f);
            }

            if (me.IsThief)
            {
                if (Phase == OnlinePhase.Heist)
                    for (int i = 0; i < safePositions.Count; i++)
                        if (!IsRobbed(i)) Consider(InteractKind.Safe, i, safePositions[i], 2.1f, $"금고 털기 (+{safeMoney[i]})", 2.5f);

                if (OnlinePlayer.All.Any(o => o.State == PState.Jailed))
                    Consider(InteractKind.Prison, 0, prison.transform.position, prison.range, "동료 구출", 2f);

                if (Phase == OnlinePhase.LastTime)
                    for (int i = 0; i < exitPositions.Count; i++)
                        Consider(InteractKind.Exit, i, exitPositions[i], 2.4f,
                            PasswordComplete ? $"비밀번호 [{Password}] 입력하고 탈출" : $"비밀번호 입력 ({PasswordDisplay}) - 단서 부족", 1.5f);
            }
            return best;
        }

        // 로컬 플레이어 기준으로 target 이 화면에 보이는지
        public bool IsVisibleToLocal(OnlinePlayer target)
        {
            var me = OnlinePlayer.Local;
            if (target.State == PState.Eliminated || target.State == PState.Escaped || target.State == PState.Waiting) return false;
            if (target.State == PState.Hidden) return false;
            if (me == null || !IsPlaying) return true;
            if (me == target || me.Role == target.Role) return true;
            if (me.State == PState.Eliminated || me.State == PState.Escaped) return true;
            if (me.State == PState.Waiting) return false;
            if (me.IsCop && Phase == OnlinePhase.LastTime) return true; // 라스트 타임 경찰 시야 해제

            Vector3 to = target.transform.position - me.transform.position;
            to.y = 0f;
            float d = to.magnitude;
            if (d <= nearRange) return true;
            if (d > viewRange || Vector3.Angle(me.transform.forward, to) > viewAngle * 0.5f) return false;
            Vector3 eye = me.transform.position + Vector3.up * 1.5f;
            if (Physics.Linecast(eye, target.transform.position + Vector3.up * 1.2f, out var hit, ~0, QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<OnlinePlayer>() == target;
            return true;
        }

        void LateUpdate()
        {
            foreach (var p in OnlinePlayer.All) p.SetRendered(IsVisibleToLocal(p));
        }

        public static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }

    static class VectorExtensions
    {
        public static Vector3 With(this Vector3 v, float? x = null, float? y = null, float? z = null) =>
            new Vector3(x ?? v.x, y ?? v.y, z ?? v.z);
    }
}
