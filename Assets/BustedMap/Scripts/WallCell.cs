using UnityEngine;

namespace Busted.Map
{
    /// <summary>벽 한 칸(2.5m)의 문 구멍 정보. openings = 벽 로컬 X 구간들(x0,x1), 높이 openingHeight.</summary>
    [DisallowMultipleComponent]
    public class WallCell : MonoBehaviour
    {
        public Vector2[] openings = new Vector2[0];
        public float openingHeight = 2.4f;
    }
}
