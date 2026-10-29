using UnityEngine;
using UnityEngine.AI;

namespace Chase
{
    public enum ThiefState { Free, Hidden, Jailed, Eliminated, Escaped }

    // 도둑 공통 데이터 (플레이어 / 봇 공용)
    public class Thief : MonoBehaviour
    {
        public string displayName = "도둑";
        public bool isPlayer;
        public float baseSpeed = 5f;
        public int money;
        public int energyDrinks;
        public ThiefState state = ThiefState.Free;
        [HideInInspector] public Cop jailedBy;
        [HideInInspector] public HideSpot hideSpot;

        [Header("에너지 드링크")]
        public float drinkDuration = 4f;
        public float speedBuffMultiplier = 1.6f;

        [Header("몽둥이")]
        public float clubCooldown = 6f;
        public float clubRange = 2.3f;
        public float clubStun = 2.5f;
        [HideInInspector] public float clubReadyAt;

        [Header("곤봉 피격")]
        public float slowMultiplier = 0.5f;

        float speedBuffUntil;
        float slowUntil;
        Renderer[] renderers;
        CharacterController cc;
        NavMeshAgent agent;

        public bool IsActive => state == ThiefState.Free || state == ThiefState.Hidden;
        public bool IsVisibleToCops => state == ThiefState.Free;
        public bool IsBuffed => Time.time < speedBuffUntil;
        public bool IsSlowed => Time.time < slowUntil;
        public float BuffRemaining => Mathf.Max(0f, speedBuffUntil - Time.time);
        public float ClubCooldownRemaining => Mathf.Max(0f, clubReadyAt - Time.time);

        public float CurrentSpeed
        {
            get
            {
                float s = baseSpeed;
                if (IsBuffed) s *= speedBuffMultiplier;
                if (IsSlowed) s *= slowMultiplier;
                return s;
            }
        }

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            cc = GetComponent<CharacterController>();
            agent = GetComponent<NavMeshAgent>();
        }

        public void AddSpeedBuff(float duration) => speedBuffUntil = Mathf.Max(speedBuffUntil, Time.time + duration);
        public void Slow(float duration) => slowUntil = Mathf.Max(slowUntil, Time.time + duration);

        public bool UseEnergyDrink()
        {
            if (energyDrinks <= 0 || state != ThiefState.Free) return false;
            energyDrinks--;
            AddSpeedBuff(drinkDuration);
            GameManager.I.Log($"{displayName}: 에너지 드링크 사용! (이속 증가)");
            return true;
        }

        // 근처 경찰을 타격해 짧게 기절시킨다. 쿨타임은 헛스윙에도 소모된다.
        public bool TryClub(bool requireFacing)
        {
            if (state != ThiefState.Free || Time.time < clubReadyAt) return false;
            clubReadyAt = Time.time + clubCooldown;

            var gm = GameManager.I;
            bool hit = false;
            foreach (var cop in gm.cops)
            {
                if (!cop.Deployed || cop.IsStunned) continue;
                Vector3 to = cop.transform.position - transform.position;
                to.y = 0f;
                if (to.magnitude > clubRange) continue;
                if (requireFacing && Vector3.Angle(transform.forward, to) > 75f) continue;
                cop.Stun(clubStun);
                gm.Log($"{displayName} → {cop.displayName} 몽둥이 명중! (기절)");
                hit = true;
            }
            if (!hit && isPlayer) gm.Log("몽둥이 헛스윙!");
            return hit;
        }

        public void SetVisible(bool visible)
        {
            foreach (var r in renderers) r.enabled = visible;
        }

        public void TeleportTo(Vector3 position, bool navigable)
        {
            if (agent != null)
            {
                agent.enabled = false;
                if (navigable && NavMesh.SamplePosition(position, out var hit, 3f, NavMesh.AllAreas))
                    position = hit.position;
                transform.position = position;
                if (navigable)
                {
                    agent.enabled = true;
                    agent.Warp(position);
                }
            }
            else if (cc != null)
            {
                cc.enabled = false;
                transform.position = position;
                cc.enabled = true;
            }
            else
            {
                transform.position = position;
            }
        }
    }
}
