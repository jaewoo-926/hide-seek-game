using UnityEngine;

namespace Busted.Map
{
    /// <summary>벽에 끼워 넣는 오브젝트(엘리베이터 등). 앞면이 닿는 벽 줄에서 이 오브젝트 폭만큼 벽을 비운다.</summary>
    [DisallowMultipleComponent]
    public class WallOpening : MonoBehaviour
    {
        [Tooltip("벽을 비우는 높이 (3 = 벽 전체 높이)")]
        public float height = 3f;
        [Tooltip("체크하면 오브젝트 가운데를 벽 줄로 사용 (벽 속에 박히는 문틀 등). 끄면 앞면 기준 (엘리베이터)")]
        public bool alignCenter;
    }
}
