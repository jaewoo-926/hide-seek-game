using System.Collections.Generic;
using UnityEngine;

namespace Chase
{
    // 가까이 가면 자동으로 줍는 아이템 (비밀번호 단서, 에너지 드링크)
    public abstract class Pickup : MonoBehaviour
    {
        public static readonly List<Pickup> All = new List<Pickup>();

        public float radius = 1.4f;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);

            var gm = GameManager.I;
            if (gm == null || !gm.IsPlaying) return;
            foreach (var t in gm.thieves)
            {
                if (t.state != ThiefState.Free) continue;
                Vector3 d = t.transform.position - transform.position;
                d.y = 0f;
                if (d.magnitude > radius) continue;
                OnPicked(t);
                Destroy(gameObject);
                return;
            }
        }

        protected abstract void OnPicked(Thief t);
    }
}
