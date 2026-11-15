using UnityEngine;

namespace Busted.Map
{
    /// <summary>문 하나(문틀 + 문짝). 벽에는 이 문 크기만큼 구멍이 뚫려 있다.</summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class DoorUnit : MonoBehaviour
    {
        public bool vertical;
        public int line;          // 가로벽이면 j, 세로벽이면 k
        public float center;      // 층 기준 벽 방향 좌표(m)
        public int pairs = 1;     // 양문 세트 수 (1 = 약 2m, 2 = 약 4m)
        public float width, height;
    }
}
