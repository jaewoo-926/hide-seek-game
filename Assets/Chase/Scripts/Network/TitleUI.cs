using UnityEngine;

namespace Chase.Net
{
    // 타이틀 화면: 닉네임, 방 만들기, 코드로 참가, 공개 방 목록
    public class TitleUI : MonoBehaviour
    {
        Font font;
        GUIStyle label, small, header, logo, button, field, toggle;
        float scale;

        string roomName = "";
        string joinCode = "";
        int maxPlayers = 6;
        bool isPrivate;
        Vector2 listScroll;

        void EnsureStyles()
        {
            float s = Screen.height / 1080f;
            if (label != null && Mathf.Approximately(s, scale)) return;
            scale = s;
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 16);

            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = Mathf.RoundToInt(20 * s), richText = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(16 * s) };
            header = new GUIStyle(label) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold };
            logo = new GUIStyle(label) { fontSize = Mathf.RoundToInt(72 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = Mathf.RoundToInt(20 * s) };
            field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = Mathf.RoundToInt(22 * s), alignment = TextAnchor.MiddleLeft };
            toggle = new GUIStyle(GUI.skin.toggle) { font = font, fontSize = Mathf.RoundToInt(18 * s) };
        }

        void OnGUI()
        {
            var sm = SessionManager.I;
            if (sm == null) return;
            EnsureStyles();

            GUI.Label(new Rect(0, 40 * scale, Screen.width, 100 * scale),
                "<color=#FF8A7A>경찰</color>과 <color=#8FB8FF>도둑</color>", logo);
            GUI.Label(new Rect(0, 130 * scale, Screen.width, 30 * scale),
                "<color=#BBBBBB>Team Chase · 멀티플레이 프로토타입</color>", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });

            float panelW = 520 * scale, panelH = 640 * scale, gap = 40 * scale;
            float left = (Screen.width - panelW * 2 - gap) / 2f, top = 200 * scale;

            GUI.enabled = sm.IsSignedIn && !sm.Busy;
            DrawCreateJoin(sm, new Rect(left, top, panelW, panelH));
            DrawRoomList(sm, new Rect(left + panelW + gap, top, panelW, panelH));
            GUI.enabled = true;

            string status = !string.IsNullOrEmpty(sm.Error) ? $"<color=#FF6B6B>{sm.Error}</color>"
                : !string.IsNullOrEmpty(sm.Status) ? $"<color=#FFE066>{sm.Status}</color>" : "";
            GUI.Label(new Rect(0, top + panelH + 20 * scale, Screen.width, 40 * scale), status,
                new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawCreateJoin(SessionManager sm, Rect r)
        {
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            float x = r.x + 24 * scale, y = r.y + 20 * scale, w = r.width - 48 * scale, h = 46 * scale;

            GUI.Label(new Rect(x, y, w, 34 * scale), "닉네임", header);
            y += 40 * scale;
            sm.Nickname = GUI.TextField(new Rect(x, y, w, h), sm.Nickname, SessionManager.NicknameMaxLength, field);
            y += h + 30 * scale;

            GUI.Label(new Rect(x, y, w, 34 * scale), "방 만들기", header);
            y += 40 * scale;
            GUI.Label(new Rect(x, y, 120 * scale, h), "방 이름", label);
            roomName = GUI.TextField(new Rect(x + 120 * scale, y, w - 120 * scale, h), roomName, 24, field);
            y += h + 10 * scale;

            GUI.Label(new Rect(x, y, 120 * scale, h), "최대 인원", label);
            if (GUI.Button(new Rect(x + 120 * scale, y, h, h), "-", button)) maxPlayers = Mathf.Max(4, maxPlayers - 1);
            GUI.Label(new Rect(x + 120 * scale + h, y, 80 * scale, h), $"{maxPlayers}명", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(new Rect(x + 200 * scale + h, y, h, h), "+", button)) maxPlayers = Mathf.Min(10, maxPlayers + 1);
            isPrivate = GUI.Toggle(new Rect(x + 270 * scale + h * 2, y + 10 * scale, 160 * scale, h), isPrivate, " 비공개 방", toggle);
            y += h + 14 * scale;

            if (GUI.Button(new Rect(x, y, w, 56 * scale), "방 만들기", button))
                _ = sm.CreateRoomAsync(string.IsNullOrWhiteSpace(roomName) ? $"{sm.Nickname}의 방" : roomName, maxPlayers, isPrivate);
            y += 56 * scale + 36 * scale;

            GUI.Label(new Rect(x, y, w, 34 * scale), "코드로 참가", header);
            y += 40 * scale;
            joinCode = SessionManager.SanitizeCode(GUI.TextField(new Rect(x, y, w - 150 * scale, h), joinCode, 12, field));
            bool canJoin = joinCode.Trim().Length > 0;
            bool prev = GUI.enabled;
            GUI.enabled = prev && canJoin;
            if (GUI.Button(new Rect(x + w - 140 * scale, y, 140 * scale, h), "참가", button))
                _ = sm.JoinByCodeAsync(joinCode);
            GUI.enabled = prev;
        }

        void DrawRoomList(SessionManager sm, Rect r)
        {
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            float x = r.x + 24 * scale, y = r.y + 20 * scale, w = r.width - 48 * scale;

            GUI.Label(new Rect(x, y, w, 34 * scale), "공개 방 목록", header);
            if (GUI.Button(new Rect(r.xMax - 150 * scale, y, 126 * scale, 38 * scale), "새로고침", button))
                _ = sm.RefreshRoomsAsync();
            y += 56 * scale;

            var listRect = new Rect(x, y, w, r.yMax - y - 20 * scale);
            float rowH = 58 * scale;
            var content = new Rect(0, 0, w - 20 * scale, Mathf.Max(listRect.height, sm.Rooms.Count * rowH));
            listScroll = GUI.BeginScrollView(listRect, listScroll, content);
            if (sm.Rooms.Count == 0)
                GUI.Label(new Rect(0, 0, content.width, 40 * scale), "<color=#BBBBBB>열린 방이 없습니다. 방을 만들어 보세요!</color>", small);

            for (int i = 0; i < sm.Rooms.Count; i++)
            {
                var room = sm.Rooms[i];
                var row = new Rect(0, i * rowH, content.width, rowH - 8 * scale);
                Fill(row, new Color(1f, 1f, 1f, 0.06f));
                int players = room.MaxPlayers - room.AvailableSlots;
                GUI.Label(new Rect(row.x + 12 * scale, row.y + 4 * scale, row.width - 160 * scale, 26 * scale), room.Name, label);
                GUI.Label(new Rect(row.x + 12 * scale, row.y + 26 * scale, row.width - 160 * scale, 22 * scale),
                    $"<color=#BBBBBB>{players} / {room.MaxPlayers}명{(room.IsLocked ? " · 잠김" : "")}</color>", small);

                bool prev = GUI.enabled;
                GUI.enabled = prev && room.AvailableSlots > 0 && !room.IsLocked;
                if (GUI.Button(new Rect(row.xMax - 130 * scale, row.y + 7 * scale, 118 * scale, row.height - 14 * scale), "참가", button))
                    _ = sm.JoinByIdAsync(room.Id);
                GUI.enabled = prev;
            }
            GUI.EndScrollView();
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
