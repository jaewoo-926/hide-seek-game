using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Chase.Online
{
    // 내 시야(앞쪽 부채꼴 + 주변 원) 밖을 어둡게 덮는 화면 효과.
    // 방향별로 레이를 쏴서 벽에 막히는 거리를 셰이더에 넘기므로, 상대가 보이는 범위와 화면 경계가 같다.
    [RequireComponent(typeof(Camera))]
    public class VisionOverlay : MonoBehaviour
    {
        const int RayCount = 256; // VisionOverlay.shader 의 RAY_COUNT 와 같아야 한다
        const float EyeHeight = 1.5f; // OnlineGame.IsVisibleToLocal 의 눈높이와 동일
        const float WallBias = 0.35f; // 벽 표면까지 밝게 보이도록 조금 더 넓힌다
        const float BodyRadius = 0.75f; // 내 캐릭터가 항상 밝게 보이는 반경

        public Color outsideColor = new Color(0.18f, 0.2f, 0.27f);
        public float edgeSoftness = 0.12f;

        static readonly int DistId = Shader.PropertyToID("_VisionDist");
        static readonly int OriginId = Shader.PropertyToID("_VisionOrigin");
        static readonly int OutsideId = Shader.PropertyToID("_VisionOutside");
        static readonly int EdgeId = Shader.PropertyToID("_VisionEdge");
        static readonly int BiasId = Shader.PropertyToID("_VisionBias");
        static readonly int BodyId = Shader.PropertyToID("_VisionBody");
        static readonly int YawId = Shader.PropertyToID("_VisionYaw");
        static readonly int HalfId = Shader.PropertyToID("_VisionHalf");

        readonly float[] distances = new float[RayCount];
        Material material;
        Mesh mesh;
        MeshRenderer overlay;

        void Awake()
        {
            var shader = Resources.Load<Shader>("VisionOverlay");
            if (shader == null)
            {
                Debug.LogError("[Chase] VisionOverlay 셰이더를 찾을 수 없음");
                enabled = false;
                return;
            }
            material = new Material(shader);
            GetComponent<Camera>().GetUniversalAdditionalCameraData().requiresDepthOption = CameraOverrideOption.On;

            mesh = new Mesh { name = "Vision Overlay" };
            mesh.vertices = new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(1, 1), new Vector3(-1, 1) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f); // 절대 컬링되지 않게

            var go = new GameObject("Vision Overlay");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            overlay = go.AddComponent<MeshRenderer>();
            overlay.sharedMaterial = material;
            overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            overlay.enabled = false;
        }

        void OnDestroy()
        {
            if (material) Destroy(material);
            if (mesh) Destroy(mesh);
        }

        void LateUpdate()
        {
            var me = OnlinePlayer.Local;
            var game = OnlineGame.I;
            bool active = me != null && game != null && game.IsPlaying && UsesVision(me, game);
            overlay.enabled = active;
            if (!active) return;

            Vector3 origin = me.transform.position;
            float yaw = me.transform.eulerAngles.y;
            CastDistances(origin + Vector3.up * EyeHeight, yaw, game);

            material.SetFloatArray(DistId, distances);
            material.SetVector(OriginId, origin);
            material.SetColor(OutsideId, outsideColor);
            material.SetFloat(EdgeId, edgeSoftness);
            material.SetFloat(BiasId, WallBias);
            material.SetFloat(BodyId, BodyRadius);
            material.SetFloat(YawId, Mathf.Repeat(yaw, 360f) / 360f);
            material.SetFloat(HalfId, game.viewAngle * 0.5f / 360f);
        }

        // OnlineGame.IsVisibleToLocal 에서 시야 제한을 받는 경우와 맞춘다
        static bool UsesVision(OnlinePlayer me, OnlineGame game)
        {
            if (me.State == PState.Waiting || me.State == PState.Eliminated || me.State == PState.Escaped) return false;
            return !(me.IsCop && game.Phase == OnlinePhase.LastTime);
        }

        void CastDistances(Vector3 eye, float yaw, OnlineGame game)
        {
            float halfAngle = game.viewAngle * 0.5f;
            for (int i = 0; i < RayCount; i++)
            {
                float angle = i * 360f / RayCount;
                bool inCone = Mathf.Abs(Mathf.DeltaAngle(yaw, angle)) <= halfAngle;
                float max = inCone ? game.viewRange : game.nearRange;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                distances[i] = Mathf.Min(max, CastPastPlayers(eye, dir, max) + WallBias);
            }
        }

        // 다른 플레이어 몸은 시야를 막지 않는다
        static float CastPastPlayers(Vector3 eye, Vector3 dir, float max)
        {
            float travelled = 0f;
            while (travelled < max &&
                   Physics.Raycast(eye + dir * travelled, dir, out var hit, max - travelled, ~0, QueryTriggerInteraction.Ignore))
            {
                travelled += hit.distance;
                if (!(hit.collider is CharacterController)) return travelled;
                travelled += 0.9f; // 플레이어 몸 지름만큼 건너뛴다
            }
            return max;
        }
    }
}
