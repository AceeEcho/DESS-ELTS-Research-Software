using System;

namespace Elts.Geometry
{

/// <summary>Immutable double precision vector in Unity room coordinates (meters).</summary>
public readonly struct Vector3d : IEquatable<Vector3d>
{
    public readonly double X, Y, Z;
    public Vector3d(double x, double y, double z)
    {
        if (!Finite(x) || !Finite(y) || !Finite(z))
            throw new ArgumentException("Vector components must be finite.");
        X = x; Y = y; Z = z;
    }
    public static Vector3d Zero => new(0, 0, 0);
    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);
    public bool IsFinite => Finite(X) && Finite(Y) && Finite(Z);
    public Vector3d Normalized(double tolerance = GeometryTolerance.Length)
    {
        RequireFinite(this, nameof(Vector3d));
        if (!Finite(tolerance) || tolerance < 0)
            throw new ArgumentException("Normalization tolerance must be finite and nonnegative.");
        // Scale first: squaring a large finite component must not create an
        // infinite norm and silently turn a direction into the zero vector.
        var scale = Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
        if (scale == 0) throw new ArgumentException("Vector is zero or degenerate.");
        var scaled = this / scale;
        var scaledLength = scaled.Length;
        if (!(scale > tolerance / scaledLength))
            throw new ArgumentException("Vector is zero or degenerate.");
        return scaled / scaledLength;
    }
    public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vector3d Cross(Vector3d a, Vector3d b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3d operator -(Vector3d a) => new(-a.X, -a.Y, -a.Z);
    public static Vector3d operator *(Vector3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vector3d operator *(double s, Vector3d a) => a * s;
    public static Vector3d operator /(Vector3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public bool Equals(Vector3d other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Vector3d other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X:G17}, {Y:G17}, {Z:G17})";
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static void RequireFinite(Vector3d value, string name) { if (!value.IsFinite) throw new ArgumentException($"{name} must be finite."); }
}

public static class GeometryTolerance
{
    public const double Length = 1e-12;
    public const double Basis = 1e-10;
}

/// <summary>Unit quaternion rotation. Values are normalized at construction.</summary>
public readonly struct Quaterniond : IEquatable<Quaterniond>
{
    public readonly double X, Y, Z, W;
    public Quaterniond(double x, double y, double z, double w)
    {
        if (!Vector3d.Finite(x) || !Vector3d.Finite(y) || !Vector3d.Finite(z) || !Vector3d.Finite(w)) throw new ArgumentException("Quaternion must be finite.");
        var scale = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Max(Math.Abs(z), Math.Abs(w)));
        if (scale == 0) throw new ArgumentException("Quaternion is degenerate.");
        x /= scale; y /= scale; z /= scale; w /= scale;
        var n = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (!(scale > GeometryTolerance.Length / n)) throw new ArgumentException("Quaternion is degenerate.");
        X = x / n; Y = y / n; Z = z / n; W = w / n;
    }
    public bool IsUnit => Vector3d.Finite(X) && Vector3d.Finite(Y) && Vector3d.Finite(Z) && Vector3d.Finite(W)
        && Math.Abs(X * X + Y * Y + Z * Z + W * W - 1) <= GeometryTolerance.Basis;
    internal void RequireUnit()
    {
        // default(struct) bypasses constructors. Never treat its zero rotation
        // as an identity quaternion or a valid tracker orientation.
        if (!IsUnit) throw new ArgumentException("A valid unit quaternion is required; default quaternion is invalid.");
    }
    public static Quaterniond Identity => new(0, 0, 0, 1);
    public static Quaterniond FromAxisAngle(Vector3d axis, double radians)
    {
        var a = axis.Normalized();
        var half = radians * 0.5;
        var s = Math.Sin(half);
        return new(a.X * s, a.Y * s, a.Z * s, Math.Cos(half));
    }
    public Quaterniond Inverse => new(-X, -Y, -Z, W);
    public Vector3d Rotate(Vector3d value)
    {
        RequireUnit();
        Vector3d.RequireFinite(value, nameof(value));
        var q = new Vector3d(X, Y, Z);
        var t = 2 * Vector3d.Cross(q, value);
        return value + W * t + Vector3d.Cross(q, t);
    }
    public static Quaterniond operator *(Quaterniond a, Quaterniond b) => new(a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y, a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X, a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W, a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    public bool Equals(Quaterniond other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
    public override bool Equals(object? obj) => obj is Quaterniond other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
}

public readonly struct RigidPose
{
    public readonly Vector3d Position;
    public readonly Quaterniond Orientation;
    public RigidPose(Vector3d position, Quaterniond orientation)
    {
        Vector3d.RequireFinite(position, nameof(position));
        orientation.RequireUnit();
        Position = position; Orientation = orientation;
    }
    public Vector3d TransformPoint(Vector3d local) => Position + Orientation.Rotate(local);
    public Vector3d TransformDirection(Vector3d local) => Orientation.Rotate(local);
    public Vector3d InverseTransformPoint(Vector3d world) => Orientation.Inverse.Rotate(world - Position);
    public RigidPose Inverse => new(Orientation.Inverse.Rotate(-Position), Orientation.Inverse);
}

public readonly struct Ray3d
{
    public readonly Vector3d Origin, Direction;
    public Ray3d(Vector3d origin, Vector3d direction) { Vector3d.RequireFinite(origin, nameof(origin)); Origin = origin; Direction = direction.Normalized(); }
    public Vector3d At(double distance) => Origin + Direction * distance;
}

public readonly struct BoreRay
{
    public readonly Ray3d Ray;
    public BoreRay(RigidPose weaponPose, Vector3d muzzleOffset, Quaterniond zeroCorrection, Vector3d boreDirection)
    {
        Ray = new Ray3d(weaponPose.TransformPoint(muzzleOffset), (weaponPose.Orientation * zeroCorrection).Rotate(boreDirection));
    }
}

public readonly struct ScreenProjection
{
    /// <summary>Forward distance from eye to the screen intersection, in meters.</summary>
    public readonly double Distance, U, V;
    public readonly Vector3d Point;
    public bool OnScreen { get; }
    public ScreenProjection(double distance, double u, double v, Vector3d point, bool onScreen) { Distance = distance; U = u; V = v; Point = point; OnScreen = onScreen; }
}

/// <summary>Rectangle basis, without asserting measured calibration. U/V bounds are meters.</summary>
public readonly struct ScreenPlane
{
    public readonly Vector3d Origin, U, V, Normal;
    public readonly double Width, Height;
    public ScreenPlane(Vector3d origin, Vector3d u, Vector3d v, double width, double height)
    {
        Vector3d.RequireFinite(origin, nameof(origin));
        if (!Vector3d.Finite(width) || !Vector3d.Finite(height) || !(width > GeometryTolerance.Length) || !(height > GeometryTolerance.Length)) throw new ArgumentException("Screen dimensions must be finite and positive.");
        U = u.Normalized(); V = v.Normalized();
        if (Math.Abs(Vector3d.Dot(U, V)) > GeometryTolerance.Basis) throw new ArgumentException("Screen basis axes must be perpendicular.");
        Normal = Vector3d.Cross(U, V).Normalized(); Origin = origin; Width = width; Height = height;
    }
    public ScreenProjection Project(Vector3d eye, Vector3d worldPoint)
    {
        Vector3d.RequireFinite(eye, nameof(eye)); Vector3d.RequireFinite(worldPoint, nameof(worldPoint));
        var direction = (worldPoint - eye).Normalized();
        var denominator = Vector3d.Dot(direction, Normal);
        if (!(Math.Abs(denominator) > GeometryTolerance.Basis)) throw new ArgumentException("View ray is parallel to screen plane.");
        var distance = Vector3d.Dot(Origin - eye, Normal) / denominator;
        if (!(distance > GeometryTolerance.Length)) return new ScreenProjection(distance, double.NaN, double.NaN, worldPoint, false);
        var point = eye + direction * distance;
        var offset = point - Origin;
        var u = Vector3d.Dot(offset, U); var v = Vector3d.Dot(offset, V);
        return new ScreenProjection(distance, u, v, point, IsInside(u, v));
    }
    public bool Intersect(Ray3d ray, out double distance, out Vector3d point, out double u, out double v)
    {
        // Validate even a default Ray3d, whose constructor was never called.
        var direction = ray.Direction.Normalized();
        var denominator = Vector3d.Dot(direction, Normal);
        distance = Vector3d.Dot(Origin - ray.Origin, Normal) / denominator;
        if (!(Math.Abs(denominator) > GeometryTolerance.Basis) || !(distance > GeometryTolerance.Length)) { point = Vector3d.Zero; u = v = double.NaN; return false; }
        point = ray.At(distance); var offset = point - Origin; u = Vector3d.Dot(offset, U); v = Vector3d.Dot(offset, V); return true;
    }
    public bool IsInside(double u, double v) => Vector3d.Finite(u) && Vector3d.Finite(v)
        && u >= -GeometryTolerance.Basis && u <= Width + GeometryTolerance.Basis
        && v >= -GeometryTolerance.Basis && v <= Height + GeometryTolerance.Basis;
}

public static class GeometryMath
{
    public static double AngularErrorDegrees(Vector3d boreDirection, Vector3d muzzleToTarget)
    {
        var a = boreDirection.Normalized(); var b = muzzleToTarget.Normalized();
        return Math.Atan2(Vector3d.Cross(a, b).Length, Vector3d.Dot(a, b)) * (180.0 / Math.PI);
    }
}
}
