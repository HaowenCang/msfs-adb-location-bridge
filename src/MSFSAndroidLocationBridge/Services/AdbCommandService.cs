using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace MSFSAndroidLocationBridge.Services
{
    /// <summary>
    /// 单个 ADB 命令执行结果。
    /// </summary>
    public sealed class AdbResult
    {
        public int ExitCode { get; private set; }
        public string StdOut { get; private set; }
        public string StdErr { get; private set; }
        public bool TimedOut { get; private set; }

        public AdbResult(int exitCode, string stdOut, string stdErr, bool timedOut)
        {
            ExitCode = exitCode;
            StdOut = stdOut ?? string.Empty;
            StdErr = stdErr ?? string.Empty;
            TimedOut = timedOut;
        }
    }

    /// <summary>
    /// ADB 命令执行服务（方案 §6 AdbCommandService 阶段 1 版）：
    /// 每次执行独立启动 adb.exe 进程（方案 §10.4 MVP 方式）。
    /// </summary>
    public sealed class AdbCommandService
    {
        private readonly string _adbPath;

        public AdbCommandService(string adbPath)
        {
            _adbPath = adbPath;
        }

        public string AdbPath
        {
            get { return _adbPath; }
        }

        /// <summary>
        /// 执行带序列号的 adb 命令。
        /// </summary>
        public AdbResult Run(string serial, string[] args, int timeoutMs = 10000)
        {
            var full = new List<string>();
            if (!string.IsNullOrEmpty(serial))
            {
                full.Add("-s");
                full.Add(serial);
            }
            full.AddRange(args);
            return RunRaw(full.ToArray(), timeoutMs);
        }

        /// <summary>
        /// 枚举已连接且已授权的设备（解析 adb devices -l）。
        /// </summary>
        public List<string> ListDeviceSerials()
        {
            var result = RunRaw(new[] { "devices", "-l" }, 5000);
            var serials = new List<string>();
            if (result.ExitCode != 0)
            {
                return serials;
            }
            foreach (var line in result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // 跳过表头；行格式：<serial> <state> <attrs...>
                if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1] == "device")
                {
                    serials.Add(parts[0]);
                }
            }
            return serials;
        }

        private AdbResult RunRaw(string[] args, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _adbPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                Arguments = BuildArguments(args)
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            bool timedOut = false;
            int exitCode;

            using (var process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    timedOut = true;
                    try { process.Kill(); } catch { /* 进程已退出 */ }
                    process.WaitForExit();
                }
                exitCode = process.ExitCode;
            }

            return new AdbResult(exitCode, stdout.ToString().Trim(), stderr.ToString().Trim(), timedOut);
        }

        /// <summary>
        /// net48 无 ProcessStartInfo.ArgumentList，按命令行规则拼接（含空格参数加引号）。
        /// </summary>
        private static string BuildArguments(string[] args)
        {
            var parts = new List<string>(args.Length);
            foreach (var a in args)
            {
                if (a.IndexOf(' ') >= 0 || a.IndexOf('\"') >= 0)
                {
                    parts.Add("\"" + a.Replace("\"", "\\\"") + "\"");
                }
                else
                {
                    parts.Add(a);
                }
            }
            return string.Join(" ", parts);
        }
    }
}
