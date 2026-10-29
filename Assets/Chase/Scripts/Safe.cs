using UnityEngine;

namespace Chase
{
    // 금고: 홀드해서 돈 획득 → 팀 게이지 상승. 일정 시간 후 다시 채워진다.
    public class Safe : Interactable
    {
        public int money = 150;
        public bool robbed;
        public float respawnDelay = 45f;
        [HideInInspector] public Thief occupant;

        Renderer door;
        Material fullMat, emptyMat;
        float respawnAt;

        public void Init(int amount, Renderer doorRenderer, Material full, Material empty)
        {
            money = amount;
            door = doorRenderer;
            fullMat = full;
            emptyMat = empty;
            range = 2.1f;
        }

        public override float HoldTime => 2.5f;

        public override bool CanInteract(Thief t) =>
            !robbed && t.state == ThiefState.Free && GameManager.I.phase == GamePhase.Heist &&
            (occupant == null || occupant == t);

        public override string Prompt(Thief t) => $"금고 털기 (+{money})";

        public override void OnHoldStart(Thief t) => occupant = t;

        public override void OnHoldCancel(Thief t)
        {
            if (occupant == t) occupant = null;
        }

        public override void Complete(Thief t)
        {
            t.money += money;
            GameManager.I.Log($"{t.displayName}: 금고에서 {money}원 획득!");
            robbed = true;
            occupant = null;
            respawnAt = Time.time + respawnDelay;
            if (door) door.sharedMaterial = emptyMat;
        }

        void Update()
        {
            if (robbed && Time.time >= respawnAt)
            {
                robbed = false;
                money = Random.Range(10, 19) * 10;
                if (door) door.sharedMaterial = fullMat;
            }
        }
    }
}
