using UnityEngine;
using UnityEngine.AI;

namespace Chase
{
    // AI 경찰: 순찰 → 시야(부채꼴 + 가림 판정)에 도둑이 들어오면 추격 → 놓치면 마지막 위치 수색
    [RequireComponent(typeof(NavMeshAgent))]
    public class Cop : MonoBehaviour
    {
        public string displayName = "경찰";

        [Header("이동")]
        public float patrolSpeed = 3.8f;
        public float chaseSpeed = 5.6f;

        [Header("시야")]
        public float viewAngle = 100f;
        public float viewRange = 11f;
        public float senseRange = 2.2f;

        [Header("체포")]
        public float catchRange = 1.25f;
        public float batonSlow = 2f;
        public bool hasCuffs = true;

        [Header("라스트 타임 버프")]
        public float lastTimeSpeedMultiplier = 1.3f;
        public float lastTimeViewRange = 30f;

        public GameObject stunFx;

        NavMeshAgent agent;
        Renderer[] renderers;
        float stunnedUntil;
        float batonReadyAt;
        Vector3 searchPos;
        float searchUntil;
        bool patrolling;

        public bool Deployed => gameObject.activeInHierarchy;
        public bool IsStunned => Time.time < stunnedUntil;
        public bool IsChasing { get; private set; }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        public void Deploy(Vector3 position)
        {
            transform.position = position;
            gameObject.SetActive(true);
            agent.Warp(position);
        }

        public void Stun(float duration)
        {
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + duration);
        }

        public void Alert(Vector3 position)
        {
            searchPos = position;
            searchUntil = Time.time + 8f;
        }

        public void SetRendered(bool visible)
        {
            foreach (var r in renderers) r.enabled = visible;
        }

        void Update()
        {
            if (stunFx) stunFx.SetActive(IsStunned);

            var gm = GameManager.I;
            if (gm == null || !gm.IsPlaying || IsStunned)
            {
                if (agent.isOnNavMesh) agent.isStopped = true;
                IsChasing = false;
                return;
            }
            agent.isStopped = false;

            bool lastTime = gm.phase == GamePhase.LastTime;
            float speedMul = lastTime ? lastTimeSpeedMultiplier : 1f;

            Thief seen = FindVisibleThief(lastTime);
            IsChasing = seen != null;
            if (seen != null)
            {
                searchPos = seen.transform.position;
                searchUntil = Time.time + 5f;
                patrolling = false;
                agent.speed = chaseSpeed * speedMul;
                agent.SetDestination(seen.transform.position);

                if (FlatDistance(seen.transform.position) <= catchRange)
                {
                    if (hasCuffs || lastTime)
                    {
                        gm.Capture(seen, this);
                    }
                    else if (Time.time >= batonReadyAt)
                    {
                        // 수갑이 없으면 곤봉으로 이동 속도를 늦춘다
                        batonReadyAt = Time.time + 1.2f;
                        seen.Slow(batonSlow);
                        gm.Log($"{displayName}: 곤봉 타격! {seen.displayName} 둔화");
                    }
                }
                return;
            }

            if (Time.time < searchUntil)
            {
                agent.speed = chaseSpeed * speedMul;
                agent.SetDestination(searchPos);
                patrolling = false;
                if (!agent.pathPending && agent.remainingDistance < 1.2f)
                {
                    SearchHideSpots();
                    searchUntil = 0f;
                }
                return;
            }

            agent.speed = patrolSpeed * speedMul;
            if (!patrolling || (!agent.pathPending && agent.remainingDistance < 1.2f))
            {
                agent.SetDestination(gm.RandomPatrolPoint());
                patrolling = true;
            }
        }

        // 수색 지점에 도착하면 근처 은신처를 뒤진다
        void SearchHideSpots()
        {
            foreach (var i in Interactable.All)
            {
                if (i is HideSpot spot && spot.occupant != null && spot.FlatDistance(transform.position) < 3.5f)
                {
                    spot.Leave(true);
                    return;
                }
            }
        }

        Thief FindVisibleThief(bool lastTime)
        {
            var gm = GameManager.I;
            Thief best = null;
            float bestDist = float.MaxValue;
            float range = lastTime ? lastTimeViewRange : viewRange;
            Vector3 eye = transform.position + Vector3.up * 1.4f;

            foreach (var t in gm.thieves)
            {
                if (!t.IsVisibleToCops) continue;
                Vector3 to = t.transform.position - transform.position;
                to.y = 0f;
                float d = to.magnitude;
                if (d >= bestDist) continue;

                bool visible = d <= senseRange ||
                               (d <= range &&
                                (lastTime || Vector3.Angle(transform.forward, to) <= viewAngle * 0.5f) &&
                                HasLineOfSight(eye, t));
                if (!visible) continue;
                best = t;
                bestDist = d;
            }
            return best;
        }

        static bool HasLineOfSight(Vector3 eye, Thief t)
        {
            Vector3 target = t.transform.position + Vector3.up * 1f;
            if (Physics.Linecast(eye, target, out var hit, ~0, QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<Thief>() == t;
            return true;
        }

        float FlatDistance(Vector3 p)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            return d.magnitude;
        }
    }
}
