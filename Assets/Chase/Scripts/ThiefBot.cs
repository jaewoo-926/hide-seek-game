using UnityEngine;
using UnityEngine.AI;

namespace Chase
{
    // 동료 도둑 AI 봇 (인원이 부족할 때 AI 봇 도입 - 기획안 5장)
    // 우선순위: 도주 > 탈출(라스트 타임) > 동료 구출 > 금고 털기 > 아이템 줍기 > 배회
    [RequireComponent(typeof(NavMeshAgent), typeof(Thief))]
    public class ThiefBot : MonoBehaviour
    {
        public float dangerRange = 9f;
        public float thinkInterval = 0.5f;

        NavMeshAgent agent;
        Thief thief;
        Interactable holding;
        float holdProgress;
        float nextThink;
        float fleeUntil;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            thief = GetComponent<Thief>();
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            if (!gm.IsPlaying || thief.state != ThiefState.Free)
            {
                CancelHold();
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
                return;
            }
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = false;
            agent.speed = thief.CurrentSpeed;

            Cop threat = NearestThreat();
            if (threat != null)
            {
                Flee(threat);
                return;
            }
            if (Time.time < fleeUntil) return;

            if (holding != null)
            {
                UpdateHold();
                return;
            }

            if (Time.time < nextThink) return;
            nextThink = Time.time + thinkInterval;
            Think(gm);
        }

        void Flee(Cop threat)
        {
            CancelHold();
            Vector3 away = transform.position - threat.transform.position;
            away.y = 0f;
            float d = away.magnitude;

            if (d < 1.9f) thief.TryClub(false);
            if (d < 5f && !thief.IsBuffed) thief.UseEnergyDrink();

            Vector3 candidate = transform.position + away.normalized * 8f + Random.insideUnitSphere * 3f;
            if (NavMesh.SamplePosition(candidate, out var hit, 4f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
            fleeUntil = Time.time + 1.2f;
        }

        void UpdateHold()
        {
            agent.ResetPath();
            if (!holding.InRange(thief) || !holding.CanInteract(thief))
            {
                CancelHold();
                return;
            }
            holdProgress += Time.deltaTime;
            if (holdProgress >= holding.HoldTime)
            {
                var done = holding;
                holding = null;
                holdProgress = 0f;
                done.Complete(thief);
            }
        }

        void Think(GameManager gm)
        {
            Interactable goal = null;
            Vector3? dest = null;

            if (gm.phase == GamePhase.LastTime)
            {
                if (gm.PasswordComplete) goal = Nearest<ExitPoint>();
                else dest = NearestPickup<ClueNote>(float.MaxValue);
            }

            if (goal == null && dest == null && gm.prisonKnown && gm.prison.CanInteract(thief))
                goal = gm.prison;

            if (goal == null && dest == null)
                dest = NearestPickup<Pickup>(8f);

            if (goal == null && dest == null && gm.phase == GamePhase.Heist)
                goal = Nearest<Safe>();

            if (goal != null)
            {
                if (goal.FlatDistance(transform.position) <= goal.range - 0.3f)
                {
                    holding = goal;
                    holdProgress = 0f;
                    holding.OnHoldStart(thief);
                    agent.ResetPath();
                }
                else
                {
                    agent.SetDestination(goal.transform.position);
                }
                return;
            }

            if (dest.HasValue)
            {
                agent.SetDestination(dest.Value);
                return;
            }

            // 배회: 아직 못 가본 곳(감옥 위치 탐색 등)
            if (!agent.hasPath || agent.remainingDistance < 1.5f)
                agent.SetDestination(gm.itemSpots[Random.Range(0, gm.itemSpots.Count)]);
        }

        T Nearest<T>() where T : Interactable
        {
            T best = null;
            float bestDist = float.MaxValue;
            foreach (var i in Interactable.All)
            {
                if (!(i is T typed) || !typed.CanInteract(thief)) continue;
                float d = typed.FlatDistance(transform.position);
                if (d >= bestDist) continue;
                best = typed;
                bestDist = d;
            }
            return best;
        }

        Vector3? NearestPickup<T>(float maxDist) where T : Pickup
        {
            Vector3? best = null;
            float bestDist = maxDist;
            foreach (var p in Pickup.All)
            {
                if (!(p is T)) continue;
                float d = GameManager.Flat(p.transform.position, transform.position);
                if (d >= bestDist) continue;
                best = p.transform.position;
                bestDist = d;
            }
            return best;
        }

        Cop NearestThreat()
        {
            Cop best = null;
            float bestDist = dangerRange;
            Vector3 eye = transform.position + Vector3.up * 1.4f;
            foreach (var cop in GameManager.I.cops)
            {
                if (!cop.Deployed || cop.IsStunned) continue;
                float d = GameManager.Flat(cop.transform.position, transform.position);
                if (d >= bestDist) continue;
                Vector3 target = cop.transform.position + Vector3.up * 1.2f;
                if (Physics.Linecast(eye, target, out var hit, ~0, QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent<Cop>() != cop) continue;
                best = cop;
                bestDist = d;
            }
            return best;
        }

        void CancelHold()
        {
            if (holding == null) return;
            holding.OnHoldCancel(thief);
            holding = null;
            holdProgress = 0f;
        }
    }
}
