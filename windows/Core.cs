using System;

namespace HushAlarm {
    public enum AlarmState { Idle, Armed, Ringing, StoppedForSafety, Finished }
    public sealed class AlarmSession {
        public AlarmState State { get; private set; }
        public string EndpointId { get; private set; }
        public DateTimeOffset Target { get; private set; }
        public bool IsTimer { get; private set; }
        public string Reason { get; private set; }
        private TimeSpan duration;
        private TimeSpan started;
        public void Arm(string endpoint, DateTimeOffset target, bool timer, DateTimeOffset now, TimeSpan monotonic) {
            if (String.IsNullOrWhiteSpace(endpoint) || endpoint == "default" || endpoint == "communications") throw new ArgumentException("A concrete endpoint is required.");
            if (target <= now || target - now > TimeSpan.FromDays(2)) throw new ArgumentOutOfRangeException("target");
            EndpointId = endpoint; Target = target; IsTimer = timer; duration = target - now; started = monotonic; Reason = ""; State = AlarmState.Armed;
        }
        public TimeSpan Remaining(DateTimeOffset now, TimeSpan monotonic) {
            var value = IsTimer ? duration - (monotonic - started) : Target - now;
            return value > TimeSpan.Zero ? value : TimeSpan.Zero;
        }
        public bool Poll(DateTimeOffset now, TimeSpan monotonic, bool routeAllowed) {
            if (State != AlarmState.Armed && State != AlarmState.Ringing) return false;
            if (!routeAllowed) { SafetyStop("イヤホンが切断されました"); return false; }
            if (State == AlarmState.Armed) {
                var late = IsTimer ? monotonic - started - duration : now - Target;
                if (late > TimeSpan.FromSeconds(90)) { SafetyStop("予定時刻を過ぎたため停止しました"); return false; }
                if (Remaining(now, monotonic) == TimeSpan.Zero) { State = AlarmState.Ringing; return true; }
            }
            return false;
        }
        public void SafetyStop(string reason) { State = AlarmState.StoppedForSafety; Reason = reason; }
        public void Stop() { State = AlarmState.Finished; Reason = "停止しました"; }
    }
    public static class AlarmTime {
        public static DateTimeOffset NextLocal(TimeSpan time, DateTimeOffset now, TimeZoneInfo zone) {
            if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException("time");
            var local = TimeZoneInfo.ConvertTime(now, zone);
            var candidate = DateTime.SpecifyKind(local.Date + time, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(candidate)) throw new ArgumentException("This local time does not exist.");
            var result = new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
            if (result <= now) {
                candidate = candidate.AddDays(1);
                if (zone.IsInvalidTime(candidate)) throw new ArgumentException("This local time does not exist.");
                result = new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
            }
            return result;
        }
        public static bool IsHeadphoneFormFactor(uint value) { return value == 3 || value == 5; }
    }
}
