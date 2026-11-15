using UnityEngine;

namespace Busted.Map
{
    /// <summary>
    /// 벽 한 칸(2.5m) 그룹. 씬 뷰에서 클릭하면 안/바깥면이 아닌 이 그룹이 선택되어
    /// 한 번에 이동·회전·삭제할 수 있다. (Ctrl 드래그 + 스냅 2.5 권장)
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class WallSegment : MonoBehaviour
    {
        public bool isDoor;
        public bool vertical;
        public int gridK, gridJ;
    }
}
