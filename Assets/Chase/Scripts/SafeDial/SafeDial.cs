using System;

namespace Chase.Online
{
    // 값이 변하는 부호. 오른쪽(시계 방향)으로 돌리면 눈금이 줄어든다.
    public enum DialDirection { Right = -1, Left = 1 }

    public enum ConfirmResult { Hit, Miss, Locked, Done }

    // 금고 다이얼 판정 (기획: 금고 미니게임 시트).
    // 화면·네트워크·유니티를 모르는 순수 규칙 코드라서 EditMode 테스트로 확인한다.
    // 단계는 서버가 올린다: Confirm 이 Hit 를 돌려주면 서버에 요청하고, 결과는 SetStep 으로 받는다.
    public sealed class SafeDial
    {
        public const int Ticks = 100;  // safeDialTicks
        public const int Steps = 3;    // safeDialSteps

        readonly int[] targets;
        readonly int zone;
        readonly float speed;          // safeDialSpeed (칸/초)
        readonly float failLockSec;    // safeFailLockSec

        float value;
        int lastSign;                  // 마지막으로 돌린 방향 (-1 오른쪽, +1 왼쪽, 0 아직 안 돌림)
        bool inZone;

        public float Value => value;
        public int Tick => (int)Math.Round(value) % Ticks;
        public int Step { get; private set; }
        public bool IsOpen => Step >= Steps;
        public DialDirection Direction => Step == 1 ? DialDirection.Left : DialDirection.Right;
        public bool InZone => inZone;
        // 지금 단계 목표까지 남은 원형 거리 (가까워질수록 삐삐 소리를 빠르게)
        public int DistanceToTarget => IsOpen ? 0 : Distance(Tick, targets[Step]);
        // 마지막으로 돌린 방향이 이번 단계 방향과 같은지
        public bool TurningRightWay => !IsOpen && lastSign == (int)Direction;
        public double LockedUntil { get; private set; } = double.MinValue;

        public event Action EnteredZone;
        public event Action Failed;

        public SafeDial(int[] targets, int zone, int step, float speed = 30f, float failLockSec = 1f)
        {
            if (targets == null || targets.Length < Steps) throw new ArgumentException("목표 눈금은 3개여야 한다", nameof(targets));
            this.targets = targets;
            this.zone = zone;
            this.speed = speed;
            this.failLockSec = failLockSec;
            Step = Math.Max(0, Math.Min(Steps, step));
        }

        public bool IsLocked(double now) => now < LockedUntil;

        // ticks 의 부호가 방향: 음수 = 오른쪽, 양수 = 왼쪽
        public void Rotate(float ticks, double now)
        {
            if (IsOpen || IsLocked(now) || ticks == 0f) return;
            value = Wrap(value + ticks);
            lastSign = ticks < 0f ? -1 : 1;
            UpdateZone();
        }

        // axis: -1..1, +1 = 오른쪽
        public void RotateByInput(float axis, float dt, double now) => Rotate(-axis * speed * dt, now);

        public ConfirmResult Confirm(double now)
        {
            if (IsOpen) return ConfirmResult.Done;
            if (IsLocked(now)) return ConfirmResult.Locked;
            if (inZone) return ConfirmResult.Hit;
            LockedUntil = now + failLockSec;
            inZone = false;
            Failed?.Invoke();
            return ConfirmResult.Miss;
        }

        // 서버가 동기화한 진행 단계 반영. 단계가 바뀌면 구간 상태를 새로 계산한다.
        public void SetStep(int step)
        {
            step = Math.Max(0, Math.Min(Steps, step));
            if (step == Step) return;
            Step = step;
            inZone = false;
            lastSign = 0;
        }

        void UpdateZone()
        {
            bool now = !IsOpen && lastSign == (int)Direction && IsHit(Tick, targets[Step], zone);
            if (now && !inZone) EnteredZone?.Invoke();
            inZone = now;
        }

        static float Wrap(float v) => ((v % Ticks) + Ticks) % Ticks;

        // 원형 거리: 98 과 1 사이는 3
        public static int Distance(int a, int b)
        {
            int d = Math.Abs(((a % Ticks) + Ticks) % Ticks - ((b % Ticks) + Ticks) % Ticks);
            return Math.Min(d, Ticks - d);
        }

        public static bool IsHit(int tick, int target, int zone) => Distance(tick, target) <= zone;
    }
}
