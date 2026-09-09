#nullable enable
using System;
using Elts.Clock;
using Elts.Geometry;

namespace Elts.Tracking
{
    /// <summary>
    /// Main-thread input bridge for a desktop mouse/keyboard participant mode.
    /// A publication is an immutable head/weapon pair; polling snapshots that
    /// pair once, so a concurrent publication cannot split one acquisition.
    /// This source contains no cursor mapping policy and never extrapolates.
    /// </summary>
    public sealed class DesktopTrackingSource : ITrackingSource
    {
        public const string SourceIdentity = "desktop-mouse-keyboard-room-v1";

        private readonly object sync = new object();
        private readonly TrackerId headTracker;
        private readonly TrackerId weaponTracker;
        private readonly TimeSpan maximumInputAge;
        private RigidPose? publishedHead;
        private RigidPose? publishedWeapon;
        private MonotonicTimestamp publishedAt;
        private bool hasPublication;
        private int pendingCount;
        private TrackingSample pendingHead;
        private TrackingSample pendingWeapon;
        private long sequence;
        private bool disposed;

        public DesktopTrackingSource(ISharedClock clock, string headTracker, string weaponTracker, TimeSpan maximumInputAge)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (String.IsNullOrWhiteSpace(headTracker)) throw new ArgumentException("A head tracker identifier is required.", nameof(headTracker));
            if (String.IsNullOrWhiteSpace(weaponTracker)) throw new ArgumentException("A weapon tracker identifier is required.", nameof(weaponTracker));
            if (String.Equals(headTracker, weaponTracker, StringComparison.Ordinal)) throw new ArgumentException("Head and weapon tracker identifiers must differ.");
            if (maximumInputAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumInputAge));
            this.headTracker = new TrackerId(headTracker);
            this.weaponTracker = new TrackerId(weaponTracker);
            this.maximumInputAge = maximumInputAge;
        }

        public ISharedClock Clock { get; }
        public TrackingSourceIdentity Source => new TrackingSourceIdentity(SourceIdentity);

        /// <summary>Publishes the latest complete input state. Null means unavailable.</summary>
        public void Publish(RigidPose? head, RigidPose? weapon)
        {
            lock (sync)
            {
                ThrowIfDisposed();
                ValidatePose(head, nameof(head));
                ValidatePose(weapon, nameof(weapon));
                publishedHead = head;
                publishedWeapon = weapon;
                publishedAt = Clock.Now;
                hasPublication = true;
            }
        }

        public bool TryGetNext(out TrackingSample sample)
        {
            lock (sync)
            {
                if (disposed) { sample = default; return false; }
                if (pendingCount == 0)
                {
                    var stamp = TrackingAcquisitionStamp.Capture(Clock, checked(sequence + 1));
                    sequence++;
                    bool usable = hasPublication && Clock.Now.Ticks - publishedAt.Ticks <= maximumInputAge.Ticks;
                    pendingHead = usable && publishedHead.HasValue
                        ? TrackingSample.Valid(stamp, headTracker, publishedHead.Value)
                        : TrackingSample.Invalid(stamp, headTracker, TrackingConnectionState.Disconnected, TrackingValidity.Unavailable);
                    pendingWeapon = usable && publishedWeapon.HasValue
                        ? TrackingSample.Valid(stamp, weaponTracker, publishedWeapon.Value)
                        : TrackingSample.Invalid(stamp, weaponTracker, TrackingConnectionState.Disconnected, TrackingValidity.Unavailable);
                    pendingCount = 2;
                }
                if (pendingCount == 2)
                {
                    sample = pendingHead;
                }
                else
                {
                    sample = pendingWeapon;
                }
                pendingCount--;
                return true;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                disposed = true;
                publishedHead = null;
                publishedWeapon = null;
                pendingCount = 0;
            }
        }

        private static void ValidatePose(RigidPose? pose, string name)
        {
            if (pose.HasValue && (!pose.Value.Position.IsFinite || !pose.Value.Orientation.IsUnit))
                throw new ArgumentException("A published pose must be finite and use a unit orientation.", name);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DesktopTrackingSource));
        }
    }
}
