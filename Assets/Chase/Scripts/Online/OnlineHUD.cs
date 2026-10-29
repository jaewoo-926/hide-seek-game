using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Chase.Online
{
    // 온라인 인게임 HUD (IMGUI 프로토타입)
    public class OnlineHUD : MonoBehaviour
    {
        Font font;
        GUIStyle label, center, big, small, button, nameplate;
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
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = Mathf.RoundToInt(22 * s) };
            nameplate = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(17 * s), fontStyle = FontStyle.Bold };
        }

        void OnGUI()
        {
            var game = OnlineGame.I;
            var me = OnlinePlayer.Local;
            if (game == null || me == null) return;
            EnsureStyles();

            DrawNameplates(game);
            DrawPing(game, me);
            DrawGauge(game, me);
            DrawRoster(game);
            DrawStatus(game, me);
            DrawPrompt(game, me);
            DrawLog(game);
            DrawControls(me);
            if (!game.IsPlaying) DrawEnd(game);
        }

        void DrawNameplates(OnlineGame game)
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var p in OnlinePlayer.All)
            {
                if (!game.IsVisibleToLocal(p)) continue;
                Vector3 sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 2.3f);
                if (sp.z < 0) continue;
                string color = p.IsCop ? "#8FB8FF" : "#FF8A7A";
                if (p == OnlinePlayer.Local) color = "#9EF0FF";
                string extra = p.IsStunned ? " <color=#FFE066>(기절)</color>" : "";
                GUI.Label(new Rect(sp.x - 120 * scale, Screen.height - sp.y - 14 * scale, 240 * scale, 28 * scale),
                    $"<color={color}>{p.DisplayName}</color>{extra}", nameplate);
            }
        }

        void DrawPing(OnlineGame game, OnlinePlayer me)
        {
            if (!me.IsCop || game.PingPosition == null || Time.time > game.PingUntil) return;
            var cam = Camera.main;
            Vector3 sp = cam.WorldToScreenPoint(game.PingPosition.Value + Vector3.up * 1.5f);
            if (sp.z < 0) return;
            float pulse = 1f + Mathf.PingPong(Time.time * 2f, 0.4f);
            GUI.Label(new Rect(sp.x - 100 * scale, Screen.height - sp.y - 20 * scale * pulse, 200 * scale, 40 * scale),
                "<color=#FFE066><b>! 소리 !</b></color>", center);
        }

        void DrawGauge(OnlineGame game, OnlinePlayer me)
        {
            float w = 640 * scale, h = 30 * scale;
            var rect = new Rect((Screen.width - w) / 2f, 18 * scale, w, h);
            Fill(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), Panel);

            string text;
            if (game.Phase == OnlinePhase.LastTime)
            {
                Fill(new Rect(rect.x, rect.y, rect.width * (game.LastTimeRemaining / game.lastTimeDuration), rect.height), LastTimeFill);
                text = $"<b>라스트 타임!</b>  남은 시간 {Mathf.CeilToInt(game.LastTimeRemaining)}초";
            }
            else
            {
                Fill(new Rect(rect.x, rect.y, rect.width * game.GaugeRatio, rect.height), GaugeFill);
                text = $"<b>도둑 팀 게이지</b>  {game.Gauge} / {game.targetMoney}";
            }
            GUI.Label(rect, text, center);

            string sub;
            if (game.Phase == OnlinePhase.LastTime)
                sub = me.IsCop ? "<color=#8FB8FF>버프: 이속 증가 · 모든 도둑이 보인다 · 잡으면 즉시 탈락</color>"
                    : "<color=#7CFF9A>초록 빛기둥 = 탈출구</color>   잡히면 즉시 탈락!";
            else if (!game.CopsDeployed.Value)
                sub = $"<color=#8FB8FF>경찰 진입까지 {Mathf.CeilToInt(game.CopEntryRemaining)}초</color>";
            else
                sub = "<color=#8FB8FF>경찰 순찰 중</color>";
            GUI.Label(new Rect(rect.x, rect.yMax + 6 * scale, rect.width, 28 * scale), sub, center);
        }

        void DrawRoster(OnlineGame game)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b><color=#FF8A7A>도둑 팀</color></b>");
            foreach (var t in OnlinePlayer.All.Where(p => p.IsThief))
            {
                string st;
                switch (t.State)
                {
                    case PState.Hidden: st = "<color=#9AE6B4>은신</color>"; break;
                    case PState.Jailed: st = "<color=#FF6B6B>수감</color>"; break;
                    case PState.Eliminated: st = "<color=#888888>탈락</color>"; break;
                    case PState.Escaped: st = "<color=#7CFF9A>탈출</color>"; break;
                    default: st = "활동"; break;
                }
                sb.AppendLine($"  {t.DisplayName}  {st}  {t.Money.Value}원");
            }
            sb.AppendLine("<b><color=#8FB8FF>경찰 팀</color></b>");
            foreach (var c in OnlinePlayer.All.Where(p => p.IsCop))
            {
                string st = c.State == PState.Waiting ? "대기" : c.IsStunned ? "<color=#FFE066>기절</color>"
                    : c.HasCuffs.Value ? "수갑 보유" : "<color=#FF6B6B>수갑 사용 중 (곤봉)</color>";
                sb.AppendLine($"  {c.DisplayName}  {st}");
            }
            int lines = OnlinePlayer.All.Count + 2;
            var rect = new Rect(16 * scale, 16 * scale, 340 * scale, (20 + lines * 24) * scale);
            Fill(rect, Panel);
            GUI.Label(new Rect(rect.x + 10 * scale, rect.y + 8 * scale, rect.width, rect.height), sb.ToString(), small);
        }

        void DrawStatus(OnlineGame game, OnlinePlayer me)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(me.IsCop ? "<b><color=#8FB8FF>나는 경찰</color></b>" : "<b><color=#FF8A7A>나는 도둑</color></b>");
            if (me.IsThief)
            {
                sb.AppendLine($"탈출 비밀번호  [ {game.PasswordDisplay} ]");
                sb.AppendLine($"감옥 위치  {(game.PrisonKnown.Value ? "<color=#FF8A7A>발견</color>" : "모름")}");
                sb.AppendLine($"소지금  {me.Money.Value}원");
                sb.AppendLine($"[F] 에너지 드링크  x{me.Drinks.Value}");
                sb.AppendLine(me.ClubCooldown > 0 ? $"[좌클릭] 몽둥이  {me.ClubCooldown:0.0}초" : "[좌클릭] 몽둥이  <color=#7CFF9A>준비</color>");
                if (me.IsBuffed) sb.AppendLine($"<color=#7CE8FF>이속 증가 {me.BuffRemaining:0.0}초</color>");
                if (me.IsSlowed) sb.AppendLine("<color=#FF6B6B>곤봉에 맞아 둔화!</color>");
            }
            else
            {
                sb.AppendLine(me.HasCuffs.Value ? "[좌클릭] 수갑 체포" : "[좌클릭] 곤봉 (수갑 사용 중)");
                sb.AppendLine("[E] 은신처 수색");
                if (me.IsStunned) sb.AppendLine($"<color=#FFE066>기절! {me.StunRemaining:0.0}초</color>");
            }

            var rect = new Rect(Screen.width - 356 * scale, 16 * scale, 340 * scale, 210 * scale);
            Fill(rect, Panel);
            GUI.Label(new Rect(rect.x + 10 * scale, rect.y + 8 * scale, rect.width, rect.height), sb.ToString(), small);
        }

        void DrawPrompt(OnlineGame game, OnlinePlayer me)
        {
            if (!game.IsPlaying) return;
            string text = null;
            float ratio = 0f;

            switch (me.State)
            {
                case PState.Waiting:
                {
                    string pick = me.ChosenSpawn.Value >= 0 ? $"{me.ChosenSpawn.Value + 1}번" : "랜덤";
                    text = $"경찰 대기 중 - 맵과 금고 위치를 확인하세요.  <b>[1~{game.CopSpawnCount}]</b> 진입 위치 선택: <color=#FFE066>{pick}</color>";
                    break;
                }
                case PState.Jailed:
                    text = "<color=#FF6B6B>감옥에 갇혔다!</color> 동료가 구출하러 오길 기다리자";
                    break;
                case PState.Eliminated:
                    text = "탈락했다 - 동료를 관전 중";
                    break;
                case PState.Escaped:
                    text = "탈출 성공!";
                    break;
                case PState.Hidden:
                    text = "숨는 중   <b>[E]</b> 나오기   <color=#BBBBBB>(오래 있으면 들킨다)</color>";
                    break;
                default:
                    if (me.Holding)
                    {
                        text = me.Focus.prompt;
                        ratio = me.HoldRatio;
                    }
                    else if (me.Focus.IsValid)
                    {
                        text = $"<b>[E]</b> {me.Focus.prompt}" + (me.Focus.holdTime > 0f ? "  (꾹 누르기)" : "");
                    }
                    break;
            }
            if (text == null) return;

            float w = 820 * scale, h = 44 * scale;
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

        void DrawLog(OnlineGame game)
        {
            float y = Screen.height - 240 * scale;
            float lineH = 26 * scale;
            for (int i = game.messages.Count - 1, n = 0; i >= 0 && n < 6; i--, n++)
            {
                var (text, time) = game.messages[i];
                float age = Time.time - time;
                if (age > 8f) break;
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(8f - age));
                GUI.Label(new Rect(18 * scale, y - n * lineH, 760 * scale, lineH), text, small);
                GUI.color = prev;
            }
        }

        void DrawControls(OnlinePlayer me)
        {
            string help = me.IsCop
                ? "WASD 이동 · 마우스 시선 · 우클릭 드래그 시점 회전 · 휠 줌\n좌클릭 체포/곤봉 · E 은신처 수색"
                : "WASD 이동 · 마우스 시선 · 우클릭 드래그 시점 회전 · 휠 줌\nE 상호작용(금고/은신/구출/탈출) · 좌클릭 몽둥이 · F 에너지 드링크";
            GUI.Label(new Rect(18 * scale, Screen.height - 64 * scale, 900 * scale, 60 * scale), $"<color=#BBBBBB>{help}</color>", small);
        }

        void DrawEnd(OnlineGame game)
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
            bool thievesWin = game.Phase == OnlinePhase.ThievesWin;
            GUI.Label(new Rect(0, Screen.height * 0.32f, Screen.width, 90 * scale),
                thievesWin ? "<color=#FF8A7A>도둑 승리!</color>" : "<color=#8FB8FF>경찰 승리!</color>", big);
            GUI.Label(new Rect(0, Screen.height * 0.32f + 100 * scale, Screen.width, 40 * scale), game.EndReason.Value.ToString(), center);

            var net = NetworkManager.Singleton;
            if (net != null && net.IsHost)
            {
                float bw = 320 * scale, bh = 60 * scale;
                if (GUI.Button(new Rect((Screen.width - bw) / 2f, Screen.height * 0.32f + 170 * scale, bw, bh), "대기방으로 돌아가기", button))
                    Chase.Net.SessionManager.I.ReturnToLobby();
            }
            else
            {
                GUI.Label(new Rect(0, Screen.height * 0.32f + 170 * scale, Screen.width, 40 * scale),
                    "<color=#BBBBBB>방장이 대기방으로 돌아가길 기다리는 중...</color>", center);
            }
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
