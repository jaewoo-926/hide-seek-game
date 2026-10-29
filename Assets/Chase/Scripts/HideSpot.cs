using UnityEngine;

namespace Chase
{
    // 은폐 오브젝트: 들어가면 경찰에게 보이지 않지만, 오래 머물면 들킨다.
    public class HideSpot : Interactable
    {
        public float safeTime = 8f;
        public float maxTime = 11f;
        [HideInInspector] public Thief occupant;
        float enteredAt;

        public float HiddenFor => occupant ? Time.time - enteredAt : 0f;

        public override float HoldTime => 0f;

        public override bool CanInteract(Thief t) =>
            occupant == t || (occupant == null && t.state == ThiefState.Free);

        public override string Prompt(Thief t) => occupant == t ? "나오기" : "숨기";

        public override void Complete(Thief t)
        {
            if (occupant == t) Leave(false);
            else Enter(t);
        }

        void Enter(Thief t)
        {
            occupant = t;
            enteredAt = Time.time;
            t.state = ThiefState.Hidden;
            t.hideSpot = this;
            t.SetVisible(false);
        }

        public void Leave(bool penalty)
        {
            if (occupant == null) return;
            var t = occupant;
            occupant = null;
            t.hideSpot = null;
            if (t.state == ThiefState.Hidden) t.state = ThiefState.Free;
            t.SetVisible(true);
            if (penalty)
            {
                GameManager.I.Log($"{t.displayName}: 은신처에서 들켰다!");
                GameManager.I.Alert(transform.position);
            }
        }

        void Update()
        {
            if (occupant != null && HiddenFor > maxTime) Leave(true);
        }
    }
}
