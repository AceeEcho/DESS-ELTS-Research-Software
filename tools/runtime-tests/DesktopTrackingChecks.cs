#nullable enable
using System;
using Elts.Clock;
using Elts.Geometry;
using Elts.Tracking;

static class DesktopTrackingChecks
{
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; }
    static void Throws<T>(Action action, string name) where T : Exception { try { action(); throw new Exception("FAIL: " + name + " did not throw"); } catch (T) { passed++; } }

    static void Main()
    {
        var clock = new ManualSharedClock(new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));
        var source = new DesktopTrackingSource(clock, "DESKTOP-HEAD", "DESKTOP-WEAPON", TimeSpan.FromMilliseconds(100));
        var head = new RigidPose(new Vector3d(0, 1.6, 0), Quaterniond.Identity);
        var weapon = new RigidPose(new Vector3d(0, 1.4, 1), Quaterniond.Identity);
        True(source.Source.Value == DesktopTrackingSource.SourceIdentity, "source identity is stable");

        source.Publish(head, weapon);
        True(source.TryGetNext(out var firstHead), "first sample is available");
        var newerHead = new RigidPose(new Vector3d(2, 1.6, 0), Quaterniond.Identity);
        source.Publish(newerHead, weapon);
        True(source.TryGetNext(out var firstWeapon), "paired second sample is available");
        True(firstHead.Sequence == firstWeapon.Sequence && firstHead.Timestamp == firstWeapon.Timestamp, "pair shares sequence and timestamp");
        True(firstHead.Pose!.Value.Position.X == 0, "pair keeps the first latched head");
        True(source.TryGetNext(out var secondHead), "next pair is available");
        True(secondHead.Pose!.Value.Position.X == 2, "next pair observes newer publication");
        True(secondHead.Sequence > firstHead.Sequence, "sequence is monotonic");
        True(secondHead.Timestamp == firstWeapon.Timestamp, "manual clock remains the timestamp authority");
        True(source.TryGetNext(out var secondWeapon), "next pair weapon is available");
        True(secondHead.Sequence == secondWeapon.Sequence, "new pair remains paired");

        clock.Advance(TimeSpan.FromMilliseconds(101));
        True(source.TryGetNext(out var staleHead), "stale head is emitted");
        True(source.TryGetNext(out var staleWeapon), "stale weapon is emitted");
        True(!staleHead.HasValidPose && !staleWeapon.HasValidPose && staleHead.Validity == TrackingValidity.Unavailable, "stale input becomes explicit invalid data");
        source.Publish(head, weapon);
        True(source.TryGetNext(out var recoveredHead) && recoveredHead.HasValidPose, "fresh publication recovers input");
        True(source.TryGetNext(out var recoveredWeapon) && recoveredWeapon.HasValidPose, "fresh weapon recovers with head");

        Throws<ArgumentException>(() => source.Publish(new RigidPose(Vector3d.Zero, default), weapon), "default orientation is rejected");
        Throws<ArgumentOutOfRangeException>(() => new DesktopTrackingSource(clock, "h", "w", TimeSpan.Zero), "zero age is rejected");
        Throws<ArgumentException>(() => new DesktopTrackingSource(clock, "same", "same", TimeSpan.FromSeconds(1)), "duplicate tracker IDs are rejected");
        source.Dispose();
        True(!source.TryGetNext(out _), "disposed source stops polling");
        Throws<ObjectDisposedException>(() => source.Publish(head, weapon), "disposed source rejects publication");
        Console.WriteLine("PASS: " + passed + " desktop tracking checks");
    }
}
