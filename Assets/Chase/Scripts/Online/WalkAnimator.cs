using UnityEngine;

namespace Chase.Online
{
    // 제자리 걷기 애니메이션을 실제 이동 속도에 맞춰 재생하고,
    // 모델 바깥의 body(Lean) 오브젝트를 기울이고 출렁여서 움직임에 반동을 준다.
    // 위치 변화로 속도를 재기 때문에 내 캐릭터와 다른 사람 캐릭터 모두 같은 방식으로 동작한다.
    // body 는 애니메이터 바깥이라 어떤 애니메이션을 추가해도 서로 덮어쓰지 않는다.
    public class WalkAnimator : MonoBehaviour
    {
        [Header("걷기 애니메이션")]
        public Animator animator;
        public string walkState = "walk";
        public float clipSpeed = 4.5f;      // 애니메이션 속도 1배일 때의 이동 속도 (도둑 기본 이속)
        public float moveThreshold = 0.3f;  // 이 속도(m/s) 아래는 멈춘 것으로 본다
        public float smoothing = 10f;

        [Header("몸 반동 (body 에만 적용, 0이면 끔)")]
        public Transform body;
        public float leanPerSpeed = 1.6f;   // 이동 방향으로 기울어지는 각도 (도 / m/s)
        public float maxLean = 9f;
        public float turnLean = 0.02f;      // 방향을 틀 때 옆으로 기우는 정도 (도 / 도/초)
        public float maxTurnLean = 8f;
        public float bobHeight = 0.05f;     // 걸음마다 위로 튀는 높이 (m)
        public float bodySmoothing = 12f;

        Vector3 lastPos;
        float lastYaw;
        Vector3 velocity;
        float yawRate;
        bool walking = true;
        Vector3 bodyBasePos;
        Quaternion bodyBaseRot;
        Quaternion leanRot = Quaternion.identity;
        float bob;

        float Speed => velocity.magnitude;

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (body)
            {
                bodyBasePos = body.localPosition;
                bodyBaseRot = body.localRotation;
            }
        }

        void OnEnable()
        {
            lastPos = transform.position;
            lastYaw = transform.eulerAngles.y;
            velocity = Vector3.zero;
            yawRate = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = transform.position - lastPos;
            lastPos = transform.position;
            delta.y = 0f;
            Vector3 rawVel = delta / dt;
            if (rawVel.sqrMagnitude > 400f) rawVel = Vector3.zero; // 순간이동(감옥 이송, 층 이동)은 무시
            float k = 1f - Mathf.Exp(-smoothing * dt);
            velocity = Vector3.Lerp(velocity, rawVel, k);

            float yaw = transform.eulerAngles.y;
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(lastYaw, yaw) / dt, k);
            lastYaw = yaw;

            UpdateAnimator();
            UpdateBody(dt);
        }

        void UpdateAnimator()
        {
            if (!animator) return;
            if (Speed > moveThreshold)
            {
                walking = true;
                animator.speed = Mathf.Clamp(Speed / clipSpeed, 0.5f, 2f);
            }
            else if (walking)
            {
                walking = false;
                animator.Play(walkState, 0, 0f);
                animator.speed = 0f;
            }
        }

        void UpdateBody(float dt)
        {
            if (!body) return;

            // 이동 방향으로 기울기 (캐릭터가 바라보는 방향 기준이라 뒷걸음질이면 뒤로 기운다)
            Vector3 local = Quaternion.Inverse(transform.rotation) * velocity;
            float pitch = Mathf.Clamp(local.z * leanPerSpeed, -maxLean, maxLean);
            float roll = Mathf.Clamp(-local.x * leanPerSpeed, -maxLean, maxLean);
            // 방향을 틀면 안쪽으로 기울기
            roll += Mathf.Clamp(-yawRate * turnLean, -maxTurnLean, maxTurnLean);
            var targetRot = Quaternion.Euler(pitch, 0f, roll);

            // 걸음에 맞춘 출렁임: 걷기 한 사이클에 두 걸음
            float targetBob = 0f;
            if (walking && animator)
            {
                float t = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                float weight = Mathf.Clamp01(Speed / clipSpeed);
                targetBob = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * bobHeight * weight;
            }

            float bk = 1f - Mathf.Exp(-bodySmoothing * dt);
            leanRot = Quaternion.Slerp(leanRot, targetRot, bk);
            bob = Mathf.Lerp(bob, targetBob, bk);

            body.localRotation = bodyBaseRot * leanRot;
            body.localPosition = bodyBasePos + Vector3.up * bob;
        }
    }
}
