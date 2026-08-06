using System;
using System.Globalization;
using System.Text.RegularExpressions;
using MSFSAndroidLocationBridge.Models;

namespace MSFSAndroidLocationBridge.Services
{
    /// <summary>
    /// 测试位置提供器生命周期管理（方案 §9、§10、§14 阶段 1 版）。
    /// 所有方法均幂等：重复执行不会造成额外损害。
    /// </summary>
    public sealed class MockLocationService
    {
        private readonly AdbCommandService _adb;
        private readonly string _serial;
        private readonly string _provider;

        private string _shellUid = "2000";
        private string _originalAppOp = "default";
        private bool _originalLocationEnabled = true;

        /// <summary>当前是否处于模拟位置注入状态。</summary>
        public bool IsActive { get; private set; }

        /// <summary>shell UID（方案 §5.3：动态读取，不硬编码）。</summary>
        public string ShellUid
        {
            get { return _shellUid; }
        }

        public MockLocationService(AdbCommandService adb, string serial, string provider)
        {
            _adb = adb;
            _serial = serial;
            _provider = provider;
        }

        /// <summary>
        /// 初始化测试提供器（方案 §9）：保存原始状态 → 清理遗留 → 开启定位 → 授权 → 创建并启用提供器。
        /// </summary>
        public bool Initialize()
        {
            _shellUid = ReadShellUid();

            // 9.1 保存原始状态
            var enabledResult = _adb.Run(_serial, new[] { "shell", "cmd", "location", "is-location-enabled" }, 5000);
            if (enabledResult.ExitCode == 0)
            {
                _originalLocationEnabled = enabledResult.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            var appOpResult = _adb.Run(_serial, new[] { "shell", "appops", "get", _shellUid, "android:mock_location" }, 5000);
            var appOpMode = ParseAppOpMode(appOpResult.StdOut);
            if (appOpMode != null)
            {
                _originalAppOp = appOpMode;
            }

            // 9.2 清理上一次遗留的测试提供器（错误可忽略）
            _adb.Run(_serial, new[] { "shell", "cmd", "location", "providers", "set-test-provider-enabled", _provider, "false" }, 5000);
            _adb.Run(_serial, new[] { "shell", "cmd", "location", "providers", "remove-test-provider", _provider }, 5000);

            // 9.3 开启定位并授权 shell UID
            var enable = _adb.Run(_serial, new[] { "shell", "cmd", "location", "set-location-enabled", "true" }, 5000);
            if (enable.ExitCode != 0)
            {
                Log.Error($"开启系统定位失败：{enable.StdErr.Trim()}");
                return false;
            }

            var appOp = _adb.Run(_serial, new[] { "shell", "appops", "set", _shellUid, "android:mock_location", "allow" }, 5000);
            if (appOp.ExitCode != 0)
            {
                Log.Error($"授予 mock_location 权限失败：{appOp.StdErr.Trim()}");
                return false;
            }

            // 9.4 创建并启用测试提供器
            var add = _adb.Run(_serial, new[] { "shell", "cmd", "location", "providers", "add-test-provider", _provider, "--requiresSatellite" }, 5000);
            if (add.ExitCode != 0)
            {
                Log.Error($"创建测试提供器 {_provider} 失败：{add.StdErr.Trim()}");
                return false;
            }

            var enableProvider = _adb.Run(_serial, new[] { "shell", "cmd", "location", "providers", "set-test-provider-enabled", _provider, "true" }, 5000);
            if (enableProvider.ExitCode != 0)
            {
                Log.Error($"启用测试提供器 {_provider} 失败：{enableProvider.StdErr.Trim()}");
                return false;
            }

            IsActive = true;
            Log.Info($"测试提供器 {_provider} 已创建并启用（shell uid={_shellUid}，原始 AppOp={_originalAppOp}，原始定位开关={_originalLocationEnabled}）");
            return true;
        }

        /// <summary>
        /// 注入一个位置（方案 §10.1/§10.2）。数字使用 InvariantCulture 且 7 位小数。
        /// </summary>
        public bool Inject(double latitudeDeg, double longitudeDeg, double accuracyMeters)
        {
            var lat = latitudeDeg.ToString("F7", CultureInfo.InvariantCulture);
            var lon = longitudeDeg.ToString("F7", CultureInfo.InvariantCulture);
            var accuracy = accuracyMeters.ToString("F1", CultureInfo.InvariantCulture);

            var result = _adb.Run(_serial, new[]
            {
                "shell", "cmd", "location", "providers",
                "set-test-provider-location", _provider,
                "--location", lat + "," + lon,
                "--accuracy", accuracy
            }, 1000);

            if (result.ExitCode != 0)
            {
                Log.Error($"位置注入失败（exit={result.ExitCode}，timeout={result.TimedOut}）：{result.StdErr.Trim()}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 恢复真实定位（方案 §14 顺序：禁用 → 删除提供器 → 恢复 AppOp → 恢复定位开关）。
        /// </summary>
        public void Restore()
        {
            if (!IsActive)
            {
                return;
            }

            _adb.Run(_serial, new[] { "shell", "cmd", "location", "providers", "set-test-provider-enabled", _provider, "false" }, 5000);
            _adb.Run(_serial, new[] { "shell", "cmd", "location", "providers", "remove-test-provider", _provider }, 5000);

            var appOp = _adb.Run(_serial, new[] { "shell", "appops", "set", _shellUid, "android:mock_location", _originalAppOp }, 5000);
            if (appOp.ExitCode != 0)
            {
                Log.Error($"恢复 mock_location AppOp 失败：{appOp.StdErr.Trim()}");
            }

            if (!_originalLocationEnabled)
            {
                var loc = _adb.Run(_serial, new[] { "shell", "cmd", "location", "set-location-enabled", "false" }, 5000);
                if (loc.ExitCode != 0)
                {
                    Log.Error($"恢复定位开关失败：{loc.StdErr.Trim()}");
                }
            }

            IsActive = false;
            Log.Info("已恢复真实定位（测试提供器已删除，AppOp/定位开关已还原）");
        }

        private string ReadShellUid()
        {
            var result = _adb.Run(_serial, new[] { "shell", "id", "-u" }, 5000);
            if (result.ExitCode != 0)
            {
                Log.Error($"读取 shell UID 失败，使用默认值 2000：{result.StdErr.Trim()}");
                return "2000";
            }
            var uid = result.StdOut.Trim();
            return string.IsNullOrEmpty(uid) ? "2000" : uid;
        }

        /// <summary>
        /// 从 appops get 输出解析权限模式。输出形如 "Uid mode: MOCK_LOCATION: default"。
        /// </summary>
        private static string ParseAppOpMode(string output)
        {
            var match = Regex.Match(output ?? string.Empty, @"MOCK_LOCATION:\s*(\w+)");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
