using UnityEngine;
using UnityEngine.UI;

namespace Chase.Online
{
    // 화면이 그릴 때 필요한 값 (판정 + 서버 진행 단계를 컨트롤러가 모아서 넘긴다)
    public struct SafeDialState
    {
        public int Tick;              // 화살표 아래 눈금
        public int Step;              // 서버가 인정한 진행 단계 = 초록 램프 수
        public DialDirection Direction;
        public bool InZone;           // 띡 구간 안
        public bool Locked;           // 실패 후 잠금 중
        public bool ShowZoneHint;     // F10 빛 힌트 (회의 비교용)
        public bool Awaiting;         // 확정 요청을 보내고 서버 응답을 기다리는 중
    }

    // 화면 두 종류(전체 창 / 작은 다이얼)가 지키는 약속. 디자인 담당이 화면을 바꿀 때도 이것만 맞추면 된다.
    public interface ISafeDialView
    {
        void Show(Transform follow);
        void Hide();
        void Render(in SafeDialState s);
        void PlayZoneShake();
        void PlayFail();
        void PlaySuccess(int money);
        bool CloseButtonPressed { get; }
    }

    // 두 화면의 공통 동작: 다이얼 회전, 숫자, 방향, 램프, 떨림, 실패 깜빡임, 성공 표시
    public abstract class SafeDialViewBase : MonoBehaviour, ISafeDialView
    {
        public static readonly Color LampDone = new Color32(0x57, 0xE3, 0x9A, 0xFF);
        public static readonly Color LampHint = new Color32(0xFF, 0xD3, 0x4D, 0xFF);
        public static readonly Color LampOff = new Color32(0x4A, 0x1C, 0x1C, 0xFF);

        [Header("공통 부품")]
        public RectTransform panel;
        public RectTransform face;        // 숫자판 (회전)
        public Image faceImage;
        public Image hintRing;            // 빛 힌트 켜졌을 때 금색 테두리
        public Text tickText;
        public Text directionIcon;
        public Text directionText;
        public Image[] lamps = new Image[3];
        public GameObject successRoot;
        public Text successMoney;
        public Button closeButton;
        public float shakePixels = 4f;

        protected Transform follow;
        float shakeUntil, failUntil;
        Vector2 faceBase;
        bool closePressed;
        Color tickColor = Color.white;

        public bool CloseButtonPressed
        {
            get { bool v = closePressed; closePressed = false; return v; }
        }

        protected virtual void Awake()
        {
            if (face) faceBase = face.anchoredPosition;
            if (tickText) tickColor = tickText.color;
            if (closeButton) closeButton.onClick.AddListener(() => closePressed = true);
            ApplyKoreanFont();
        }

        // 임시 UI: 프로젝트에 한글 폰트가 없어서 윈도우 기본 폰트를 쓴다 (디자인 담당이 교체)
        void ApplyKoreanFont()
        {
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 32);
            foreach (var t in GetComponentsInChildren<Text>(true)) t.font = font;
        }

        public virtual void Show(Transform followTarget)
        {
            follow = followTarget;
            closePressed = false;
            if (successRoot) successRoot.SetActive(false);
            gameObject.SetActive(true);
        }

        public virtual void Hide() => gameObject.SetActive(false);

        public virtual void Render(in SafeDialState s)
        {
            if (face) face.localEulerAngles = new Vector3(0f, 0f, s.Tick * (360f / SafeDial.Ticks));
            if (tickText) tickText.text = s.Tick.ToString("00");
            bool right = s.Direction == DialDirection.Right;
            if (directionIcon) directionIcon.text = right ? "↻" : "↺";
            if (directionText) directionText.text = right ? "오른쪽" : "왼쪽";
            for (int i = 0; i < lamps.Length; i++)
            {
                if (!lamps[i]) continue;
                lamps[i].color = i < s.Step ? LampDone
                    : i == s.Step && s.ShowZoneHint && s.InZone ? LampHint
                    : LampOff;
            }
            if (hintRing) hintRing.enabled = s.ShowZoneHint && s.InZone;
            if (faceImage) faceImage.color = s.Locked || s.Awaiting ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
        }

        public void PlayZoneShake() => shakeUntil = Time.unscaledTime + 0.12f;
        public void PlayFail() => failUntil = Time.unscaledTime + 0.35f;

        public virtual void PlaySuccess(int money)
        {
            if (successMoney) successMoney.text = $"+${money:N0}";
            if (successRoot) successRoot.SetActive(true);
        }

        protected virtual void LateUpdate()
        {
            if (face)
            {
                Vector2 offset = Time.unscaledTime < shakeUntil ? Random.insideUnitCircle * shakePixels : Vector2.zero;
                face.anchoredPosition = faceBase + offset;
            }
            if (tickText) tickText.color = Time.unscaledTime < failUntil ? new Color(1f, 0.35f, 0.35f) : tickColor;
        }
    }

}
