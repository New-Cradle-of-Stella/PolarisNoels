using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using PolarisNoels.Networking;

namespace PolarisNoels.NetSmoke
{
    public static class NatCheck
    {
        // Live STUN diagnostics are opt-in; never print NAT_STATE's room secret.
        public static int Run()
        {
            using var transport = new NativeTransport(new NetTransportConfig { MaxPeers = 2, EnableStun = true });
            string type = null;
            transport.NatInfo += json =>
            {
                using var info = JsonDocument.Parse(json);
                type = info.RootElement.GetProperty("nat").GetString();
                Console.WriteLine($"NAT={type} candidates={info.RootElement.GetProperty("candidates").GetArrayLength()} stun_ms={info.RootElement.GetProperty("stun_rtt_ms").GetInt32()}");
            };
            if (!transport.Init(out string error)) { Console.Error.WriteLine(error); return 1; }
            transport.StartHost();
            var deadline = Stopwatch.StartNew();
            while (type == null && deadline.Elapsed.TotalSeconds < 10) { transport.Poll(); Thread.Sleep(10); }
            bool reflected = type is "cone" or "symmetric" or "unknown";
            Console.WriteLine(reflected ? "LIVE-STUN: PASS" : "LIVE-STUN: BLOCKED (no public STUN response)");
            return reflected ? 0 : 10;
        }
    }
}
