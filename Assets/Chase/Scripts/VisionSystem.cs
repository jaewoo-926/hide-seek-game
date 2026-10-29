using UnityEngine;

namespace Chase
{
    // 플레이어 시야: 정면 부채꼴 + 벽 가림 판정으로 경찰 표시 여부를 결정.
    // 시야 밖 지역은 어두운 조명으로 실루엣만 보이고, 정면은 스포트라이트로 밝힌다.
    public class VisionSystem : MonoBehaviour
    {
        public float viewAngle = 110f;
        public float viewRange = 20f;
        public float nearRange = 3.5f;

        Thief thief;

        void Awake() => thief = GetComponent<Thief>();

        void LateUpdate()
        {
            var gm = GameManager.I;
            if (gm == null) return;

            bool spectating = !gm.IsPlaying || thief.state == ThiefState.Eliminated || thief.state == ThiefState.Escaped;
            Vector3 eye = transform.position + Vector3.up * 1.5f;

            foreach (var cop in gm.cops)
            {
                if (!cop.Deployed) continue;
                cop.SetRendered(spectating || CanSee(eye, cop));
            }
        }

        bool CanSee(Vector3 eye, Cop cop)
        {
            Vector3 to = cop.transform.position - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            if (d <= nearRange) return true;
            if (d > viewRange || Vector3.Angle(transform.forward, to) > viewAngle * 0.5f) return false;

            Vector3 target = cop.transform.position + Vector3.up * 1.2f;
            if (Physics.Linecast(eye, target, out var hit, ~0, QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<Cop>() == cop;
            return true;
        }
    }
}
