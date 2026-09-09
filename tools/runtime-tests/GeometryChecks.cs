using System;
using Elts.Geometry;

static class GeometryChecks
{
    static int passed;
    static void True(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; }
    static void Near(double actual, double expected, double tolerance, string name) => True(Math.Abs(actual - expected) <= tolerance, $"{name}: {actual} != {expected}");
    static void Throws<T>(Action action, string name) where T : Exception { try { action(); throw new Exception("FAIL: " + name + " did not throw"); } catch (T) { passed++; } }
    static void Main()
    {
        var x = new Vector3d(1, 0, 0); var y = new Vector3d(0, 1, 0); var z = new Vector3d(0, 0, 1);
        True(Vector3d.Cross(x, y).Equals(z), "cross product");
        var q = Quaterniond.FromAxisAngle(z, Math.PI / 2); var rotated = q.Rotate(x); Near(rotated.X, 0, 1e-12, "quarter turn x"); Near(rotated.Y, 1, 1e-12, "quarter turn y");
        var pose = new RigidPose(new Vector3d(2, 3, 4), q); var local = new Vector3d(0.5, -1, 2); var world = pose.TransformPoint(local); var recovered = pose.InverseTransformPoint(world); Near((recovered - local).Length, 0, 1e-12, "pose inverse");
        Near(GeometryMath.AngularErrorDegrees(x, y), 90, 1e-12, "angular error"); Near(GeometryMath.AngularErrorDegrees(x, new Vector3d(-1, 0, 0)), 180, 1e-12, "opposite angular error");
        var bore = new BoreRay(new RigidPose(Vector3d.Zero, Quaterniond.Identity), new Vector3d(0, 0, 0.2), Quaterniond.Identity, z); True(bore.Ray.Origin.Equals(new Vector3d(0, 0, 0.2)), "bore origin"); True(bore.Ray.Direction.Equals(z), "bore direction");
        var screen = new ScreenPlane(new Vector3d(0, 0, 2), x, y, 2, 1); var projection = screen.Project(new Vector3d(0, 0, 0), new Vector3d(1.5, 0.75, 3)); Near(projection.U, 1, 1e-12, "projection u"); Near(projection.V, 0.5, 1e-12, "projection v"); True(projection.OnScreen, "projection inside"); True(!screen.Project(Vector3d.Zero, new Vector3d(4.5, 0, 3)).OnScreen, "projection outside");
        True(screen.Intersect(new Ray3d(Vector3d.Zero, z), out var distance, out _, out _, out _), "plane intersection"); Near(distance, 2, 1e-12, "plane distance"); True(!screen.Intersect(new Ray3d(Vector3d.Zero, x), out _, out _, out _, out _), "parallel rejection");
        Throws<ArgumentException>(() => new Vector3d(0, 0, 0).Normalized(), "zero vector"); Throws<ArgumentException>(() => GeometryMath.AngularErrorDegrees(x, Vector3d.Zero), "zero angular input"); Throws<ArgumentException>(() => new Vector3d(double.NaN, 0, 0).Normalized(), "nonfinite vector"); Throws<ArgumentException>(() => new ScreenPlane(Vector3d.Zero, x, x, 1, 1), "invalid basis");
        Near(new Vector3d(1e308, 0, 0).Normalized().X, 1, 1e-12, "large finite vector normalization");
        Near(new Quaterniond(0, 0, 0, 1e308).W, 1, 1e-12, "large finite quaternion normalization");
        Throws<ArgumentException>(() => new RigidPose(Vector3d.Zero, default), "default quaternion pose rejection");
        Throws<ArgumentException>(() => default(Quaterniond).Rotate(x), "default quaternion rotation rejection");
        Throws<ArgumentException>(() => x.Normalized(-1), "negative numerical tolerance rejection");
        Throws<ArgumentException>(() => screen.Intersect(default, out _, out _, out _, out _), "default ray rejection");
        var offAxisEye = new Vector3d(-0.7, 0.4, -0.5);
        foreach (var corner in new[] { new Vector3d(0, 0, 2), new Vector3d(2, 0, 2), new Vector3d(0, 1, 2), new Vector3d(2, 1, 2) })
        {
            var projectedCorner = screen.Project(offAxisEye, corner);
            True(projectedCorner.OnScreen, "off-axis corner inside numerical bounds");
            Near(projectedCorner.Distance, (corner - offAxisEye).Length, 1e-12, "projection distance is meters");
            Near(projectedCorner.U, corner.X, 1e-12, "off-axis corner u meters");
            Near(projectedCorner.V, corner.Y, 1e-12, "off-axis corner v meters");
        }
        True(!screen.Project(Vector3d.Zero, new Vector3d(0, 0, -1)).OnScreen, "behind-eye projection excluded");
        var corrected = new BoreRay(new RigidPose(Vector3d.Zero, Quaterniond.Identity), Vector3d.Zero,
            Quaterniond.FromAxisAngle(y, Math.PI / 2), z);
        Near(GeometryMath.AngularErrorDegrees(corrected.Ray.Direction, x), 0, 1e-12, "nonidentity weapon zero correction");
        Console.WriteLine($"PASS: {passed} geometry checks");
    }
}
