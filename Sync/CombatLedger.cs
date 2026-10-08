using System;
using System.Collections.Generic;
using PolarisNoels.DataStruct;

namespace PolarisNoels
{
    /// <summary>Bounded replay protection and owner-clock hit validation. No Unity dependency.</summary>
    public sealed class CombatLedger
    {
        const int WindowSize = 1024;
        sealed class Window
        {
            public long Highest;
            public readonly HashSet<long> Seen = new();
            public readonly Dictionary<long, CombatResult> Results = new();
        }
        readonly Dictionary<(int Peer, string Epoch), Window> windows = new();

        public bool Begin(int peer, string epoch, long id, out CombatResult cached)
        {
            cached = null;
            if (peer < 0 || id <= 0 || string.IsNullOrEmpty(epoch) || epoch.Length > 64) return false;
            var key = (peer, epoch);
            if (!windows.TryGetValue(key, out var window)) windows[key] = window = new Window();
            if (window.Results.TryGetValue(id, out cached)) return false;
            if (id <= window.Highest - WindowSize || !window.Seen.Add(id)) return false;
            if (id > window.Highest)
            {
                window.Highest = id;
                window.Seen.RemoveWhere(value => value <= id - WindowSize);
                foreach (var old in new List<long>(window.Results.Keys))
                    if (old <= id - WindowSize) window.Results.Remove(old);
            }
            return true;
        }

        public void Complete(int peer, string epoch, long id, CombatResult result)
        {
            if (windows.TryGetValue((peer, epoch), out var window) && window.Seen.Contains(id))
                window.Results[id] = result;
        }
        public void RemovePeer(int peer)
        {
            foreach (var key in new List<(int Peer, string Epoch)>(windows.Keys))
                if (key.Peer == peer) windows.Remove(key);
        }
        public void Clear() => windows.Clear();

        public readonly struct Bounds
        {
            public readonly double Time;
            public readonly float X, Y, Width, Height;
            public Bounds(double time, float x, float y, float width, float height)
            { Time = time; X = x; Y = y; Width = width; Height = height; }
        }

        public static CombatOutcome ValidateHit(double now, double observed, int rttMs,
            float x, float y, Bounds current, IReadOnlyList<Bounds> history, float radius = 0)
        {
            if (double.IsNaN(observed) || double.IsInfinity(observed) || float.IsNaN(x) || float.IsInfinity(x)
                || float.IsNaN(y) || float.IsInfinity(y)) return CombatOutcome.Invalid;
            double allowance = Math.Min(3, 0.75 + Math.Max(0, rttMs) / 1000.0);
            if (observed < 0 || observed > now + 0.05 || (observed > 0 && now - observed > allowance))
                return CombatOutcome.Stale;
            if (float.IsNaN(radius) || float.IsInfinity(radius)) return CombatOutcome.Invalid;
            float margin = 0.35f + Math.Min(16, Math.Max(0, radius));
            if (Contains(current, x, y, margin)) return CombatOutcome.Applied;
            // No wall-clock synchronization: observed is echoed from the target owner's snapshot.
            if (observed > 0 && history != null)
                foreach (var sample in history)
                    if (Math.Abs(sample.Time - observed) <= 0.15 && Contains(sample, x, y, margin))
                        return CombatOutcome.Applied;
            return CombatOutcome.OutOfRange;
        }
        static bool Contains(Bounds bounds, float x, float y, float margin)
            => Math.Abs(x - bounds.X) <= Math.Max(0, bounds.Width) + margin
                && Math.Abs(y - bounds.Y) <= Math.Max(0, bounds.Height) + margin;
    }
}
