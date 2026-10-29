using System.Collections.Generic;
using UnityEngine;

namespace Chase.Online
{
    // 온라인 씬의 맵 데이터 (씬 빌더가 채움). 모든 클라이언트가 같은 값을 가진다.
    public class OnlineMapData : MonoBehaviour
    {
        public Vector3 thiefSpawn;
        public Vector3 copWaitPoint;
        public Transform mapCenter;
        public List<Vector3> copSpawnPoints = new List<Vector3>();
        public List<Vector3> prisonSpots = new List<Vector3>();
        public List<Vector3> itemSpots = new List<Vector3>();
    }
}
