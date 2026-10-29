using System.Collections.Generic;
using UnityEngine;

namespace Chase.Online
{
    public enum Role { Thief = 0, Cop = 1 }

    // 대기방 → 인게임으로 넘어갈 때 서버(방장)가 기억하는 참가자 정보
    public static class MatchSetup
    {
        public struct Entry
        {
            public Role role;
            public string nickname;
            public int color;
        }

        public static readonly Dictionary<ulong, Entry> Players = new Dictionary<ulong, Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Players.Clear();
    }
}
