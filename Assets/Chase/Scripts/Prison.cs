using UnityEngine;

namespace Chase
{
    // 감옥: 갇힌 동료 근처에서 홀드하면 전원 구출
    public class Prison : Interactable
    {
        public override float HoldTime => 2f;

        public Vector3 CellPoint(int index)
        {
            var offset = new Vector3(index % 2 == 0 ? -0.7f : 0.7f, 0.1f, index / 2 == 0 ? -0.7f : 0.7f);
            return transform.TransformPoint(offset);
        }

        public Vector3 ReleasePoint(int index) =>
            transform.TransformPoint(new Vector3((index - 1) * 1.1f, 0.1f, 3.1f));

        public override bool CanInteract(Thief t)
        {
            if (t.state != ThiefState.Free) return false;
            foreach (var other in GameManager.I.thieves)
                if (other.state == ThiefState.Jailed) return true;
            return false;
        }

        public override string Prompt(Thief t) => "동료 구출";

        public override void Complete(Thief t) => GameManager.I.RescueAll(t);
    }
}
