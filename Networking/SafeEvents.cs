using System;

namespace PolarisNoels.Networking
{
    /// <summary>
    /// 事件分发的异常隔离：逐个订阅者 try/catch，一个回调抛异常不能中断
    /// 同一事件里的其它订阅者，也不能中断后续事件。
    /// </summary>
    public static class SafeEvents
    {
        public static void Raise<T1>(Action<T1> handler, T1 a, Action<Exception> onError)
        {
            if (handler == null) return;
            Delegate[] list = handler.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<T1>)list[i])(a);
                }
                catch (Exception e)
                {
                    Report(onError, e);
                }
            }
        }

        public static void Raise<T1, T2>(Action<T1, T2> handler, T1 a, T2 b, Action<Exception> onError)
        {
            if (handler == null) return;
            Delegate[] list = handler.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<T1, T2>)list[i])(a, b);
                }
                catch (Exception e)
                {
                    Report(onError, e);
                }
            }
        }

        public static void Raise<T1, T2, T3>(Action<T1, T2, T3> handler, T1 a, T2 b, T3 c, Action<Exception> onError)
        {
            if (handler == null) return;
            Delegate[] list = handler.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<T1, T2, T3>)list[i])(a, b, c);
                }
                catch (Exception e)
                {
                    Report(onError, e);
                }
            }
        }

        public static void Raise<T1, T2, T3, T4>(Action<T1, T2, T3, T4> handler, T1 a, T2 b, T3 c, T4 d, Action<Exception> onError)
        {
            if (handler == null) return;
            Delegate[] list = handler.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<T1, T2, T3, T4>)list[i])(a, b, c, d);
                }
                catch (Exception e)
                {
                    Report(onError, e);
                }
            }
        }

        static void Report(Action<Exception> onError, Exception e)
        {
            if (onError == null) return;
            try
            {
                onError(e);
            }
            catch
            {
                // 错误处理本身不能再抛。
            }
        }
    }
}
