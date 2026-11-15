using UnityEngine;

namespace Busted.Map
{
    /// <summary>
    /// 방(구역) 트리거. 자식 BoxCollider(isTrigger)들이 방 모양을 덮는다.
    /// 용도: "2층 CCTV실에 도둑 발견" 같은 위치 텍스트, CCTV 감시 구역, 스폰 방 판정, 은폐 오브젝트 그룹화 등.
    /// </summary>
    public class RoomZone : MonoBehaviour
    {
        public int floor = 1;
        public string roomName;
        public float area;

        public string DisplayName => $"{floor}층 {roomName}";

        public bool Contains(Vector3 worldPos)
        {
            foreach (var c in GetComponentsInChildren<BoxCollider>())
                if (c.bounds.Contains(worldPos)) return true;
            return false;
        }

        /// <summary>월드 좌표가 속한 방을 찾는다. 없으면 null.</summary>
        public static RoomZone Find(Vector3 worldPos)
        {
            foreach (var z in FindObjectsByType<RoomZone>())
                if (z.Contains(worldPos)) return z;
            return null;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.15f);
            foreach (var c in GetComponentsInChildren<BoxCollider>())
                Gizmos.DrawCube(c.bounds.center, c.bounds.size);
        }

        void OnDrawGizmos()
        {
            var cols = GetComponentsInChildren<BoxCollider>();
            if (cols.Length == 0) return;
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.2f, roomName);
        }
#endif
    }
}
