using Chase.Online;
using NUnit.Framework;

namespace Chase.Tests
{
    // 금고 다이얼 판정 규칙 테스트 (시간은 인자로 넘긴다)
    public class SafeDialTests
    {
        static SafeDial Make(int t0 = 50, int t1 = 20, int t2 = 70, int zone = 3, int step = 0)
            => new SafeDial(new[] { t0, t1, t2 }, zone, step, 30f, 1f);

        [Test]
        public void Distance_IsCircular()
        {
            Assert.AreEqual(3, SafeDial.Distance(98, 1));
            Assert.AreEqual(30, SafeDial.Distance(10, 40));
            Assert.AreEqual(50, SafeDial.Distance(0, 50));
        }

        [Test]
        public void Hit_WrapsAroundZero()
        {
            Assert.IsTrue(SafeDial.IsHit(99, 1, 3));
            Assert.IsTrue(SafeDial.IsHit(2, 98, 4));
            Assert.IsFalse(SafeDial.IsHit(5, 98, 4));
        }

        [Test]
        public void Direction_RightLeftRight()
        {
            var dial = Make();
            Assert.AreEqual(DialDirection.Right, dial.Direction);
            dial.SetStep(1);
            Assert.AreEqual(DialDirection.Left, dial.Direction);
            dial.SetStep(2);
            Assert.AreEqual(DialDirection.Right, dial.Direction);
        }

        [Test]
        public void Rotate_Right_DecreasesAndWraps()
        {
            var dial = Make();
            dial.Rotate(-3f, 0);
            Assert.AreEqual(97, dial.Tick);
        }

        [Test]
        public void EnteringZone_FiresOnce()
        {
            var dial = Make(t0: 90, zone: 3);
            int entered = 0;
            dial.EnteredZone += () => entered++;
            for (int i = 0; i < 14; i++) dial.Rotate(-1f, 0); // 오른쪽으로 0 → 86 (93~87 구간을 지나감)
            Assert.AreEqual(1, entered);
            Assert.IsFalse(dial.InZone); // 86 은 90±3 밖
            dial.Rotate(1f, 0);          // 왼쪽 87: 구간 안이지만 방향이 달라 구간 아님
            Assert.IsFalse(dial.InZone);
            Assert.AreEqual(1, entered);
        }

        [Test]
        public void InZone_WhileTurningRightIntoTarget()
        {
            var dial = Make(t0: 90, zone: 3);
            for (int i = 0; i < 9; i++) dial.Rotate(-1f, 0); // 0 → 91
            Assert.AreEqual(91, dial.Tick);
            Assert.IsTrue(dial.InZone);
            Assert.AreEqual(ConfirmResult.Hit, dial.Confirm(0));
        }

        [Test]
        public void WrongDirection_NoTickNoHit()
        {
            var dial = Make(t0: 10, zone: 3);
            int entered = 0;
            dial.EnteredZone += () => entered++;
            for (int i = 0; i < 10; i++) dial.Rotate(1f, 0); // 왼쪽으로 0 → 10 (1단계는 오른쪽이어야 함)
            Assert.AreEqual(10, dial.Tick);
            Assert.AreEqual(0, entered);
            Assert.IsFalse(dial.InZone);
            Assert.AreEqual(ConfirmResult.Miss, dial.Confirm(0));
        }

        [Test]
        public void Confirm_Miss_LocksOneSecond()
        {
            var dial = Make(t0: 50);
            int failed = 0;
            dial.Failed += () => failed++;
            Assert.AreEqual(ConfirmResult.Miss, dial.Confirm(10.0));
            Assert.AreEqual(1, failed);
            dial.Rotate(-5f, 10.5);
            Assert.AreEqual(0, dial.Tick);
            Assert.AreEqual(ConfirmResult.Locked, dial.Confirm(10.5));
            dial.Rotate(-5f, 11.01);
            Assert.AreEqual(95, dial.Tick);
        }

        [Test]
        public void Confirm_Hit_DoesNotAdvanceStep()
        {
            var dial = Make(t0: 97, zone: 3);
            dial.Rotate(-3f, 0);
            Assert.AreEqual(ConfirmResult.Hit, dial.Confirm(0));
            Assert.AreEqual(0, dial.Step);
        }

        [Test]
        public void Confirm_WhenOpen_ReturnsDone()
        {
            var dial = Make();
            dial.SetStep(3);
            Assert.IsTrue(dial.IsOpen);
            Assert.AreEqual(ConfirmResult.Done, dial.Confirm(0));
        }

        [Test]
        public void DistanceToTarget_FollowsCurrentStep()
        {
            var dial = Make(t0: 90, t1: 20);
            Assert.AreEqual(10, dial.DistanceToTarget);   // 0 과 90 의 원형 거리
            dial.Rotate(-5f, 0);                          // 95
            Assert.AreEqual(5, dial.DistanceToTarget);
            dial.SetStep(1);
            Assert.AreEqual(25, dial.DistanceToTarget);   // 95 와 20
        }

        [Test]
        public void TurningRightWay_OnlyWhenLastTurnMatchesDirection()
        {
            var dial = Make();
            Assert.IsFalse(dial.TurningRightWay);         // 아직 안 돌림
            dial.Rotate(-1f, 0);                          // 1단계 오른쪽
            Assert.IsTrue(dial.TurningRightWay);
            dial.Rotate(1f, 0);
            Assert.IsFalse(dial.TurningRightWay);
            dial.SetStep(1);                              // 2단계 왼쪽, 방향 기록 초기화
            Assert.IsFalse(dial.TurningRightWay);
            dial.Rotate(1f, 0);
            Assert.IsTrue(dial.TurningRightWay);
        }

        [Test]
        public void RotateByInput_PositiveAxisIsRight()
        {
            var dial = Make();
            dial.RotateByInput(1f, 0.1f, 0); // 30칸/초 * 0.1초 = 3칸 오른쪽
            Assert.AreEqual(97, dial.Tick);
        }
    }
}
