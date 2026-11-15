using UnityEngine;

namespace Busted.Map
{
    /// <summary>
    /// 방 하나의 디자인. Room_xxx 오브젝트에 붙어 있다.
    ///  - 벽 디자인(프리팹/모델): 메쉬 모양. 이 방 안쪽을 바라보는 모든 벽면(문 달린 벽 포함)에 적용
    ///  - 벽 머티리얼: 비우면 디자인의 원래 머티리얼
    ///  - 바닥 디자인 / 바닥 머티리얼: 이 방 바닥 타일
    /// 층 루트(1F_Map 등)에 붙은 것은 exterior = true → 외벽 바깥면 + 층 전체 기본값 담당.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomStyle : MonoBehaviour
    {
        [Tooltip("벽 모양으로 쓸 프리팹/모델 (비우면 층 기본 디자인)")]
        public GameObject wallDesign;
        [Tooltip("벽 머티리얼 (비우면 디자인 원래 머티리얼)")]
        public Material wallMaterial;
        [Tooltip("벽 디자인 앞뒤가 반대로 보이면 체크")]
        public bool flipWallDesign;
        [Tooltip("바닥 모양으로 쓸 프리팹/모델 (비우면 층 기본 바닥)")]
        public GameObject floorDesign;
        [Tooltip("바닥 머티리얼 (비우면 디자인 원래 머티리얼)")]
        public Material floorMaterial;

        [HideInInspector] public bool exterior;

        // ── 층 루트 전용: 층 전체 기본값 ──
        [Tooltip("층 루트 전용: 방에 벽 디자인을 따로 안 넣었을 때 쓰는 내부 벽 디자인")]
        public GameObject defaultInteriorWall;
        [Tooltip("층 루트 전용: 방에 바닥 디자인을 따로 안 넣었을 때 쓰는 바닥 디자인")]
        public GameObject defaultFloor;
    }
}
