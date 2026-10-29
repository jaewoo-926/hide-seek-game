using UnityEngine;
using UnityEngine.InputSystem;

namespace Chase
{
    // 플레이어 도둑 조작: WASD 이동(카메라 기준), 마우스 방향으로 시선, E 상호작용, 좌클릭 몽둥이, F 에너지 드링크
    [RequireComponent(typeof(CharacterController), typeof(Thief))]
    public class ThiefPlayer : MonoBehaviour
    {
        public QuarterViewCamera cam;
        public float turnSharpness = 18f;

        public Interactable Focus { get; private set; }
        public Interactable Holding { get; private set; }
        public float HoldRatio => Holding != null && Holding.HoldTime > 0f ? holdProgress / Holding.HoldTime : 0f;

        Thief thief;
        CharacterController cc;
        float holdProgress;
        float verticalVelocity;

        void Awake()
        {
            thief = GetComponent<Thief>();
            cc = GetComponent<CharacterController>();
        }

        void Update()
        {
            var gm = GameManager.I;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (gm == null || kb == null || mouse == null) return;

            if (!gm.IsPlaying || thief.state != ThiefState.Free) CancelHold();
            if (!gm.IsPlaying) return;

            if (thief.state == ThiefState.Hidden)
            {
                Focus = thief.hideSpot;
                if (kb.eKey.wasPressedThisFrame && thief.hideSpot) thief.hideSpot.Complete(thief);
                return;
            }
            if (thief.state != ThiefState.Free && thief.state != ThiefState.Jailed)
            {
                Focus = null;
                return;
            }

            Move(kb);
            FaceMouse(mouse);

            if (thief.state == ThiefState.Jailed)
            {
                Focus = null;
                return;
            }

            if (kb.fKey.wasPressedThisFrame) thief.UseEnergyDrink();
            if (mouse.leftButton.wasPressedThisFrame) thief.TryClub(true);

            Interact(kb);
        }

        void Move(Keyboard kb)
        {
            Vector2 input = Vector2.zero;
            if (kb.wKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed) input.x -= 1f;

            var yawRot = Quaternion.Euler(0f, cam ? cam.yaw : 0f, 0f);
            Vector3 move = yawRot * new Vector3(input.x, 0f, input.y);
            if (move.sqrMagnitude > 1f) move.Normalize();
            if (Holding != null) move = Vector3.zero; // 상호작용 중에는 이동 불가

            verticalVelocity = cc.isGrounded ? -1f : verticalVelocity - 20f * Time.deltaTime;
            cc.Move((move * thief.CurrentSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);
        }

        void FaceMouse(Mouse mouse)
        {
            if (cam == null) return;
            Ray ray = cam.Cam.ScreenPointToRay(mouse.position.ReadValue());
            var plane = new Plane(Vector3.up, transform.position);
            if (!plane.Raycast(ray, out float enter)) return;
            Vector3 dir = ray.GetPoint(enter) - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            var targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
        }

        void Interact(Keyboard kb)
        {
            if (Holding != null)
            {
                if (!kb.eKey.isPressed || !Holding.InRange(thief) || !Holding.CanInteract(thief))
                {
                    CancelHold();
                    return;
                }
                holdProgress += Time.deltaTime;
                if (holdProgress >= Holding.HoldTime)
                {
                    var done = Holding;
                    Holding = null;
                    holdProgress = 0f;
                    done.Complete(thief);
                }
                return;
            }

            Focus = Interactable.FindNearest(thief);
            if (Focus == null || !kb.eKey.wasPressedThisFrame) return;

            if (Focus.HoldTime <= 0f)
            {
                Focus.Complete(thief);
            }
            else
            {
                Holding = Focus;
                holdProgress = 0f;
                Holding.OnHoldStart(thief);
            }
        }

        void CancelHold()
        {
            if (Holding == null) return;
            Holding.OnHoldCancel(thief);
            Holding = null;
            holdProgress = 0f;
        }
    }
}
