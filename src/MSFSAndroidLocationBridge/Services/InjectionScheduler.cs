using System;
using System.Threading;
using MSFSAndroidLocationBridge.Models;

namespace MSFSAndroidLocationBridge.Services
{
    /// <summary>
    /// 注入调度器（方案 §11 阶段 1 版）：
    /// 500 ms 周期（2 Hz）；最新值覆盖模型，不排队；同一时间最多一条 ADB 命令在执行（跳帧）。
    /// </summary>
    public sealed class InjectionScheduler : IDisposable
    {
        private const int PeriodMs = 500;
        private const int MinValidStreak = 3;
        private const double MaxSnapshotAgeSeconds = 1.0;

        private readonly SimConnectService _sim;
        private readonly MockLocationService _mock;
        private readonly double _accuracyMeters;

        private Timer _timer;
        private volatile bool _running;
        private int _inFlight;

        public InjectionScheduler(SimConnectService sim, MockLocationService mock, double accuracyMeters)
        {
            _sim = sim;
            _mock = mock;
            _accuracyMeters = accuracyMeters;
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }
            _running = true;
            _timer = new Timer(Tick, null, PeriodMs, PeriodMs);
            Log.Info($"注入调度已启动（{PeriodMs} ms 周期，精度 {_accuracyMeters} m）");
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }
            _running = false;
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
            Log.Info("注入调度已停止");
        }

        public void Dispose()
        {
            Stop();
        }

        private void Tick(object state)
        {
            if (!_running)
            {
                return;
            }

            // 上一条 ADB 命令尚未完成：丢弃本周期（方案 §10.3/§11）
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var snapshot = _sim.LatestSnapshot;
                if (snapshot == null || !_mock.IsActive)
                {
                    return;
                }
                if (!snapshot.SimRunning)
                {
                    return; // 模拟器未运行：停止注入（方案 §7.5）
                }
                if ((DateTime.UtcNow - snapshot.ReceivedUtc).TotalSeconds > MaxSnapshotAgeSeconds)
                {
                    return; // 数据过期
                }
                if (snapshot.ValidStreak < MinValidStreak)
                {
                    return; // 连续有效样本不足（方案 §8）
                }

                _mock.Inject(snapshot.State.LatitudeDeg, snapshot.State.LongitudeDeg, _accuracyMeters);
            }
            catch (Exception ex)
            {
                Log.Error("注入调度异常：" + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
            }
        }
    }
}
