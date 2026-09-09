#nullable enable
using System;
using Elts.Config;
using Elts.Geometry;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>
    /// Development controls in the configured screen basis: U right, V up,
    /// Normal toward the target volume. All distances are metres. These are
    /// virtual controls, not a new measured rig or calibration authority.
    /// </summary>
    public sealed class DesktopInputModel
    {
        public const double MoveSpeedMetresPerSecond = 0.35;
        public const double InitialEyeDistanceMetres = 2;
        public const double MinimumEyeDistanceMetres = 1;
        public const double MaximumEyeDistanceMetres = 3;
        public const double LateralTravelMetres = 0.5;
        public const double VerticalTravelMetres = 0.35;
        public const double MaximumFrameStepSeconds = 0.1;
        private readonly DevelopmentConfiguration config;
        private Vector3 offset;
        public Vector2 Aim { get; private set; } = new Vector2(0.5f, 0.5f);
        public RigidPose Head { get; private set; }
        public RigidPose Weapon { get; private set; }

        public DesktopInputModel(DevelopmentConfiguration config)
        { this.config = config ?? throw new ArgumentNullException(nameof(config)); Rebuild(); }

        public void Reset() { offset = Vector3.zero; Aim = new Vector2(0.5f, 0.5f); Rebuild(); }
        public void SetAim(Vector2 normalizedBottomLeft)
        {
            if (!float.IsFinite(normalizedBottomLeft.x) || !float.IsFinite(normalizedBottomLeft.y))
                throw new ArgumentException("Aim coordinates must be finite.");
            Aim = new Vector2(Mathf.Clamp01(normalizedBottomLeft.x), Mathf.Clamp01(normalizedBottomLeft.y));
            Rebuild();
        }
        public void Move(Vector3 screenBasisDirection, double deltaSeconds)
        {
            if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            var direction = Vector3.ClampMagnitude(screenBasisDirection, 1);
            offset += direction * (float)(MoveSpeedMetresPerSecond * Math.Min(deltaSeconds, MaximumFrameStepSeconds));
            offset.x = Mathf.Clamp(offset.x, -(float)LateralTravelMetres, (float)LateralTravelMetres);
            offset.y = Mathf.Clamp(offset.y, -(float)VerticalTravelMetres, (float)VerticalTravelMetres);
            offset.z = Mathf.Clamp(offset.z, (float)(InitialEyeDistanceMetres-MaximumEyeDistanceMetres),
                (float)(InitialEyeDistanceMetres-MinimumEyeDistanceMetres));
            Rebuild();
        }
        private void Rebuild()
        {
            var rig = config.Rig;
            var screen = rig.Display;
            var eye = screen.Origin + screen.U * (screen.Width*0.5+offset.x)
                + screen.V * (screen.Height*0.5+offset.y) - screen.Normal * (InitialEyeDistanceMetres-offset.z);
            var headRotation = Quaternion.LookRotation(ToUnity(screen.Normal), ToUnity(screen.V));
            var headOrientation = ToRoom(headRotation);
            Head = new RigidPose(eye-headOrientation.Rotate(rig.HeadEyeOffsetM), headOrientation);

            // The virtual muzzle is at the viewing eye, giving a conventional
            // desktop reticle without parallax. Weapon orientation still owns
            // the bore ray; a real source later supplies an independent weapon
            // tracker pose through ITrackingSource, using these same rig offsets.
            var screenPoint = screen.Origin + screen.U*(screen.Width*Aim.x) + screen.V*(screen.Height*Aim.y);
            var direction = (screenPoint-eye).Normalized();
            var localBore = rig.WeaponZero.Rotate(rig.WeaponBoreLocalDirection);
            var weaponRotation = ToRoom(Quaternion.FromToRotation(ToUnity(localBore), ToUnity(direction)));
            Weapon = new RigidPose(eye-weaponRotation.Rotate(rig.WeaponMuzzleOffsetM), weaponRotation);
        }
        private static Vector3 ToUnity(Vector3d value) => new Vector3((float)value.X, (float)value.Y, (float)value.Z);
        private static Quaterniond ToRoom(Quaternion value) => new Quaterniond(value.x,value.y,value.z,value.w);
    }
}
