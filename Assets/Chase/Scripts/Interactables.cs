using System.Collections.Generic;
using UnityEngine;

namespace Chase
{
    // E키(홀드)로 상호작용하는 오브젝트 공통 베이스
    // 구현: Safe(금고), HideSpot(은신처), Prison(감옥), ExitPoint(탈출구)
    public abstract class Interactable : MonoBehaviour
    {
        public static readonly List<Interactable> All = new List<Interactable>();

        public float range = 2.2f;

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        public abstract float HoldTime { get; }
        public abstract bool CanInteract(Thief t);
        public abstract string Prompt(Thief t);
        public abstract void Complete(Thief t);
        public virtual void OnHoldStart(Thief t) { }
        public virtual void OnHoldCancel(Thief t) { }

        public float FlatDistance(Vector3 p)
        {
            Vector3 d = transform.position - p;
            d.y = 0f;
            return d.magnitude;
        }

        public bool InRange(Thief t) => FlatDistance(t.transform.position) <= range;

        public static Interactable FindNearest(Thief t)
        {
            Interactable best = null;
            float bestDist = float.MaxValue;
            foreach (var i in All)
            {
                float d = i.FlatDistance(t.transform.position);
                if (d > i.range || d >= bestDist || !i.CanInteract(t)) continue;
                best = i;
                bestDist = d;
            }
            return best;
        }
    }
}
