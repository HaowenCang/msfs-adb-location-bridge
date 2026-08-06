using System;

namespace MSFSAndroidLocationBridge.Models
{
    /// <summary>
    /// 飞机状态快照（方案 §8）。每次收到新位置时整体替换；注入线程只读最新快照。
    /// </summary>
    public sealed class AircraftSnapshot
    {
        public long Sequence { get; private set; }
        public DateTime ReceivedUtc { get; private set; }
        public AircraftState State { get; private set; }
        public bool SimRunning { get; private set; }
        public bool PositionChanged { get; private set; }
        public int ValidStreak { get; private set; }

        public AircraftSnapshot(long sequence, DateTime receivedUtc, AircraftState state,
            bool simRunning, bool positionChanged, int validStreak)
        {
            Sequence = sequence;
            ReceivedUtc = receivedUtc;
            State = state;
            SimRunning = simRunning;
            PositionChanged = positionChanged;
            ValidStreak = validStreak;
        }
    }
}
