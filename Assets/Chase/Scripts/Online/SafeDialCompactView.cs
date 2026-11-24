using UnityEngine;

namespace Chase.Online
{
    // R: 라쿤 옆에 다이얼·램프·짧은 안내만 (게임 화면을 가리지 않음). 회의 후 하나만 남긴다.
    public class SafeDialCompactView : SafeDialViewBase
    {
        public Vector3 worldOffset = new Vector3(0f, 1.2f, 0f);
        public Vector2 screenOffset = new Vector2(150f, 0f);

        protected override void LateUpdate()
        {
            base.LateUpdate();
            var cam = Camera.main;
            if (!panel || !follow || !cam) return;

            // 라쿤의 3D 위치가 화면 어디에 보이는지 구해서 그 옆에 패널을 둔다
            Vector3 sp = cam.WorldToScreenPoint(follow.position + worldOffset);
            var canvas = panel.GetComponentInParent<Canvas>();
            float scale = canvas ? canvas.scaleFactor : 1f;
            Vector2 half = panel.rect.size * scale * 0.5f;
            Vector2 pos = new Vector2(sp.x, sp.y) + screenOffset * scale;
            pos.x = Mathf.Clamp(pos.x, half.x + 8f, Screen.width - half.x - 8f);
            pos.y = Mathf.Clamp(pos.y, half.y + 8f, Screen.height - half.y - 8f);
            panel.position = pos;
        }
    }
}
