using UnityEngine;
using UnityEngine.InputSystem;

namespace Chase
{
    // 쿼터뷰 카메라: 마우스 우클릭 드래그로 회전, 휠로 줌
    [RequireComponent(typeof(Camera))]
    public class QuarterViewCamera : MonoBehaviour
    {
        public Transform target;
        public float yaw = 45f;
        public float pitch = 55f;
        public float distance = 30f;
        public float minDistance = 16f;
        public float maxDistance = 44f;
        public float rotateSpeed = 0.25f;
        public float followSharpness = 10f;

        public Camera Cam { get; private set; }
        Vector3 focus;

        void Awake()
        {
            Cam = GetComponent<Camera>();
            if (target) focus = target.position;
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed) yaw += mouse.delta.ReadValue().x * rotateSpeed;
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * 1.5f, minDistance, maxDistance);
            }

            Transform follow = ResolveTarget();
            if (follow == null) return;

            focus = Vector3.Lerp(focus, follow.position, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }

        // 플레이어가 탈락/탈출하면 남은 동료를 관전
        Transform ResolveTarget()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return target;
            var s = gm.player.state;
            if (s != ThiefState.Eliminated && s != ThiefState.Escaped) return target;
            foreach (var t in gm.thieves)
                if (t.IsActive) return t.transform;
            return target;
        }
    }
}
