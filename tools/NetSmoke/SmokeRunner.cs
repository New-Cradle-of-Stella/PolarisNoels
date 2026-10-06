using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PolarisNoels.NetSmoke
{
    public static class SmokeRunner
    {
        public static int Run(string[] args)
        {
            int timeout = args.Length > 1 ? int.Parse(args[1]) : 90;
            string directory = Path.Combine(AppContext.BaseDirectory, "smoke-run");
            Directory.CreateDirectory(directory);
            string codeFile = Path.Combine(directory, "roomcode.txt");
            File.Delete(codeFile);
            var children = new List<(string Name, Process Process, Task<string> Out, Task<string> Error)>();
            void Launch(string name, params string[] roleArgs)
            {
                var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                // Merge differently cased Windows environment keys before launching.
                info.Environment.Clear();
                foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) info.Environment[(string)entry.Key] = (string)entry.Value;
                info.ArgumentList.Add(typeof(Program).Assembly.Location);
                foreach (string arg in roleArgs) info.ArgumentList.Add(arg);
                var process = Process.Start(info);
                children.Add((name, process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync()));
            }
            try
            {
                Launch("host", "host", codeFile, "2");
                var started = Stopwatch.StartNew();
                while (!File.Exists(codeFile) && !children[0].Process.HasExited && started.Elapsed.TotalSeconds < 30) Thread.Sleep(100);
                if (!File.Exists(codeFile)) throw new IOException("room code never appeared");
                string roomCode = File.ReadAllText(codeFile).Trim();
                Launch("client1", "client", roomCode, "c1", "any");
                Launch("client2", "client", roomCode, "c2", "any");
                started.Restart();
                while (children.Any(c => !c.Process.HasExited) && started.Elapsed.TotalSeconds < timeout) Thread.Sleep(100);
                if (children.Any(c => !c.Process.HasExited)) throw new TimeoutException("three-process mesh timeout");
                bool ok = true, kick = false, close = false;
                foreach (var child in children)
                {
                    string output = child.Out.GetAwaiter().GetResult();
                    string error = child.Error.GetAwaiter().GetResult();
                    File.WriteAllText(Path.Combine(directory, child.Name + ".log"), output);
                    File.WriteAllText(Path.Combine(directory, child.Name + ".err"), error);
                    Console.WriteLine($"== {child.Name} exit={child.Process.ExitCode} ==\n{output}{error}");
                    ok &= child.Process.ExitCode == 0;
                    if (child.Name != "host") { kick |= output.Contains("(Kicked)"); close |= output.Contains("(HostClosed)"); }
                }
                ok &= kick && close;
                Console.WriteLine($"RESULT: {(ok ? "OK" : "FAIL")} kick={kick} hostclose={close}");
                return ok ? 0 : 3;
            }
            finally
            {
                foreach (var child in children) { if (!child.Process.HasExited) child.Process.Kill(true); child.Process.WaitForExit(); child.Process.Dispose(); }
                File.Delete(codeFile);
            }
        }
    }
}
