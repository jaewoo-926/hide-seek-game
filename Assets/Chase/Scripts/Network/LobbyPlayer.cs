using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chase.Net
{
    // 대기방 플레이어: 소유자가 직접 움직이고(NetworkTransform Owner 권한), 닉네임/색상은 NetworkVariable 로 동기화
    [RequireComponent(typeof(CharacterController))]
    public class LobbyPlayer : NetworkBehaviour
    {
        public static readonly List<LobbyPlayer> All = new List<LobbyPlayer>();

        public static readonly Color[] Palette =
        {
            new Color(1f, 0.54f, 0.24f), new Color(0.88f, 0.35f, 0.35f), new Color(0.3f, 0.6f, 1f),
            new Color(0.35f, 0.8f, 0.45f), new Color(0.83f, 0.42f, 0.69f), new Color(1f, 0.85f, 0.25f),
            new Color(0.3f, 0.85f, 0.85f), new Color(0.6f, 0.45f, 0.95f), new Color(0.95f, 0.95f, 0.95f),
            new Color(0.45f, 0.35f, 0.25f),
        };

        public float moveSpeed = 5f;
        public float turnSharpness = 14f;
        public Renderer bodyRenderer;

        public readonly NetworkVariable<FixedString64Bytes> Nickname = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<int> ColorIndex = new NetworkVariable<int>(-1);

        // 0 = 도둑, 1 = 경찰 (본인이 선택)
        public readonly NetworkVariable<int> Role = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public GameObject thiefParts;
        public GameObject copParts;

        public bool IsCop => Role.Value == 1;

        public bool IsHostPlayer => OwnerClientId == NetworkManager.ServerClientId;
        public string DisplayName => Nickname.Value.IsEmpty ? "..." : Nickname.Value.ToString();

        CharacterController cc;
        Material bodyMaterial;
        float verticalVelocity;
        QuarterViewCamera cam;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (bodyRenderer) bodyMaterial = bodyRenderer.material;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            ColorIndex.OnValueChanged += OnColorChanged;
            Role.OnValueChanged += OnRoleChanged;
            ApplyColor(ColorIndex.Value);
            ApplyRole();

            // 다른 사람 캐릭터는 NetworkTransform 이 위치를 맞추므로 물리 이동 비활성화
            cc.enabled = IsOwner;

            if (IsOwner)
            {
                Nickname.Value = new FixedString64Bytes(SessionManager.I ? SessionManager.I.Nickname : "Player");
                Role.Value = SessionManager.I ? SessionManager.I.PreferredRole : 0;
                cam = FindAnyObjectByType<QuarterViewCamera>();
                if (cam) cam.target = transform;
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            ColorIndex.OnValueChanged -= OnColorChanged;
            Role.OnValueChanged -= OnRoleChanged;
        }

        // 소유자가 팀 변경
        public void SetRole(int role)
        {
            if (!IsOwner) return;
            Role.Value = role;
            if (SessionManager.I) SessionManager.I.PreferredRole = role;
        }

        void OnRoleChanged(int previous, int current) => ApplyRole();

        void ApplyRole()
        {
            if (thiefParts) thiefParts.SetActive(!IsCop);
            if (copParts) copParts.SetActive(IsCop);
        }

        void OnColorChanged(int previous, int current) => ApplyColor(current);

        void ApplyColor(int index)
        {
            if (bodyMaterial == null || index < 0) return;
            bodyMaterial.SetColor("_BaseColor", Palette[index % Palette.Length]);
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            Vector2 input = Vector2.zero;
            if (kb.wKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed) input.x -= 1f;

            var yawRot = Quaternion.Euler(0f, cam ? cam.yaw : 45f, 0f);
            Vector3 move = yawRot * new Vector3(input.x, 0f, input.y);
            if (move.sqrMagnitude > 1f) move.Normalize();

            verticalVelocity = cc.isGrounded ? -1f : verticalVelocity - 20f * Time.deltaTime;
            cc.Move((move * moveSpeed + Vector3.up * verticalVelocity) * Time.deltaTime);

            if (move.sqrMagnitude > 0.01f)
            {
                var target = Quaternion.LookRotation(move);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
            }
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            if (bodyMaterial) Destroy(bodyMaterial);
            base.OnDestroy();
        }
    }
}
