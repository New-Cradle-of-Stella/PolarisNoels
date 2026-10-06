using System;
using System.Threading;

namespace PolarisNoels.NetSmoke
{
    /// <summary>
    /// NetSmoke 入口。独立 .NET 进程，只链接传输层源文件，不引用 Unity/游戏程序集。
    ///   NetSmoke abi [--with-native-selftest]       ABI 布局自检（无需 DLL）
    ///   NetSmoke selfcheck                          托管队列/事件隔离自检（无需 DLL）
    ///   NetSmoke host &lt;roomCodeFile&gt; [clients]     主机进程
    ///   NetSmoke client &lt;roomCode&gt; &lt;name&gt; [role]  成员进程（role = kick | hostclose）
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // 看门狗：任何角色卡死都不能拖住编排脚本。
            Thread watchdog = new(() =>
            {
                Thread.Sleep(150000);
                Console.Error.WriteLine("NetSmoke: watchdog timeout, aborting");
                Environment.Exit(3);
            })
            { IsBackground = true };
            watchdog.Start();

            if (args.Length == 0)
            {
                Console.WriteLine("usage: NetSmoke abi [--with-native-selftest] | selfcheck | host <roomCodeFile> [clients] | client <roomCode> <name> [kick|hostclose]");
                return 2;
            }
            try
            {
                return args[0] switch
                {
                    "abi" => AbiChecks.Run(args),
                    "selfcheck" => SelfChecks.Run(args),
                    "orchestrate" => SmokeRunner.Run(args),
                    "nat" => NatCheck.Run(),
                    "host" => MeshRoles.RunHost(args),
                    "client" => MeshRoles.RunClient(args),
                    _ => Unknown(args[0])
                };
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"NetSmoke: unhandled {e}");
                return 4;
            }
        }

        static int Unknown(string mode)
        {
            Console.Error.WriteLine($"NetSmoke: unknown mode '{mode}'");
            return 2;
        }
    }
}
