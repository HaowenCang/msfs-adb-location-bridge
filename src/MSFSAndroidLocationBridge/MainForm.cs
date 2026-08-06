using System;
using System.Drawing;
using System.Windows.Forms;
using MSFSAndroidLocationBridge.Models;
using MSFSAndroidLocationBridge.Services;

namespace MSFSAndroidLocationBridge
{
    /// <summary>
    /// 主窗体（阶段 1 最小版：状态显示 + 开始/停止 + 日志）。
    /// 同时充当 SimConnect 消息泵：WndProc 处理 WM_SIMCONNECT。
    /// </summary>
    public sealed class MainForm : Form
    {
        // 本机环境配置（阶段 2 将迁移至 JSON 配置，见方案 §17）
        private const string AdbPath = @"E:\Laptop\softwares\AndroidSdk\platform-tools\adb.exe";
        private const string DeviceSerial = "c3a3ea64";
        private const string Provider = "gps";
        private const double AccuracyMeters = 3.0;

        private readonly SimConnectService _sim = new SimConnectService();
        private readonly AdbCommandService _adb = new AdbCommandService(AdbPath);
        private readonly MockLocationService _mock;
        private readonly InjectionScheduler _scheduler;

        private readonly Label _lblSim = new Label();
        private readonly Label _lblDevice = new Label();
        private readonly Label _lblPos = new Label();
        private readonly Button _btnStart = new Button();
        private readonly Button _btnStop = new Button();
        private readonly Button _btnConnectSim = new Button();
        private readonly TextBox _txtLog = new TextBox();
        private readonly Timer _uiTimer = new Timer();

        private bool _restoring;

        public MainForm()
        {
            _mock = new MockLocationService(_adb, DeviceSerial, Provider);
            _scheduler = new InjectionScheduler(_sim, _mock, AccuracyMeters);

            BuildUi();
            Log.Message += OnLogMessage;
            _uiTimer.Interval = 500;
            _uiTimer.Tick += OnUiTick;
            _uiTimer.Start();

            FormClosing += OnFormClosing;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryConnectSimConnect();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == SimConnectService.WmSimConnect)
            {
                _sim.ReceiveMessage();
            }
            base.WndProc(ref m);
        }

        private void BuildUi()
        {
            Text = "MSFS Android Location Bridge（阶段 1 概念验证）";
            ClientSize = new Size(640, 420);
            MinimumSize = new Size(560, 360);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 2, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            AddInfoRow(info, "MSFS 连接", _lblSim);
            AddInfoRow(info, "Android 设备", _lblDevice);
            AddInfoRow(info, "最新位置", _lblPos);
            root.Controls.Add(info, 0, 0);
            root.SetColumnSpan(info, 2);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _btnStart.Text = "连接并开始注入";
            _btnStart.Width = 150;
            _btnStart.Click += OnStartClicked;
            _btnStop.Text = "停止并恢复真实定位";
            _btnStop.Width = 160;
            _btnStop.Enabled = false;
            _btnStop.Click += OnStopClicked;
            _btnConnectSim.Text = "重连 MSFS";
            _btnConnectSim.Width = 110;
            _btnConnectSim.Click += OnConnectSimClicked;
            buttons.Controls.Add(_btnStart);
            buttons.Controls.Add(_btnStop);
            buttons.Controls.Add(_btnConnectSim);
            root.Controls.Add(buttons, 0, 1);
            root.SetColumnSpan(buttons, 2);

            _txtLog.Dock = DockStyle.Fill;
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.BackColor = Color.FromArgb(30, 30, 30);
            _txtLog.ForeColor = Color.LightGray;
            root.Controls.Add(_txtLog, 0, 2);
            root.SetColumnSpan(_txtLog, 2);

            Controls.Add(root);
            UpdateDeviceLabel();
        }

        private static void AddInfoRow(TableLayoutPanel panel, string caption, Label value)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            row.Controls.Add(new Label { Text = caption + "：", AutoSize = true, Font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold) });
            row.Controls.Add(value);
            value.AutoSize = true;
            value.Font = new Font(FontFamily.GenericSansSerif, 9f);
            panel.Controls.Add(row);
        }

        private void UpdateDeviceLabel()
        {
            string text;
            var serials = _adb.ListDeviceSerials();
            if (serials.Count == 0)
            {
                text = "未检测到已授权设备";
            }
            else
            {
                text = serials.Contains(DeviceSerial)
                    ? DeviceSerial + "（已连接，使用中）"
                    : DeviceSerial + "（未连接；检测到：" + string.Join(", ", serials) + "）";
            }
            _lblDevice.Text = text;
        }

        private void TryConnectSimConnect()
        {
            try
            {
                _sim.Connect(Handle);
                _lblSim.Text = "已连接";
                _lblSim.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                _lblSim.Text = "未连接";
                _lblSim.ForeColor = Color.Red;
                Log.Info("MSFS 连接失败：" + ex.Message + "（确认 MSFS 2024 已启动并进入飞行后点“重连 MSFS”）");
            }
        }

        private void OnConnectSimClicked(object sender, EventArgs e)
        {
            TryConnectSimConnect();
        }

        private void OnStartClicked(object sender, EventArgs e)
        {
            if (!_sim.Connected)
            {
                Log.Info("尚未连接 MSFS，请先连接（进入飞行后点“重连 MSFS”）");
                return;
            }

            try
            {
                if (!_mock.Initialize())
                {
                    Log.Error("测试提供器初始化失败，未启动注入");
                    return;
                }
                _scheduler.Start();
                _btnStart.Enabled = false;
                _btnStop.Enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error("启动失败：" + ex.Message);
            }
        }

        private void OnStopClicked(object sender, EventArgs e)
        {
            StopAndRestore();
        }

        private void StopAndRestore()
        {
            if (_restoring)
            {
                return;
            }
            _restoring = true;
            try
            {
                _scheduler.Stop();
                _mock.Restore();
                _btnStart.Enabled = true;
                _btnStop.Enabled = false;
            }
            finally
            {
                _restoring = false;
            }
        }

        private void OnUiTick(object sender, EventArgs e)
        {
            var snapshot = _sim.LatestSnapshot;
            if (snapshot == null)
            {
                _lblPos.Text = "（无数据）";
                return;
            }
            var s = snapshot.State;
            _lblPos.Text = string.Format(
                "纬度 {0:F6}  经度 {1:F6}  高度 {2:F0} ft  地速 {3:F1} m/s  航迹 {4:F0}°  倍率 {5:F1}",
                s.LatitudeDeg, s.LongitudeDeg, s.AltitudeFt, s.GroundSpeedMps, s.GroundTrackDeg, s.SimulationRate);
        }

        private void OnLogMessage(string message)
        {
            if (IsDisposed || _txtLog.IsDisposed)
            {
                return;
            }
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => AppendLog(message))); }
                catch { /* 窗体已关闭 */ }
            }
            else
            {
                AppendLog(message);
            }
        }

        private void AppendLog(string message)
        {
            _txtLog.AppendText(message + Environment.NewLine);
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            Log.Message -= OnLogMessage;
            _uiTimer.Stop();
            try
            {
                _scheduler.Stop();
                _mock.Restore(); // 方案 §14：关闭前强制恢复真实定位
            }
            catch (Exception ex)
            {
                Log.Error("退出恢复失败：" + ex.Message);
            }
            _sim.Dispose();
        }
    }
}
