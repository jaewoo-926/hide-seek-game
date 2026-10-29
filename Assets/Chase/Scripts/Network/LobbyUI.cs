using Unity.Netcode;
using UnityEngine;

namespace Chase.Net
{
    // 대기방 HUD: 방 정보/참가 코드, 참가자 목록, 머리 위 닉네임, 시작/나가기 버튼
    public class LobbyUI : MonoBehaviour
    {
        // 기획 기준은 4명. 테스트 편의를 위해 2명(경찰 1 + 도둑 1)부터 시작 가능
        public int minPlayersToStart = 2;

        Font font;
        GUIStyle label, small, title, button, nameplate, code;
        float scale;
        string notice;
        float noticeUntil;

        void EnsureStyles()
        {
            float s = Screen.height / 1080f;
            if (label != null && Mathf.Approximately(s, scale)) return;
            scale = s;
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 16);

            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = Mathf.RoundToInt(20 * s), richText = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(16 * s) };
            title = new GUIStyle(label) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold };
            code = new GUIStyle(label) { fontSize = Mathf.RoundToInt(34 * s), fontStyle = FontStyle.Bold };
            code.normal.textColor = new Color(1f, 0.85f, 0.3f);
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = Mathf.RoundToInt(20 * s) };
            nameplate = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Bold };
        }

        void OnGUI()
        {
            var sm = SessionManager.I;
            var net = NetworkManager.Singleton;
            if (sm == null || net == null) return;
            EnsureStyles();

            DrawNameplates();
            DrawRoomPanel(sm, net);
            DrawButtons(sm, net);
            DrawTeamPicker();

            GUI.Label(new Rect(18 * scale, Screen.height - 40 * scale, 800 * scale, 30 * scale),
                "<color=#BBBBBB>WASD 이동 · 우클릭 드래그 시점 회전 · 휠 줌</color>", small);
            if (Time.time < noticeUntil)
                GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 40 * scale),
                    $"<color=#FFE066>{notice}</color>", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawNameplates()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var p in LobbyPlayer.All)
            {
                Vector3 sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 2.3f);
                if (sp.z < 0) continue;
                string name = (p.IsHostPlayer ? "<color=#FFD54A>★</color> " : "") + p.DisplayName;
                name = p.IsOwner ? $"<color=#9EF0FF>{name}</color>"
                    : p.IsCop ? $"<color=#8FB8FF>{name}</color>" : $"<color=#FF8A7A>{name}</color>";
                var r = new Rect(sp.x - 120 * scale, Screen.height - sp.y - 16 * scale, 240 * scale, 32 * scale);
                var shadow = new Rect(r.x + 2, r.y + 2, r.width, r.height);
                GUI.Label(shadow, $"<color=#000000AA>{StripTags(name)}</color>", nameplate);
                GUI.Label(r, name, nameplate);
            }
        }

        void DrawRoomPanel(SessionManager sm, NetworkManager net)
        {
            var rect = new Rect(16 * scale, 16 * scale, 360 * scale, (190 + LobbyPlayer.All.Count * 28) * scale);
            Fill(rect, new Color(0f, 0f, 0f, 0.6f));
            float x = rect.x + 14 * scale, y = rect.y + 10 * scale, w = rect.width - 28 * scale;

            var session = sm.Session;
            GUI.Label(new Rect(x, y, w, 36 * scale), session != null ? session.Name : "대기방", title);
            y += 40 * scale;
            GUI.Label(new Rect(x, y, w, 24 * scale), "참가 코드", small);
            y += 22 * scale;
            string joinCode = session != null ? session.Code : "-";
            GUI.Label(new Rect(x, y, w, 44 * scale), joinCode, code);
            if (session != null && GUI.Button(new Rect(rect.xMax - 100 * scale, y + 4 * scale, 86 * scale, 34 * scale), "복사", button))
            {
                GUIUtility.systemCopyBuffer = joinCode;
                Notice("참가 코드를 복사했습니다");
            }
            y += 52 * scale;

            int max = session != null ? session.MaxPlayers : 0;
            GUI.Label(new Rect(x, y, w, 26 * scale), $"<b>참가자</b>  {LobbyPlayer.All.Count} / {max}", label);
            y += 32 * scale;
            foreach (var p in LobbyPlayer.All)
            {
                var c = LobbyPlayer.Palette[Mathf.Max(0, p.ColorIndex.Value) % LobbyPlayer.Palette.Length];
                Fill(new Rect(x, y + 6 * scale, 14 * scale, 14 * scale), c);
                string tag = (p.IsCop ? "  <color=#8FB8FF>[경찰]</color>" : "  <color=#FF8A7A>[도둑]</color>")
                             + (p.IsHostPlayer ? "  <color=#FFD54A>(방장)</color>" : "");
                string me = p.IsOwner ? "  <color=#9EF0FF>(나)</color>" : "";
                GUI.Label(new Rect(x + 24 * scale, y, w, 26 * scale), p.DisplayName + tag + me, small);
                y += 28 * scale;
            }
        }

        void DrawButtons(SessionManager sm, NetworkManager net)
        {
            float bw = 220 * scale, bh = 54 * scale;
            float x = Screen.width - bw - 24 * scale, y = Screen.height - bh - 24 * scale;

            if (GUI.Button(new Rect(x, y, bw, bh), "방 나가기", button))
                sm.LeaveRoom();

            if (!net.IsHost)
            {
                GUI.Label(new Rect(x - 40 * scale, y - 40 * scale, bw + 40 * scale, 34 * scale),
                    "<color=#BBBBBB>방장이 게임을 시작하길 기다리는 중...</color>", small);
                return;
            }

            y -= bh + 12 * scale;
            int count = LobbyPlayer.All.Count;
            int cops = 0;
            foreach (var p in LobbyPlayer.All) if (p.IsCop) cops++;
            if (GUI.Button(new Rect(x, y, bw, bh), $"게임 시작 ({count}/{minPlayersToStart})", button))
            {
                if (count < minPlayersToStart) Notice($"최소 {minPlayersToStart}명이 필요합니다");
                else if (cops == 0) Notice("경찰 팀이 1명 이상 필요합니다");
                else if (cops == count) Notice("도둑 팀이 1명 이상 필요합니다");
                else StartGame(sm);
            }
        }

        void StartGame(SessionManager sm)
        {
            // 서버가 인게임 씬에서 쓸 참가자 정보 저장
            Chase.Online.MatchSetup.Players.Clear();
            foreach (var p in LobbyPlayer.All)
            {
                Chase.Online.MatchSetup.Players[p.OwnerClientId] = new Chase.Online.MatchSetup.Entry
                {
                    role = p.IsCop ? Chase.Online.Role.Cop : Chase.Online.Role.Thief,
                    nickname = p.DisplayName,
                    color = Mathf.Max(0, p.ColorIndex.Value),
                };
            }
            sm.StartGame();
        }

        void DrawTeamPicker()
        {
            LobbyPlayer me = null;
            foreach (var p in LobbyPlayer.All) if (p.IsOwner) me = p;
            if (me == null) return;

            float bw = 200 * scale, bh = 52 * scale, gap = 16 * scale;
            float x = (Screen.width - bw * 2 - gap) / 2f, y = Screen.height - bh - 24 * scale;
            GUI.Label(new Rect(x, y - 34 * scale, bw * 2 + gap, 30 * scale),
                "팀 선택", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });

            var prev = GUI.backgroundColor;
            GUI.backgroundColor = me.IsCop ? Color.gray : new Color(1f, 0.55f, 0.48f);
            if (GUI.Button(new Rect(x, y, bw, bh), me.IsCop ? "도둑 팀" : "✔ 도둑 팀", button)) me.SetRole(0);
            GUI.backgroundColor = me.IsCop ? new Color(0.56f, 0.72f, 1f) : Color.gray;
            if (GUI.Button(new Rect(x + bw + gap, y, bw, bh), me.IsCop ? "✔ 경찰 팀" : "경찰 팀", button)) me.SetRole(1);
            GUI.backgroundColor = prev;
        }

        void Notice(string text)
        {
            notice = text;
            noticeUntil = Time.time + 3f;
        }

        static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");

        static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
