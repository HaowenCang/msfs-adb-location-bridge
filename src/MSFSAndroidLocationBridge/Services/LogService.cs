using System;

namespace MSFSAndroidLocationBridge.Services
{
    /// <summary>
    /// 简单日志服务（方案 §18 基础版）。Info 记关键事件，Error 记失败；正常位置更新不刷屏。
    /// </summary>
    public static class Log
    {
        public static event Action<string> Message;

        public static void Info(string message)
        {
            Message?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public static void Error(string message)
        {
            Message?.Invoke($"[{DateTime.Now:HH:mm:ss}] [错误] {message}");
        }
    }
}
