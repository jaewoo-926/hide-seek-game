namespace Chase
{
    // 도둑 탐색 아이템: 사용 시 잠시 이동 속도 증가
    public class EnergyDrink : Pickup
    {
        protected override void OnPicked(Thief t)
        {
            t.energyDrinks++;
            GameManager.I.Log($"{t.displayName}: 에너지 드링크 획득");
        }
    }
}
