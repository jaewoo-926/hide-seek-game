using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chase.Online
{
    public enum PState { Free, Hidden, Jailed, Eliminated, Escaped, Waiting }

    // 인게임 플레이어 (도둑/경찰 공용)
    // - 이동: 소유자가 직접 (NetworkTransform Owner 권한) → 반응성 확보
    // - 상태/판정: 서버가 NetworkVariable 로 관리, 행동은 Rpc 로 서버에 요청
    [RequireComponent(typeof(CharacterController))]
    public class OnlinePlayer : NetworkBehaviour
    {
        public static readonly List<OnlinePlayer> All = new List<OnlinePlayer>();
        public static OnlinePlayer Local { get; private set; }

        [Header("이동")]
        public float thiefSpeed = 5f;
        public float copSpeed = 5.6f;
        public float buffMultiplier = 1.6f;
        public float slowMultiplier = 0.5f;
        public float lastTimeCopMultiplier = 1.3f;
        public float turnSharpness = 18f;

        [Header("비주얼")]
        public Renderer bodyRenderer;
        public GameObject thiefParts;
        public GameObject copParts;
        public GameObject stunFx;
        public Light visionLight;
        public Light auraLight;

        // ---- 서버가 쓰는 동기화 상태 ----
        public readonly NetworkVariable<int> RoleValue = new NetworkVariable<int>();
        public readonly NetworkVariable<FixedString64Bytes> Nickname = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<int> ColorIndex = new NetworkVariable<int>();
        public readonly NetworkVariable<int> StateValue = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Money = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Drinks = new NetworkVariable<int>();
        public readonly NetworkVariable<int> HideIndex = new NetworkVariable<int>(-1);
        public readonly NetworkVariable<bool> HasCuffs = new NetworkVariable<bool>(true);
        public readonly NetworkVariable<double> BuffUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> SlowUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> StunUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> ClubReadyAt = new NetworkVariable<double>();

        // ---- 소유자가 쓰는 값: 경찰 대기 중 고른 스폰 위치 ----
        public readonly NetworkVariable<int> ChosenSpawn = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // ---- 서버 전용 ----
        [System.NonSerialized] public ulong jailedBy = ulong.MaxValue;
        [System.NonSerialized] public double hideEnteredAt;
        [System.NonSerialized] public double batonReadyAt;

        public Role Role => (Role)RoleValue.Value;
        public PState State => (PState)StateValue.Value;
        public bool IsThief => Role == Role.Thief;
        public bool IsCop => Role == Role.Cop;
        public bool IsActiveThief => IsThief && (State == PState.Free || State == PState.Hidden);
        public string DisplayName => Nickname.Value.IsEmpty ? "..." : Nickname.Value.ToString();

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        public bool IsStunned => Now < StunUntil.Value;
        public bool IsBuffed => Now < BuffUntil.Value;
        public bool IsSlowed => Now < SlowUntil.Value;
        public float BuffRemaining => (float)System.Math.Max(0, BuffUntil.Value - Now);
        public float StunRemaining => (float)System.Math.Max(0, StunUntil.Value - Now);
        public float ClubCooldown => (float)System.Math.Max(0, ClubReadyAt.Value - Now);

        public float CurrentSpeed
        {
            get
            {
                if (IsStunned) return 0f;
                float s = IsCop ? copSpeed : thiefSpeed;
                if (IsCop && OnlineGame.I && OnlineGame.I.Phase == OnlinePhase.LastTime) s *= lastTimeCopMultiplier;
                if (IsBuffed) s *= buffMultiplier;
                if (IsSlowed) s *= slowMultiplier;
                return s;
            }
        }

        // ---- 소유자 상호작용 상태 (HUD 표시용) ----
        public Interaction Focus { get; private set; }
        public float HoldRatio { get; private set; }
        public bool Holding { get; private set; }

        CharacterController cc;
        NetworkTransform netTransform;
        QuarterViewCamera cam;
        Material bodyMaterial;
        Renderer[] renderers;
        float holdTime;
        Interaction holdTarget;
        float verticalVelocity;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            netTransform = GetComponent<NetworkTransform>();
            renderers = GetComponentsInChildren<Renderer>(true);
            if (bodyRenderer) bodyMaterial = bodyRenderer.material;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            RoleValue.OnValueChanged += (_, __) => ApplyLook();
            ColorIndex.OnValueChanged += (_, __) => ApplyLook();
            ApplyLook();

            cc.enabled = IsOwner;
            if (visionLight) visionLight.enabled = IsOwner;
            if (auraLight) auraLight.enabled = IsOwner;

            if (IsOwner)
            {
                Local = this;
                cam = FindAnyObjectByType<QuarterViewCamera>();
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            if (bodyMaterial) Destroy(bodyMaterial);
            base.OnDestroy();
        }

        void ApplyLook()
        {
            if (thiefParts) thiefParts.SetActive(IsThief);
            if (copParts) copParts.SetActive(IsCop);
            if (bodyMaterial)
            {
                var c = IsCop ? new Color(0.18f, 0.36f, 0.83f)
                    : Chase.Net.LobbyPlayer.Palette[Mathf.Max(0, ColorIndex.Value) % Chase.Net.LobbyPlayer.Palette.Length];
                bodyMaterial.SetColor("_BaseColor", c);
            }
        }

        public void SetRendered(bool visible)
        {
            foreach (var r in renderers)
                if (r) r.enabled = visible;
            if (stunFx) stunFx.SetActive(visible && IsStunned);
        }

        // 서버 → 소유자: 위치 강제 이동 (감옥 이송, 경찰 진입 등)
        public void ServerTeleport(Vector3 position) => TeleportRpc(position);

        [Rpc(SendTo.Owner)]
        void TeleportRpc(Vector3 position)
        {
            cc.enabled = false;
            if (netTransform) netTransform.Teleport(position, transform.rotation, transform.localScale);
            else transform.position = position;
            cc.enabled = true;
            verticalVelocity = 0f;
        }

        // ================= 소유자 입력 =================

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var game = OnlineGame.I;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (game == null || kb == null || mouse == null) return;

            UpdateCameraTarget(game);
            if (!game.IsPlaying)
            {
                CancelHold();
                return;
            }

            switch (State)
            {
                case PState.Waiting:
                    for (int i = 0; i < game.CopSpawnCount && i < 9; i++)
                        if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) ChosenSpawn.Value = i;
                    return;
                case PState.Hidden:
                    Focus = game.FindInteraction(this);
                    if (kb.eKey.wasPressedThisFrame && Focus.IsValid) InteractRpc((int)Focus.kind, Focus.index);
                    return;
                case PState.Free:
                case PState.Jailed:
                    Move(kb);
                    FaceMouse(mouse);
                    break;
                default:
                    Focus = default;
                    return;
            }

            if (State != PState.Free)
            {
                CancelHold();
                Focus = default;
                return;
            }

            if (IsThief)
            {
                if (kb.fKey.wasPressedThisFrame) DrinkRpc();
                if (mouse.leftButton.wasPressedThisFrame) ClubRpc();
            }
            else if (mouse.leftButton.wasPressedThisFrame)
            {
                CatchRpc();
            }

            UpdateInteraction(game, kb);
        }

        void UpdateCameraTarget(OnlineGame game)
        {
            if (cam == null) return;
            Transform target = transform;
            if (State == PState.Waiting) target = game.MapCenter;
            else if (State == PState.Eliminated || State == PState.Escaped)
            {
                foreach (var p in All)
                    if (p.Role == Role && (p.State == PState.Free || p.State == PState.Hidden)) { target = p.transform; break; }
            }
            cam.target = target;
        }

        void Move(Keyboard kb)
        {
            Vector2 input = Vector2.zero;
            if (kb.wKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed) input.x -= 1f;

            var yawRot = Quaternion.Euler(0f, cam ? cam.yaw : 45f, 0f);
            Vector3 move = yawRot * new Vector3(input.x, 0f, input.y);
            if (move.sqrMagnitude > 1f) move.Normalize();
            if (Holding) move = Vector3.zero;

            verticalVelocity = cc.isGrounded ? -1f : verticalVelocity - 20f * Time.deltaTime;
            cc.Move((move * CurrentSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);
        }

        void FaceMouse(Mouse mouse)
        {
            if (cam == null || IsStunned) return;
            Ray ray = cam.Cam.ScreenPointToRay(mouse.position.ReadValue());
            var plane = new Plane(Vector3.up, transform.position);
            if (!plane.Raycast(ray, out float enter)) return;
            Vector3 dir = ray.GetPoint(enter) - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir),
                1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
        }

        void UpdateInteraction(OnlineGame game, Keyboard kb)
        {
            if (Holding)
            {
                var current = game.FindInteraction(this);
                if (!kb.eKey.isPressed || !current.SameTarget(holdTarget))
                {
                    CancelHold();
                    return;
                }
                holdTime += Time.deltaTime;
                HoldRatio = holdTime / holdTarget.holdTime;
                if (holdTime >= holdTarget.holdTime)
                {
                    InteractRpc((int)holdTarget.kind, holdTarget.index);
                    CancelHold();
                }
                return;
            }

            Focus = game.FindInteraction(this);
            if (!Focus.IsValid || !kb.eKey.wasPressedThisFrame) return;
            if (Focus.holdTime <= 0f)
            {
                InteractRpc((int)Focus.kind, Focus.index);
            }
            else
            {
                Holding = true;
                holdTarget = Focus;
                holdTime = 0f;
                HoldRatio = 0f;
            }
        }

        void CancelHold()
        {
            Holding = false;
            holdTime = 0f;
            HoldRatio = 0f;
        }

        // ================= 서버 요청 =================

        [Rpc(SendTo.Server)]
        void InteractRpc(int kind, int index) => OnlineGame.I.ServerInteract(this, (InteractKind)kind, index);

        [Rpc(SendTo.Server)]
        void ClubRpc() => OnlineGame.I.ServerClub(this);

        [Rpc(SendTo.Server)]
        void CatchRpc() => OnlineGame.I.ServerCatch(this);

        [Rpc(SendTo.Server)]
        void DrinkRpc() => OnlineGame.I.ServerDrink(this);
    }
}
