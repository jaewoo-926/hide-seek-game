using UnityEngine;

namespace Busted.Map.EditorTools
{
    public enum FloorLayout
    {
        [Tooltip("층을 위로 쌓음 (실제 건물처럼). 쿼터뷰에선 위층 숨김 처리 필요")]
        Stacked,
        [Tooltip("층을 옆으로 나란히 배치. 엘리베이터/계단을 텔레포트로 처리할 때 편함")]
        SideBySide
    }

    /// <summary>맵 빌더 설정. 프리팹/머티리얼을 여기 넣어두면 재생성할 때마다 그대로 적용된다.</summary>
    [CreateAssetMenu(menuName = "Busted/Map Build Settings", fileName = "BustedMapBuildSettings")]
    public class BustedMapBuildSettings : ScriptableObject
    {
        [Header("데이터")]
        public TextAsset mapJson;

        [Header("치수")]
        [Tooltip("설계도 전체 스케일. 1 = 건물 가로 60m. 맵이 너무 크면 0.8 정도로 줄여보세요")]
        public float mapScale = 1f;
        public float wallHeight = 3f;
        [Tooltip("층 간 높이 (Stacked) ")]
        public float floorHeight = 4f;
        public float counterHeight = 1.1f;
        public FloorLayout layout = FloorLayout.Stacked;
        [Tooltip("SideBySide일 때 층 사이 간격(m)")]
        public float sideBySideGap = 15f;

        [Header("생성 옵션")]
        public bool generateRoomFloorTint = true;
        public bool generateRoomZones = true;
        public bool generateMarkers = true;
        public bool generateDoorMarkers = true;
        [Tooltip("벽에 지정할 레이어 이름 (시야 차단 Raycast용). 비우면 Default")]
        public string wallLayerName = "";

        [Header("에셋 교체 (비워두면 그레이박스 큐브)")]
        [Tooltip("모듈형 벽 프리팹. 길이 방향 = 프리팹의 X축이라고 가정하고 자동으로 늘려/반복 배치")]
        public GameObject wallPrefab;
        [Tooltip("벽 프리팹 한 칸 길이(m). 긴 벽은 이 길이 기준으로 여러 개로 나눠 배치")]
        public float wallSegmentLength = 4f;
        [Tooltip("문 프리팹(문틀/문). 폭 방향 = 프리팹 X축 가정, 문 폭에 맞게 스케일")]
        public GameObject doorPrefab;
        public bool scaleDoorPrefabToFit = true;
        [Tooltip("안내 창구 카운터 프리팹 (1층)")]
        public GameObject counterPrefab;

        [Header("머티리얼 (비우면 자동 생성)")]
        public Material wallMaterial;
        public Material floorMaterial;
        public Material counterMaterial;
    }
}
