using System;
using Microsoft.FlightSimulator.SimConnect;
using MSFSAndroidLocationBridge.Models;

namespace MSFSAndroidLocationBridge.Services
{
    /// <summary>
    /// SimConnect 服务（方案 §7、§8 阶段 1 版）。
    /// 注意：SimConnect 非线程安全——本类所有方法必须在创建连接的 UI/消息线程调用
    /// （由 MainForm 通过 WndProc 转发 WM_SIMCONNECT 驱动 ReceiveMessage）。
    /// </summary>
    public sealed class SimConnectService : IDisposable
    {
        internal const uint WmSimConnect = 0x0400 + 0x100; // WM_USER + 自定义偏移

        private enum DefinitionId
        {
            AircraftState = 0
        }

        private enum RequestId
        {
            AircraftState = 0
        }

        private enum EventId
        {
            Sim = 0,
            PositionChanged = 1
        }

        private readonly object _lock = new object();
        private SimConnect _simConnect;
        private bool _simRunning;
        private bool _positionChangedPending;
        private long _sequence;
        private AircraftSnapshot _latest;
        private bool _disposed;

        /// <summary>当前是否已连接 MSFS。</summary>
        public bool Connected { get; private set; }

        /// <summary>最新飞机状态快照（线程安全读取，返回副本）。</summary>
        public AircraftSnapshot LatestSnapshot
        {
            get
            {
                lock (_lock)
                {
                    return _latest;
                }
            }
        }

        /// <summary>
        /// 建立 SimConnect 连接并注册数据定义（方案 §7.3）。hWnd 必须为可用窗口句柄。
        /// 连接失败（MSFS 未运行）时抛出异常，由调用方处理。
        /// </summary>
        public void Connect(IntPtr hWnd)
        {
            if (Connected)
            {
                return;
            }

            lock (_lock)
            {
                _sequence = 0;
                _latest = null;
            }

            _simConnect = new SimConnect("MSFS Android Location Bridge", hWnd, WmSimConnect, null, 0);
            _simConnect.OnRecvOpen += OnRecvOpen;
            _simConnect.OnRecvQuit += OnRecvQuit;
            _simConnect.OnRecvException += OnRecvException;
            _simConnect.OnRecvSimobjectData += OnRecvSimobjectData;
            _simConnect.OnRecvEvent += OnRecvEvent;

            RegisterDataDefinitions();

            _simConnect.RequestDataOnSimObject(
                RequestId.AircraftState,
                DefinitionId.AircraftState,
                SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);

            _simConnect.SubscribeToSystemEvent(EventId.Sim, "Sim");
            _simConnect.SubscribeToSystemEvent(EventId.PositionChanged, "PositionChanged");

            Connected = true;
            Log.Info("已连接 MSFS 2024（SimConnect），数据请求：SIM_FRAME");
        }

        /// <summary>
        /// 由 UI 线程在收到 WM_SIMCONNECT 消息时调用，驱动 SimConnect 消息处理。
        /// </summary>
        public void ReceiveMessage()
        {
            if (_simConnect != null && Connected)
            {
                _simConnect.ReceiveMessage();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Connected = false;
            if (_simConnect != null)
            {
                try { _simConnect.Dispose(); } catch { /* 忽略释放异常 */ }
                _simConnect = null;
            }
        }

        private void RegisterDataDefinitions()
        {
            var def = DefinitionId.AircraftState;
            _simConnect.AddToDataDefinition(def, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(def, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(def, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(def, "GPS GROUND SPEED", "meters per second", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(def, "GPS GROUND TRUE TRACK", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(def, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.INT32, 0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(def, "SIMULATION RATE", "number", SIMCONNECT_DATATYPE.FLOAT64, 0f, SimConnect.SIMCONNECT_UNUSED);
        }

        private void OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            // 连接建立（Connected 已在 Connect() 中置位）
        }

        private void OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Connected = false;
            _simRunning = false;
            Log.Info("MSFS 已退出/断开 SimConnect");
        }

        private void OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            Log.Error("SimConnect 异常：" + data.dwException + " (exception=" + data.dwSendID + ")");
        }

        private void OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if (data.dwRequestID != (uint)RequestId.AircraftState)
            {
                return;
            }

            // 本包装器将数据按 AddToDataDefinition 顺序封送为 object[]（MSFS 2024 SDK 托管包装器行为）
            var raw = data.dwData as object[];
            if (raw == null || raw.Length < 7)
            {
                return;
            }

            var state = new AircraftState
            {
                LatitudeDeg = Convert.ToDouble(raw[0]),
                LongitudeDeg = Convert.ToDouble(raw[1]),
                AltitudeFt = Convert.ToDouble(raw[2]),
                GroundSpeedMps = Convert.ToDouble(raw[3]),
                GroundTrackDeg = Convert.ToDouble(raw[4]),
                OnGround = Convert.ToInt32(raw[5]),
                SimulationRate = Convert.ToDouble(raw[6])
            };

            // 方案 §8 有效性检查（新鲜度检查由注入线程负责，见 InjectionScheduler）
            bool valid =
                IsFinite(state.LatitudeDeg) && IsFinite(state.LongitudeDeg) &&
                state.LatitudeDeg >= -90.0 && state.LatitudeDeg <= 90.0 &&
                state.LongitudeDeg >= -180.0 && state.LongitudeDeg <= 180.0;

            lock (_lock)
            {
                bool positionChanged = _positionChangedPending;
                if (positionChanged)
                {
                    _positionChangedPending = false;
                }

                int streak;
                if (!valid)
                {
                    streak = 0;
                }
                else if (positionChanged)
                {
                    streak = 3; // PositionChanged 后立即接受新坐标（方案 §7.5）
                }
                else
                {
                    streak = Math.Min(_latest == null ? 0 : _latest.ValidStreak + 1, 1000);
                }

                _latest = new AircraftSnapshot(
                    ++_sequence,
                    DateTime.UtcNow,
                    state,
                    _simRunning,
                    positionChanged,
                    streak);
            }
        }

        private void OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
        {
            if (data.uEventID == (uint)EventId.Sim)
            {
                _simRunning = data.dwData != 0;
                Log.Info("模拟器运行状态：" + (_simRunning ? "运行" : "停止"));
            }
            else if (data.uEventID == (uint)EventId.PositionChanged)
            {
                _positionChangedPending = true;
                Log.Info("检测到飞机位置变更事件（PositionChanged）");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
