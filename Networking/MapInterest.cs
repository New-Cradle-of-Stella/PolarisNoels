using System;
using System.Collections.Generic;

namespace PolarisNoels.Networking
{
    /// <summary>地图通知独立于角色快照，异图停止快照后仍能发现重新同图的玩家。</summary>
    public sealed class MapInterest
    {
        readonly Dictionary<int, string> peerMaps = new();

        public string LocalMap { get; private set; }

        public bool SetLocalMap(string key)
        {
            key ??= "";
            if (LocalMap == key) return false;
            LocalMap = key;
            return true;
        }

        public bool SetPeerMap(int id, string key)
        {
            key ??= "";
            bool changed = !peerMaps.TryGetValue(id, out string previous) || previous != key;
            peerMaps[id] = key;
            return changed;
        }

        public string GetPeerMap(int id) => peerMaps.TryGetValue(id, out string key) ? key : "";

        public bool IsSameMap(int id, string key) => !string.IsNullOrEmpty(key)
            && string.Equals(GetPeerMap(id), key, StringComparison.Ordinal);

        public void Remove(int id) => peerMaps.Remove(id);
    }
}
