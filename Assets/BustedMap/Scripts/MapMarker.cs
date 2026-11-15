using UnityEngine;

namespace Busted.Map
{
    public enum MapMarkerKind { SafeCandidate, CopSpawn, ThiefSpawn, JailCandidate, FloorLink, Door, ExitCandidate }

    /// <summary>
    /// 맵 빌더가 생성하는 위치 마커. 게임 코드에서
    /// FindObjectsByType&lt;MapMarker&gt;() 로 모아서 금고/스폰/감옥 배치에 사용.
    /// </summary>
    public class MapMarker : MonoBehaviour
    {
        public MapMarkerKind kind;
        public string id;
        public int floor = 1;
        public string room;

        static Color ColorOf(MapMarkerKind k)
        {
            switch (k)
            {
                case MapMarkerKind.SafeCandidate: return new Color(1f, 0.8f, 0f);
                case MapMarkerKind.CopSpawn: return new Color(0.85f, 0.15f, 0.15f);
                case MapMarkerKind.ThiefSpawn: return new Color(0.1f, 0.45f, 1f);
                case MapMarkerKind.JailCandidate: return new Color(0.55f, 0.25f, 0.7f);
                case MapMarkerKind.FloorLink: return new Color(0.15f, 0.65f, 0.55f);
                case MapMarkerKind.ExitCandidate: return new Color(1f, 0.45f, 0f);
                default: return Color.green;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = ColorOf(kind);
            if (kind == MapMarkerKind.SafeCandidate)
                Gizmos.DrawCube(transform.position + Vector3.up * 0.5f, new Vector3(0.8f, 1f, 0.8f));
            else
            {
                Gizmos.DrawSphere(transform.position + Vector3.up * 0.5f, 0.45f);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, transform.position + Vector3.up * 0.5f + transform.forward * 1.2f);
            }
#if UNITY_EDITOR
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.6f, id);
#endif
        }
    }
}
