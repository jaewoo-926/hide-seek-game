using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Chase
{
    public enum GamePhase { Heist, LastTime, ThievesWin, CopsWin }

    // 게임 규칙 총괄: 공용 게이지, 경찰 진입, 체포/구출, 라스트 타임, 승패
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        [Header("규칙")]
        public int targetMoney = 1000;
        public float copEntryDelay = 20f;
        public float lastTimeDuration = 90f;
        public int safeCount = 10;
        public int drinkCount = 5;
        public float rescueSpeedBuff = 4f;

        [Header("참가자")]
        public Thief player;
        public List<Thief> thieves = new List<Thief>();
        public List<Cop> cops = new List<Cop>();

        [Header("맵 데이터 (씬 빌더가 채움)")]
        public WorldSpawner spawner;
        public Vector3 thiefSpawn;
        public List<Vector3> copSpawnPoints = new List<Vector3>();
        public List<Vector3> prisonSpots = new List<Vector3>();
        public List<Vector3> itemSpots = new List<Vector3>();

        [Header("런타임 상태")]
        public GamePhase phase = GamePhase.Heist;
        public float elapsed;
        public float lastTimeRemaining;
        public bool copsDeployed;
        public string password;
        public bool[] knownDigits = new bool[4];
        public bool prisonKnown;
        public Prison prison;
        public List<ExitPoint> exits = new List<ExitPoint>();
        public string endReason;

        readonly List<Safe> safes = new List<Safe>();
        readonly List<Vector3> exitPositions = new List<Vector3>();
        public readonly List<(string text, float time)> messages = new List<(string, float)>();

        public bool IsPlaying => phase == GamePhase.Heist || phase == GamePhase.LastTime;
        public int Gauge => thieves.Where(t => t.state != ThiefState.Eliminated).Sum(t => t.money);
        public float GaugeRatio => Mathf.Clamp01((float)Gauge / targetMoney);
        public bool PasswordComplete => knownDigits.All(k => k);
        public string PasswordDisplay =>
            string.Join(" ", Enumerable.Range(0, 4).Select(i => knownDigits[i] ? password[i].ToString() : "_"));
        public float CopEntryRemaining => Mathf.Max(0f, copEntryDelay - elapsed);

        void Awake() => I = this;

        void Start()
        {
            password = string.Concat(Enumerable.Range(0, 4).Select(_ => Random.Range(0, 10)));

            // 감옥: 도둑 시작 지점에서 먼 광장 중 랜덤
            var prisonCandidates = prisonSpots.Where(p => Flat(p, thiefSpawn) > 20f).ToList();
            if (prisonCandidates.Count == 0) prisonCandidates = prisonSpots;
            Vector3 prisonPos = prisonCandidates[Random.Range(0, prisonCandidates.Count)];
            prison = spawner.SpawnPrison(prisonPos);

            var pool = itemSpots.OrderBy(_ => Random.value).ToList();
            var used = new List<Vector3> { prisonPos, thiefSpawn };

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

            // 탈출구 위치는 미리 정해두고 라스트 타임에 생성
            Vector3 e1 = Take(8f, s => Flat(s, thiefSpawn) > 25f);
            Vector3 e2 = Take(8f, s => Flat(s, e1) > 35f);
            exitPositions.Add(e1);
            exitPositions.Add(e2);

            for (int i = 0; i < safeCount; i++)
                safes.Add(spawner.SpawnSafe(Take(7f, s => Flat(s, thiefSpawn) > 6f), Random.Range(10, 19) * 10));
            for (int i = 0; i < 4; i++)
                spawner.SpawnClue(Take(6f), i);
            for (int i = 0; i < drinkCount; i++)
                spawner.SpawnDrink(Take(5f));

            foreach (var c in cops) c.gameObject.SetActive(false);

            Log($"도둑 선진입! 경찰은 {copEntryDelay:0}초 뒤에 들어온다.");
            Log($"금고를 털어 팀 게이지 {targetMoney}을 채우자.");
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (!IsPlaying)
            {
                if (kb != null && kb.rKey.wasPressedThisFrame)
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            elapsed += Time.deltaTime;

            if (!copsDeployed && elapsed >= copEntryDelay) DeployCops();

            if (!prisonKnown && prison != null)
            {
                foreach (var t in thieves)
                {
                    if (t.state == ThiefState.Free && Flat(t.transform.position, prison.transform.position) < 9f)
                    {
                        prisonKnown = true;
                        Log($"{t.displayName}: 감옥 위치 발견! (팀 공유)");
                        break;
                    }
                }
            }

            if (phase == GamePhase.Heist && Gauge >= targetMoney) StartLastTime();

            if (phase == GamePhase.LastTime)
            {
                lastTimeRemaining -= Time.deltaTime;
                if (lastTimeRemaining <= 0f)
                {
                    End(GamePhase.CopsWin, "라스트 타임 종료 - 아무도 탈출하지 못했다");
                    return;
                }
            }

            if (thieves.All(t => !t.IsActive))
                End(GamePhase.CopsWin, "모든 도둑이 제압되었다");
        }

        void DeployCops()
        {
            copsDeployed = true;
            // 경찰은 정해진 N개의 스폰 위치 중 하나를 골라 진입
            var points = copSpawnPoints.OrderBy(_ => Random.value).ToList();
            for (int i = 0; i < cops.Count; i++)
                cops[i].Deploy(points[i % points.Count]);
            Log("경찰 진입! 시야 밖을 조심하자.");
        }

        void StartLastTime()
        {
            phase = GamePhase.LastTime;
            lastTimeRemaining = lastTimeDuration;
            foreach (var p in exitPositions) exits.Add(spawner.SpawnExit(p));
            Log("게이지 100%! 라스트 타임 시작 - 탈출구 2개가 열렸다!");
            Log("경찰 버프 발동: 이속 증가 + 시야 해제. 이제 잡히면 즉시 탈락!");
        }

        public void Capture(Thief t, Cop cop)
        {
            if (!t.IsActive) return;

            if (phase == GamePhase.LastTime)
            {
                t.state = ThiefState.Eliminated;
                t.SetVisible(false);
                t.TeleportTo(new Vector3(0f, -100f, 0f), false);
                Log($"{cop.displayName} → {t.displayName} 제압! (탈락)");
                return;
            }

            int lost = t.money;
            t.money = 0;
            t.state = ThiefState.Jailed;
            t.jailedBy = cop;
            cop.hasCuffs = false;
            int jailed = thieves.Count(x => x.state == ThiefState.Jailed) - 1;
            t.TeleportTo(prison.CellPoint(jailed), false);
            Log($"{cop.displayName} → {t.displayName} 체포! 감옥 이송 (게이지 -{lost})");
        }

        public void RescueAll(Thief rescuer)
        {
            int n = 0;
            foreach (var t in thieves)
            {
                if (t.state != ThiefState.Jailed) continue;
                t.state = ThiefState.Free;
                if (t.jailedBy) t.jailedBy.hasCuffs = true;
                t.jailedBy = null;
                t.TeleportTo(prison.ReleasePoint(n), true);
                t.AddSpeedBuff(rescueSpeedBuff);
                n++;
            }
            if (n > 0) Log($"{rescuer.displayName}: 동료 {n}명 구출! 탈출자 이속 버프");
        }

        public void RevealDigit(int index, Thief finder)
        {
            if (knownDigits[index]) return;
            knownDigits[index] = true;
            Log($"{finder.displayName}: 비밀번호 단서 발견! [{PasswordDisplay}]");
        }

        public void ThiefEscaped(Thief t)
        {
            t.state = ThiefState.Escaped;
            t.SetVisible(false);
            End(GamePhase.ThievesWin, $"{t.displayName} 탈출 성공!");
        }

        public void Alert(Vector3 position)
        {
            foreach (var c in cops)
                if (c.Deployed) c.Alert(position);
        }

        public Vector3 RandomPatrolPoint()
        {
            if (Random.value < 0.5f && safes.Count > 0)
                return safes[Random.Range(0, safes.Count)].transform.position;
            return itemSpots[Random.Range(0, itemSpots.Count)];
        }

        public void Log(string text)
        {
            messages.Add((text, Time.time));
            if (messages.Count > 8) messages.RemoveAt(0);
        }

        void End(GamePhase result, string reason)
        {
            if (!IsPlaying) return;
            phase = result;
            endReason = reason;
            foreach (var c in cops) c.SetRendered(true);
        }

        public static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
