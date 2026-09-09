using System;
using System.Collections;
using System.Threading;
using Elts.Clock;
using Elts.Tracking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Elts.Tests
{
    // A Unity Update consumer: test clock advances by one 250 Hz acquisition period.
    public sealed class TrackingFrameProbe : MonoBehaviour
    {
        public ManualSharedClock Clock;
        public SyntheticTrackingSource Source;
        public TrackingSample Head, Weapon;
        public int Frames, ThreadId;
        public bool Paired = true;
        private void Update()
        {
            if (Source == null) return;
            Clock.Advance(TimeSpan.FromMilliseconds(4));
            bool head = Source.TryGetNext(out Head);
            bool weapon = Source.TryGetNext(out Weapon);
            Paired &= head && weapon && Head.Sequence == Weapon.Sequence
                && Head.Timestamp == Weapon.Timestamp && !Head.Tracker.Equals(Weapon.Tracker);
            Frames++;
            ThreadId = Thread.CurrentThread.ManagedThreadId;
        }
    }

    public sealed class TrackingFrameTests
    {
        [UnityTest]
        public IEnumerator SyntheticPairsRemainRawAndSynchronizedAcrossUnityFrames()
        {
            var clock = new ManualSharedClock(DateTimeOffset.UtcNow);
            var source = new SyntheticTrackingSource(new SyntheticTrackingSettings(clock));
            var go = new GameObject("Synthetic acquisition smoke probe");
            var probe = go.AddComponent<TrackingFrameProbe>();
            probe.Clock = clock;
            probe.Source = source;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                yield return null;
                yield return null;
                var previous = probe.Head;
                long originalTicks = previous.Timestamp.Ticks;
                yield return null;
                yield return null;
                Assert.That(probe.Frames, Is.GreaterThanOrEqualTo(3));
                Assert.That(probe.ThreadId, Is.EqualTo(mainThread));
                Assert.That(probe.Paired, Is.True);
                Assert.That(probe.Head.HasValidPose && probe.Weapon.HasValidPose, Is.True);
                Assert.That(probe.Head.Sequence, Is.GreaterThan(previous.Sequence));
                Assert.That(previous.Timestamp.Ticks, Is.EqualTo(originalTicks));
                Assert.That(probe.Head.Timestamp, Is.EqualTo(clock.Now));
            }
            finally
            {
                probe.Source = null;
                source.Dispose();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}

