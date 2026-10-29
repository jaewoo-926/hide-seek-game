using UnityEngine;

namespace Chase
{
    // 프로토타입용 IMGUI HUD: 팀 게이지, 페이즈, 비밀번호, 아이템, 상호작용 프롬프트, 로그, 결과 화면
    public class HUD : MonoBehaviour
    {
        public ThiefPlayer playerControl;

        Font font;
        GUIStyle label, center, big, small;
        float scale;

        static readonly Color Panel = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color GaugeFill = new Color(1f, 0.78f, 0.2f);
        static readonly Color LastTimeFill = new Color(0.95f, 0.25f, 0.25f);

        void EnsureStyles()
        {
            float s = Screen.height / 1080f;
            if (label != null && Mathf.Approximately(s, scale)) return;
            scale = s;
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 16);

            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = Mathf.RoundToInt(20 * s), richText = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(17 * s) };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(22 * s) };
            big = new GUIStyle(center) { fontSize = Mathf.RoundToInt(64 * s), fontStyle = FontStyle.Bold };
        }

        void OnGUI()
        {
            var gm = GameManager.I;
            if (gm == null || gm.player == null) return;
            EnsureStyles();

            DrawGauge(gm);
            DrawRoster(gm);
            DrawStatus(gm);
            DrawPrompt(gm);
            DrawLog(gm);
            DrawControls();
            if (!gm.IsPlaying) DrawEnd(gm);
        }

        void DrawGauge(GameManager gm)
        {
            float w = 640 * scale, h = 30 * scale;
            var rect = new Rect((Screen.width - w) / 2f, 18 * scale, w, h);
            Fill(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), Panel);

            string text;
            if (gm.phase == GamePhase.LastTime || (gm.phase != GamePhase.Heist && gm.GaugeRatio >= 1f))
            {
                float ratio = Mathf.Clamp01(gm.lastTimeRemaining / gm.lastTimeDuration);
                Fill(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), LastTimeFill);
                text = $"<b>라스트 타임!</b>  남은 시간 {Mathf.CeilToInt(Mathf.Max(0, gm.lastTimeRemaining))}초";
            }
            else
            {
                Fill(new Rect(rect.x, rect.y, rect.width * gm.GaugeRatio, rect.height), GaugeFill);
                text = $"<b>팀 게이지</b>  {gm.Gauge} / {gm.targetMoney}";
            }
            GUI.Label(rect, text, center);

            string sub = gm.phase == GamePhase.LastTime
                ? "<color=#7CFF9A>초록 빛기둥 = 탈출구</color>   경찰 이속·시야 버프 / 잡히면 즉시 탈락"
                : gm.copsDeployed ? "<color=#8FB8FF>경찰 순찰 중</color>"
                : $"<color=#8FB8FF>경찰 진입까지 {Mathf.CeilToInt(gm.CopEntryRemaining)}초</color>";
            GUI.Label(new Rect(rect.x, rect.yMax + 6 * scale, rect.width, 28 * scale), sub, center);
        }

        void DrawRoster(GameManager gm)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b><color=#FF8A7A>도둑 팀</color></b>");
            foreach (var t in gm.thieves)
            {
                string st;
                switch (t.state)
                {
                    case ThiefState.Hidden: st = "<color=#9AE6B4>은신</color>"; break;
                    case ThiefState.Jailed: st = "<color=#FF6B6B>수감</color>"; break;
                    case ThiefState.Eliminated: st = "<color=#888888>탈락</color>"; break;
                    case ThiefState.Escaped: st = "<color=#7CFF9A>탈출</color>"; break;
                    default: st = "활동"; break;
                }
                sb.AppendLine($"  {t.displayName}  {st}  {t.money}원");
            }
            sb.AppendLine("<b><color=#8FB8FF>경찰 팀</color></b>");
            foreach (var c in gm.cops)
            {
                string st = !gm.copsDeployed ? "대기" : c.IsStunned ? "<color=#FFE066>기절</color>" : c.hasCuffs ? "수갑 보유" : "<color=#FF6B6B>수갑 사용 중 (곤봉)</color>";
                sb.AppendLine($"  {c.displayName}  {st}");
            }
            var rect = new Rect(16 * scale, 16 * scale, 330 * scale, 260 * scale);
            Fill(rect, Panel);
            GUI.Label(new Rect(rect.x + 10 * scale, rect.y + 8 * scale, rect.width, rect.height), sb.ToString(), small);
        }

        void DrawStatus(GameManager gm)
        {
            var p = gm.player;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>탈출 비밀번호</b>  [ {gm.PasswordDisplay} ]");
            sb.AppendLine($"<b>감옥 위치</b>  {(gm.prisonKnown ? "<color=#FF8A7A>발견</color>" : "모름")}");
            sb.AppendLine($"<b>소지금</b>  {p.money}원");
            sb.AppendLine($"<b>[F] 에너지 드링크</b>  x{p.energyDrinks}");
            sb.AppendLine(p.ClubCooldownRemaining > 0
                ? $"<b>[좌클릭] 몽둥이</b>  {p.ClubCooldownRemaining:0.0}초"
                : "<b>[좌클릭] 몽둥이</b>  <color=#7CFF9A>준비</color>");
            if (p.IsBuffed) sb.AppendLine($"<color=#7CE8FF>이속 증가 {p.BuffRemaining:0.0}초</color>");
            if (p.IsSlowed) sb.AppendLine("<color=#FF6B6B>곤봉에 맞아 둔화!</color>");

            var rect = new Rect(Screen.width - 346 * scale, 16 * scale, 330 * scale, 200 * scale);
            Fill(rect, Panel);
            GUI.Label(new Rect(rect.x + 10 * scale, rect.y + 8 * scale, rect.width, rect.height), sb.ToString(), small);
        }

        void DrawPrompt(GameManager gm)
        {
            if (!gm.IsPlaying) return;
            var p = gm.player;
            string text = null;
            float ratio = 0f;

            if (p.state == ThiefState.Jailed)
            {
                text = "<color=#FF6B6B>감옥에 갇혔다!</color> 동료가 구출하러 오길 기다리자";
            }
            else if (p.state == ThiefState.Hidden && p.hideSpot)
            {
                float t = p.hideSpot.HiddenFor;
                string warn = t > p.hideSpot.safeTime ? "<color=#FF6B6B>너무 오래 숨어 있다! 곧 들킨다</color>" : "숨는 중";
                text = $"{warn}  ({t:0.0}초)   <b>[E]</b> 나오기";
            }
            else if (p.state == ThiefState.Eliminated)
            {
                text = "탈락했다 - 동료를 관전 중";
            }
            else if (playerControl && playerControl.Holding != null)
            {
                text = playerControl.Holding.Prompt(p);
                ratio = playerControl.HoldRatio;
            }
            else if (playerControl && playerControl.Focus != null)
            {
                var f = playerControl.Focus;
                text = $"<b>[E]</b> {f.Prompt(p)}" + (f.HoldTime > 0f ? "  (꾹 누르기)" : "");
            }
            if (text == null) return;

            float w = 560 * scale, h = 44 * scale;
            var rect = new Rect((Screen.width - w) / 2f, Screen.height - 150 * scale, w, h);
            Fill(rect, Panel);
            GUI.Label(rect, text, center);
            if (ratio > 0f)
            {
                var bar = new Rect(rect.x, rect.yMax, rect.width, 8 * scale);
                Fill(bar, Panel);
                Fill(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), GaugeFill);
            }
        }

        void DrawLog(GameManager gm)
        {
            float y = Screen.height - 240 * scale;
            float lineH = 26 * scale;
            for (int i = gm.messages.Count - 1, n = 0; i >= 0 && n < 6; i--, n++)
            {
                var (text, time) = gm.messages[i];
                float age = Time.time - time;
                if (age > 8f) break;
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(8f - age));
                GUI.Label(new Rect(18 * scale, y - n * lineH, 700 * scale, lineH), text, small);
                GUI.color = prev;
            }
        }

        void DrawControls()
        {
            const string help = "WASD 이동 · 마우스 시선 · 우클릭 드래그 시점 회전 · 휠 줌\nE 상호작용(금고/은신/구출/탈출) · 좌클릭 몽둥이 · F 에너지 드링크";
            GUI.Label(new Rect(18 * scale, Screen.height - 64 * scale, 900 * scale, 60 * scale),
                $"<color=#BBBBBB>{help}</color>", small);
        }

        void DrawEnd(GameManager gm)
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
            bool thievesWin = gm.phase == GamePhase.ThievesWin;
            string title = thievesWin ? "<color=#FF8A7A>도둑 승리!</color>" : "<color=#8FB8FF>경찰 승리!</color>";
            GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 90 * scale), title, big);
            GUI.Label(new Rect(0, Screen.height * 0.35f + 100 * scale, Screen.width, 40 * scale), gm.endReason, center);
            GUI.Label(new Rect(0, Screen.height * 0.35f + 150 * scale, Screen.width, 40 * scale), "<color=#BBBBBB>R 키를 눌러 다시 시작</color>", center);
        }

        static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
