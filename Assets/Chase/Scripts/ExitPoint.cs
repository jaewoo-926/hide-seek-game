namespace Chase
{
    // 탈출구: 라스트 타임에 등장, 비밀번호를 알아야 탈출 가능
    public class ExitPoint : Interactable
    {
        public override float HoldTime => 1.5f;

        public override bool CanInteract(Thief t) =>
            t.state == ThiefState.Free && GameManager.I.phase == GamePhase.LastTime;

        public override string Prompt(Thief t) =>
            GameManager.I.PasswordComplete
                ? $"비밀번호 [{GameManager.I.password}] 입력하고 탈출"
                : $"비밀번호 입력 ({GameManager.I.PasswordDisplay}) - 단서 부족";

        public override void Complete(Thief t)
        {
            if (!GameManager.I.PasswordComplete)
            {
                GameManager.I.Log("비밀번호가 틀렸다! 단서를 더 찾아야 한다.");
                return;
            }
            GameManager.I.ThiefEscaped(t);
        }
    }
}
