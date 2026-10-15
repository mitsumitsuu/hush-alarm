using System;
using HushAlarm;
class Tests {
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    static void Main() {
        var now = new DateTimeOffset(2026, 10, 15, 23, 59, 50, TimeSpan.FromHours(9));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/JST", TimeSpan.FromHours(9), "JST", "JST");
        Check(AlarmTime.NextLocal(TimeSpan.Zero, now, zone).Date == now.AddDays(1).Date, "midnight rollover");
        Check(AlarmTime.NextLocal(now.TimeOfDay, now, zone) == now.AddDays(1), "equal time is tomorrow");
        Check(AlarmTime.IsHeadphoneFormFactor(3) && AlarmTime.IsHeadphoneFormFactor(5), "headphones and headsets allowed");
        Check(!AlarmTime.IsHeadphoneFormFactor(1) && !AlarmTime.IsHeadphoneFormFactor(9) && !AlarmTime.IsHeadphoneFormFactor(10), "speakers display and unknown denied");
        var session = new AlarmSession();
        bool rejected = false; try { session.Arm("default", now.AddMinutes(1), false, now, TimeSpan.Zero); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "default output rejected");
        session.Arm("headphones-1", now.AddSeconds(10), false, now, TimeSpan.Zero);
        Check(!session.Poll(now.AddSeconds(9), TimeSpan.FromSeconds(9), true), "not early");
        Check(session.Poll(now.AddSeconds(10), TimeSpan.FromSeconds(10), true), "fires once when due");
        Check(!session.Poll(now.AddSeconds(11), TimeSpan.FromSeconds(11), true), "no double firing");
        session.Poll(now.AddSeconds(12), TimeSpan.FromSeconds(12), false);
        Check(session.State == AlarmState.StoppedForSafety, "disconnect while ringing stops");
        Check(!session.Poll(now.AddSeconds(13), TimeSpan.FromSeconds(13), true), "reconnect never resumes");
        session.Arm("headphones-1", now.AddSeconds(10), true, now, TimeSpan.FromSeconds(100));
        Check(!session.Poll(now.AddHours(1), TimeSpan.FromSeconds(105), true), "duration immune to wall clock jump");
        Check(session.Poll(now, TimeSpan.FromSeconds(110), true), "duration uses monotonic clock");
        session.Arm("headphones-1", now.AddSeconds(10), false, now, TimeSpan.Zero);
        session.Poll(now.AddSeconds(9), TimeSpan.FromSeconds(9), false);
        Check(!session.Poll(now.AddSeconds(10), TimeSpan.FromSeconds(10), true), "disconnect before due blocks alarm");
        session.Arm("headphones-1", now.AddSeconds(10), false, now, TimeSpan.Zero);
        Check(!session.Poll(now.AddHours(1), TimeSpan.FromHours(1), true) && session.State == AlarmState.StoppedForSafety, "missed alarm never sounds late");
        session.Arm("headphones-1", now.AddSeconds(10), false, now, TimeSpan.Zero); session.Stop();
        Check(!session.Poll(now.AddSeconds(10), TimeSpan.FromSeconds(10), true), "manual stop cancels");
        Console.WriteLine("Passed " + count + " tests.");
    }
}
