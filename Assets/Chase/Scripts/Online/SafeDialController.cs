using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Chase.Online
{
    // 금고 다이얼 연결부 (내 플레이어에만 붙는다).
    // 입력 → 판정(SafeDial) → 화면(ISafeDialView), 열기·확정·닫기는 서버에 요청.
    // 창이 열려 있는지는 서버 값(OnlinePlayer.DialSafe)을 기준으로 한다.
    public class SafeDialController : MonoBehaviour
    {
        public static bool ShowZoneHint;          // F10 빛 힌트 (회의 비교용, 나중에 삭제)
        const float DragTicksPerPixel = 0.15f;    // 마우스 드래그 1픽셀 = 0.15칸
        const float ClickMaxDrag = 4f;            // 이보다 덜 움직이고 떼면 클릭(확정)
        const float AwaitTimeout = 1f;            // 서버 응답을 기다리는 최대 시간

        OnlinePlayer me;
        SafeDial dial;
        int safe = -1;
        bool compactRequested;
        ISafeDialView view;
        SafeDialViewBase fullView, compactView;
        bool awaiting;
        float awaitingUntil;
        float successCloseAt = -1f;
        bool dragging, dragBlocked;
        float dragMoved;
        int lastTick;
        AudioSource audioSource;

        public bool IsOpen => dial != null || successCloseAt > 0f;

        public void Init(OnlinePlayer owner)
        {
            me = owner;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        OnlineGame subscribed;

        void OnDestroy()
        {
            if (subscribed) subscribed.SafeOpenedLocal -= OnSafeOpened;
            if (fullView) Destroy(fullView.gameObject);
            if (compactView) Destroy(compactView.gameObject);
        }

        // E = 전체 창, R = 작은 다이얼. 서버가 허락하면(DialSafe 가 바뀌면) 그때 화면이 열린다.
        public void Open(int safeIndex, bool compact)
        {
            if (IsOpen || me == null) return;
            compactRequested = compact;
            me.RequestOpenSafe(safeIndex);
        }

        void Update()
        {
            var game = OnlineGame.I;
            var kb = Keyboard.current;
            if (game == null || me == null || kb == null) return;
            if (subscribed != game)
            {
                if (subscribed) subscribed.SafeOpenedLocal -= OnSafeOpened;
                game.SafeOpenedLocal += OnSafeOpened;
                subscribed = game;
            }

            if (kb.f10Key.wasPressedThisFrame)
            {
                ShowZoneHint = !ShowZoneHint;
                game.messages.Add((ShowZoneHint ? "금고 빛 힌트 켬 (F10)" : "금고 빛 힌트 끔 (F10)", Time.time));
            }

            if (successCloseAt > 0f)
            {
                if (Time.time >= successCloseAt) CloseView();
                return;
            }

            int serverSafe = me.DialSafe.Value;
            if (dial == null)
            {
                if (serverSafe >= 0) OpenView(game, serverSafe);
                return;
            }
            if (serverSafe != safe)
            {
                // 서버가 닫았다: 다 열어서 닫힌 거면 성공 표시, 아니면 (곤봉·기절·체포 등) 그냥 닫기
                if (game.IsSafeRobbed(safe) || game.SafeProgress(safe) >= SafeDial.Steps) StartSuccess(game.SafeMoney(safe));
                else CloseView();
                return;
            }

            // 서버가 인정한 단계를 판정에 반영
            int progress = game.SafeProgress(safe);
            if (progress != dial.Step)
            {
                dial.SetStep(progress);
                awaiting = false;
            }
            if (awaiting && Time.time > awaitingUntil) awaiting = false;

            HandleInput(kb, Mouse.current);
            Render();
        }

        void HandleInput(Keyboard kb, Mouse mouse)
        {
            double now = Time.timeAsDouble;

            if (kb.escapeKey.wasPressedThisFrame || view.CloseButtonPressed)
            {
                me.RequestCloseSafe();
                CloseView();
                return;
            }

            float axis = 0f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) axis += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) axis -= 1f;
            if (axis != 0f) dial.RotateByInput(axis, Time.deltaTime, now);

            float dragDelta = 0f;
            bool confirm = kb.spaceKey.wasPressedThisFrame;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    dragging = true;
                    dragMoved = 0f;
                    // X 버튼 같은 UI 위에서 누른 건 다이얼 조작이 아니다
                    dragBlocked = EventSystem.current && EventSystem.current.IsPointerOverGameObject();
                }
                if (dragging && !dragBlocked && mouse.leftButton.isPressed)
                {
                    dragDelta = mouse.delta.ReadValue().x;
                    dragMoved += Mathf.Abs(dragDelta);
                    if (dragDelta != 0f) dial.Rotate(-dragDelta * DragTicksPerPixel, now); // 오른쪽으로 끌면 오른쪽
                }
                if (dragging && mouse.leftButton.wasReleasedThisFrame)
                {
                    if (!dragBlocked && dragMoved < ClickMaxDrag) confirm = true;
                    dragging = false;
                }
            }

            me.SetDialTurning((axis != 0f || dragDelta != 0f) && !dial.IsLocked(now));

            if (dial.Tick != lastTick)
            {
                lastTick = dial.Tick;
                Play(ClickClip, 0.2f);
            }
            PlayProximityBeep();

            if (confirm && !awaiting)
            {
                if (dial.Confirm(now) == ConfirmResult.Hit)
                {
                    awaiting = true;
                    awaitingUntil = Time.time + AwaitTimeout;
                    me.RequestConfirmSafe(safe, dial.Step, dial.Tick);
                }
            }
        }

        // 목표에 가까워질수록 삐삐 간격이 짧아진다 (맞는 방향으로 돌리고 있을 때만)
        const int BeepRange = 20;            // 이 칸 수 안에 들어오면 삐삐 시작
        const float BeepSlowest = 0.6f;      // 가장 멀 때 간격(초)
        const float BeepFastest = 0.07f;     // 목표 위에서 간격(초)
        float nextBeepAt;

        void PlayProximityBeep()
        {
            if (!dial.TurningRightWay || dial.DistanceToTarget > BeepRange || Time.time < nextBeepAt) return;
            float far = dial.DistanceToTarget / (float)BeepRange; // 0 = 목표 위, 1 = 범위 끝
            Play(PipClip, Mathf.Lerp(0.8f, 0.35f, far));
            nextBeepAt = Time.time + Mathf.Lerp(BeepFastest, BeepSlowest, far);
        }

        void Render()
        {
            view.Render(new SafeDialState
            {
                Tick = dial.Tick,
                Step = dial.Step,
                Direction = dial.Direction,
                InZone = dial.InZone,
                Locked = dial.IsLocked(Time.timeAsDouble),
                ShowZoneHint = ShowZoneHint,
                Awaiting = awaiting,
            });
        }

        void OpenView(OnlineGame game, int safeIndex)
        {
            safe = safeIndex;
            dial = new SafeDial(game.SafeTargets(safe), game.SafeZone(safe), game.SafeProgress(safe), game.safeDialSpeed, game.safeFailLockSec);
            dial.EnteredZone += () => { view?.PlayZoneShake(); Play(BeepClip, 0.8f); };
            dial.Failed += () => { view?.PlayFail(); Play(FailClip, 0.8f); };
            lastTick = dial.Tick;
            awaiting = false;
            dragging = false;

            EnsureEventSystem();
            view = GetView(game, compactRequested);
            view.Show(me.transform);
            Render();
        }

        void StartSuccess(int money)
        {
            if (successCloseAt > 0f) return;
            if (view == null) view = GetView(OnlineGame.I, compactRequested);
            view.Show(me.transform);
            view.PlaySuccess(money);
            Play(BeepClip, 1f);
            dial = null;
            successCloseAt = Time.time + 1f; // 기획: 1초 뒤 자동으로 닫힘
            me.SetDialTurning(false);
        }

        void OnSafeOpened(int safeIndex, int money)
        {
            if (safeIndex == safe || dial == null) StartSuccess(money);
        }

        void CloseView()
        {
            view?.Hide();
            dial = null;
            safe = -1;
            awaiting = false;
            dragging = false;
            successCloseAt = -1f;
            me.SetDialTurning(false);
        }

        ISafeDialView GetView(OnlineGame game, bool compact)
        {
            if (compact)
            {
                if (!compactView) compactView = Instantiate(game.safeDialCompactPrefab).GetComponent<SafeDialViewBase>();
                if (fullView) fullView.Hide();
                return compactView;
            }
            if (!fullView) fullView = Instantiate(game.safeDialFullPrefab).GetComponent<SafeDialViewBase>();
            if (compactView) compactView.Hide();
            return fullView;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        void Play(AudioClip clip, float volume)
        {
            if (audioSource && clip) audioSource.PlayOneShot(clip, volume);
        }

        // ---------------- 임시 효과음 (사운드 에셋이 생기면 교체) ----------------

        static AudioClip clickClip, beepClip, failClip, pipClip;
        static AudioClip PipClip => pipClip ? pipClip : pipClip = Tone("Dial Pip", 2200f, 0.04f, 0.45f);
        public static AudioClip ClickClip => clickClip ? clickClip : clickClip = Tone("Dial Click", 3000f, 0.015f, 0.35f);
        static AudioClip BeepClip => beepClip ? beepClip : beepClip = Tone("Dial Beep", 1600f, 0.06f, 0.5f);
        static AudioClip FailClip => failClip ? failClip : failClip = Tone("Dial Fail", 220f, 0.2f, 0.6f);

        static AudioClip Tone(string name, float freq, float seconds, float amp)
        {
            const int rate = 44100;
            var data = new float[Mathf.CeilToInt(rate * seconds)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / rate;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * amp * (1f - (float)i / data.Length);
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
