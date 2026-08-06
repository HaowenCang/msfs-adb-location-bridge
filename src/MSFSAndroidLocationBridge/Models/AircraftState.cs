using System;
using System.Runtime.InteropServices;

namespace MSFSAndroidLocationBridge.Models
{
    /// <summary>
    /// 飞机状态数据模型（方案 §7.1 字段全集）。
    /// 注：MSFS 2024 SDK 托管包装器将数据按 AddToDataDefinition 顺序封送为 object[]，
    /// 本结构体仅作内存数据载体（见 SimConnectService.OnRecvSimobjectData）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AircraftState
    {
        public double LatitudeDeg;
        public double LongitudeDeg;
        public double AltitudeFt;
        public double GroundSpeedMps;
        public double GroundTrackDeg;
        public int OnGround;
        public double SimulationRate;
    }
}
