using MechPilot.SwMcp.Tools.Internal;

namespace MechPilot.SwMcp.Tests;

/// <summary>
/// L1 tests for <see cref="ComponentTransform.BuildTransformArray"/> — the pure
/// (SolidWorks-free) rotation+translation math behind M53-① component
/// orientation. The 16-element ArrayData is column-major: data[0..2] is the
/// component's local X axis in assembly space, [3..5] = local Y, [6..8] =
/// local Z, [9..11] = translation (metres), [12] = scale, [13..15] = 0.
/// </summary>
public sealed class ComponentTransformTests
{
    private const double Tol = 1e-9;

    private static (double X, double Y, double Z) Col0(double[] d) => (d[0], d[1], d[2]);
    private static (double X, double Y, double Z) Col1(double[] d) => (d[3], d[4], d[5]);
    private static (double X, double Y, double Z) Col2(double[] d) => (d[6], d[7], d[8]);

    private static void AssertVec(
        (double X, double Y, double Z) actual, double x, double y, double z)
    {
        Assert.Equal(x, actual.X, Tol);
        Assert.Equal(y, actual.Y, Tol);
        Assert.Equal(z, actual.Z, Tol);
    }

    [Fact]
    public void Identity_when_no_rotation_no_translation()
    {
        var d = ComponentTransform.BuildTransformArray(0, 0, 0, 0, 0, 0);

        Assert.Equal(16, d.Length);
        AssertVec(Col0(d), 1, 0, 0);
        AssertVec(Col1(d), 0, 1, 0);
        AssertVec(Col2(d), 0, 0, 1);
        Assert.Equal(0, d[9], Tol);
        Assert.Equal(0, d[10], Tol);
        Assert.Equal(0, d[11], Tol);
        Assert.Equal(1, d[12], Tol);   // scale
        Assert.Equal(0, d[13], Tol);
        Assert.Equal(0, d[14], Tol);
        Assert.Equal(0, d[15], Tol);
    }

    [Fact]
    public void Translation_is_mm_to_metres_in_slots_9_to_11()
    {
        var d = ComponentTransform.BuildTransformArray(0, 0, 0, 30, -20, 10);

        Assert.Equal(0.030, d[9], Tol);
        Assert.Equal(-0.020, d[10], Tol);
        Assert.Equal(0.010, d[11], Tol);
        // Rotation block stays identity.
        AssertVec(Col0(d), 1, 0, 0);
    }

    [Fact]
    public void Rotate_90_about_z_maps_local_x_to_assembly_plus_y()
    {
        var d = ComponentTransform.BuildTransformArray(0, 0, 90, 0, 0, 0);

        AssertVec(Col0(d), 0, 1, 0);    // local +X → assembly +Y
        AssertVec(Col1(d), -1, 0, 0);   // local +Y → assembly -X
        AssertVec(Col2(d), 0, 0, 1);    // local +Z unchanged
    }

    [Fact]
    public void Rotate_90_about_y_maps_local_x_to_assembly_minus_z()
    {
        var d = ComponentTransform.BuildTransformArray(0, 90, 0, 0, 0, 0);

        AssertVec(Col0(d), 0, 0, -1);   // local +X → assembly -Z
        AssertVec(Col1(d), 0, 1, 0);    // local +Y unchanged
        AssertVec(Col2(d), 1, 0, 0);    // local +Z → assembly +X
    }

    [Fact]
    public void Rotate_90_about_x_maps_local_y_to_assembly_plus_z()
    {
        var d = ComponentTransform.BuildTransformArray(90, 0, 0, 0, 0, 0);

        AssertVec(Col0(d), 1, 0, 0);    // local +X unchanged
        AssertVec(Col1(d), 0, 0, 1);    // local +Y → assembly +Z
        AssertVec(Col2(d), 0, -1, 0);   // local +Z → assembly -Y
    }

    [Fact]
    public void Rotation_columns_stay_orthonormal_for_composite_angles()
    {
        var d = ComponentTransform.BuildTransformArray(45, 30, 60, 5, -7, 12);
        var c0 = Col0(d);
        var c1 = Col1(d);
        var c2 = Col2(d);

        // Unit length.
        Assert.Equal(1, Norm(c0), Tol);
        Assert.Equal(1, Norm(c1), Tol);
        Assert.Equal(1, Norm(c2), Tol);
        // Mutually perpendicular.
        Assert.Equal(0, Dot(c0, c1), Tol);
        Assert.Equal(0, Dot(c1, c2), Tol);
        Assert.Equal(0, Dot(c0, c2), Tol);
        // Translation still carried independently.
        Assert.Equal(0.005, d[9], Tol);
        Assert.Equal(-0.007, d[10], Tol);
        Assert.Equal(0.012, d[11], Tol);
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(90, 0, 0, true)]
    [InlineData(0, -45, 0, true)]
    [InlineData(0, 0, 0.001, true)]
    public void HasRotation_detects_any_nonzero_angle(
        double rx, double ry, double rz, bool expected)
    {
        Assert.Equal(expected, ComponentTransform.HasRotation(rx, ry, rz));
    }

    private static double Norm((double X, double Y, double Z) v) =>
        Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

    private static double Dot(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
}
