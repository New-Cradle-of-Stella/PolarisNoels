using System;
using System.IO;
using System.Text;

namespace PolarisNoels.SessionSmoke
{
    /// <summary>
    /// SessionSmoke 入口：独立 net8.0 控制台，只源链接 managed 会话层与编解码器。
    /// 退出码：0 = 全部断言通过；1 = 有断言失败；4 = 未处理异常。
    /// 输出同时写到 stdout 和 &lt;exe 目录&gt;/session-smoke.log（沙箱里 shell 重定向不可用时仍可留存日志）。
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "session-smoke.log");
            using (StreamWriter file = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true })
            using (TeeTextWriter tee = new TeeTextWriter(Console.Out, file))
            {
                Console.SetOut(tee);
                try
                {
                    int exit = SessionChecks.Run(args);
                    Console.WriteLine($"log: {logPath}");
                    return exit;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"SessionSmoke: unhandled {e}");
                    Console.Error.WriteLine($"SessionSmoke: unhandled {e}");
                    return 4;
                }
            }
        }

        /// <summary>把控制台输出同时镜像到日志文件，避免依赖 shell 重定向。</summary>
        sealed class TeeTextWriter : TextWriter
        {
            readonly TextWriter primary;
            readonly TextWriter secondary;

            public TeeTextWriter(TextWriter primary, TextWriter secondary)
            {
                this.primary = primary;
                this.secondary = secondary;
            }

            public override Encoding Encoding => primary.Encoding;

            public override void Write(char value)
            {
                primary.Write(value);
                secondary.Write(value);
            }

            public override void Write(string value)
            {
                primary.Write(value);
                secondary.Write(value);
            }

            public override void WriteLine(string value)
            {
                primary.WriteLine(value);
                secondary.WriteLine(value);
            }

            public override void Flush()
            {
                primary.Flush();
                secondary.Flush();
            }
        }
    }
}
