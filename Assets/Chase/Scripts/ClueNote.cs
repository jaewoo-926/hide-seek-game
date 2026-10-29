namespace Chase
{
    // 탈출구 비밀번호의 한 자리를 알려주는 쪽지 (팀 전체 공유 = 보이스챗 가정)
    public class ClueNote : Pickup
    {
        public int digitIndex;

        protected override void OnPicked(Thief t) => GameManager.I.RevealDigit(digitIndex, t);
    }
}
