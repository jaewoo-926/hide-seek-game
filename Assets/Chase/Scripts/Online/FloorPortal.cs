using UnityEngine;

namespace Chase.Online
{
    // 층 이동 포탈. 앞에서 E 를 누르면 destination 포탈의 도착 지점으로 순간이동한다.
    // 위치만 들고 있는 표시용 오브젝트이고, 판정과 이동은 OnlineGame 이 서버에서 처리한다.
    public class FloorPortal : MonoBehaviour
    {
        public FloorPortal destination;
        public Transform arrival;   // 이 포탈로 들어올 때 도착하는 자리 (포탈 바로 위가 아니라 옆)
        public string prompt = "다른 층으로 이동";

        public Vector3 ArrivalPoint => arrival ? arrival.position : transform.position;
    }
}
