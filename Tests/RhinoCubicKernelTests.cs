using GestionProduccion.Application.ModelingGrading.GeometryKernel;

namespace GestionProduccion.Grading.Tests;

public sealed class RhinoCubicKernelTests
{
    [Fact]
    public void Evaluate_ReturnsPositionAndDerivativesForStraightCubic()
    {
        using var kernel = new RhinoCubicKernel(new CubicControls(
            new KernelPoint(0, 2), new KernelPoint(1, 2),
            new KernelPoint(2, 2), new KernelPoint(3, 2)));

        var evaluation = kernel.Evaluate(0.25);

        AssertPoint(new KernelPoint(0.75, 2), evaluation.Position);
        AssertPoint(new KernelPoint(3, 0), evaluation.FirstDerivative);
        AssertPoint(new KernelPoint(0, 0), evaluation.SecondDerivative);
        Assert.Equal(new KernelBounds(new KernelPoint(0, 2), new KernelPoint(3, 2)), kernel.Bounds());
    }

    [Fact]
    public void Evaluate_HandlesStrongInflectionAndReturnsDeterministicValues()
    {
        using var kernel = new RhinoCubicKernel(new CubicControls(
            new KernelPoint(0, 0), new KernelPoint(1, 4),
            new KernelPoint(2, -4), new KernelPoint(3, 0)));

        var first = kernel.Evaluate(0.5);
        var second = kernel.Evaluate(0.5);

        AssertPoint(new KernelPoint(1.5, 0), first.Position);
        AssertPoint(new KernelPoint(3, -6), first.FirstDerivative);
        AssertPoint(new KernelPoint(0, 0), first.SecondDerivative);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Evaluate_HandlesOrdinaryCurvedCubic()
    {
        using var kernel = new RhinoCubicKernel(new CubicControls(
            new KernelPoint(2, 1), new KernelPoint(3, 4),
            new KernelPoint(7, 5), new KernelPoint(9, 2)));

        var evaluation = kernel.Evaluate(0.4);

        AssertPoint(new KernelPoint(4.32, 3.512), evaluation.Position, 1e-10);
        AssertPoint(new KernelPoint(7.8, 3.24), evaluation.FirstDerivative, 1e-10);
        AssertPoint(new KernelPoint(6, -16.8), evaluation.SecondDerivative, 1e-10);
    }

    [Fact]
    public void Evaluate_HandlesStrongCurvature()
    {
        using var kernel = new RhinoCubicKernel(new CubicControls(
            new KernelPoint(0, 0), new KernelPoint(0, 100),
            new KernelPoint(100, 100), new KernelPoint(100, 0)));

        var evaluation = kernel.Evaluate(0.5);

        AssertPoint(new KernelPoint(50, 75), evaluation.Position, 1e-10);
        AssertPoint(new KernelPoint(150, 0), evaluation.FirstDerivative, 1e-10);
        AssertPoint(new KernelPoint(0, -600), evaluation.SecondDerivative, 1e-10);
    }

    [Fact]
    public void Evaluate_PreservesDecimalCoordinatesAtRepresentativeMillimeterMagnitude()
    {
        var start = new KernelPoint(1842.625, -937.375);
        var end = new KernelPoint(1918.8125, -901.0625);
        using var kernel = new RhinoCubicKernel(new CubicControls(
            start, new KernelPoint(1857.1875, -912.4375),
            new KernelPoint(1893.34375, -925.71875), end));

        AssertPoint(start, kernel.Evaluate(0).Position, 1e-10);
        AssertPoint(end, kernel.Evaluate(1).Position, 1e-10);
    }

    [Fact]
    public void Split_PreservesParentCurveOnEachLocalHalf()
    {
        var controls = new CubicControls(
            new KernelPoint(-2, 1), new KernelPoint(0, 5),
            new KernelPoint(4, -3), new KernelPoint(7, 2));
        using var parent = new RhinoCubicKernel(controls);
        var (left, right) = parent.Split();
        using (left)
        using (right)
        {
            AssertSplitEvaluationNear(parent.Evaluate(0.3), left.Evaluate(0.6));
            AssertSplitEvaluationNear(parent.Evaluate(0.8), right.Evaluate(0.6));
        }

        Assert.Equal(new CubicControls(
            new KernelPoint(-2, 1), new KernelPoint(0, 5),
            new KernelPoint(4, -3), new KernelPoint(7, 2)), controls);
    }

    [Fact]
    public void SplitChildrenRemainUsableAfterParentDisposal()
    {
        var parent = new RhinoCubicKernel(new CubicControls(
            new KernelPoint(0, 0), new KernelPoint(0, 3),
            new KernelPoint(4, 3), new KernelPoint(4, 0)));
        var (left, right) = parent.Split();
        parent.Dispose();

        using (left)
        using (right)
        {
            AssertPoint(new KernelPoint(0.625, 1.6875), left.Evaluate(0.5).Position, 1e-10);
            AssertPoint(new KernelPoint(3.375, 1.6875), right.Evaluate(0.5).Position, 1e-10);
        }
    }

    [Fact]
    public void Bounds_EncloseCurveSamplesInMillimeters()
    {
        using var kernel = new RhinoCubicKernel(new CubicControls(
            new KernelPoint(1.25, -0.5), new KernelPoint(3.75, 8.25),
            new KernelPoint(8.5, -6.75), new KernelPoint(12.125, 0.25)));

        var bounds = kernel.Bounds();

        for (var index = 0; index <= 100; index++)
        {
            var position = kernel.Evaluate(index / 100d).Position;
            Assert.InRange(position.X, bounds.Minimum.X - 1e-10, bounds.Maximum.X + 1e-10);
            Assert.InRange(position.Y, bounds.Minimum.Y - 1e-10, bounds.Maximum.Y + 1e-10);
        }
    }

    [Fact]
    public void Evaluate_IsStableForCoincidentAndNearlyDegenerateControls()
    {
        var coincidentControls = new CubicControls(
            new KernelPoint(4, -2), new KernelPoint(4, -2),
            new KernelPoint(4, -2), new KernelPoint(4, -2));
        using var coincident = new RhinoCubicKernel(coincidentControls);
        using var nearDegenerate = new RhinoCubicKernel(new CubicControls(
            new KernelPoint(0, 0), new KernelPoint(1e-12, -1e-12),
            new KernelPoint(2e-12, 1e-12), new KernelPoint(3e-12, 0)));

        var samePoint = coincident.Evaluate(0.73);
        AssertPoint(new KernelPoint(4, -2), samePoint.Position);
        AssertPoint(new KernelPoint(0, 0), samePoint.FirstDerivative);
        AssertPoint(new KernelPoint(0, 0), samePoint.SecondDerivative);
        Assert.Equal(new KernelBounds(new KernelPoint(4, -2), new KernelPoint(4, -2)), coincident.Bounds());
        var tinyEvaluation = nearDegenerate.Evaluate(0.5);
        AssertPoint(new KernelPoint(1.5e-12, 0), tinyEvaluation.Position, 1e-23);
        AssertPoint(new KernelPoint(3e-12, 1.5e-12), tinyEvaluation.FirstDerivative, 1e-23);
        AssertPoint(new KernelPoint(0, 0), tinyEvaluation.SecondDerivative, 1e-23);
    }

    [Fact]
    public void Constructor_RejectsNonFiniteControlCoordinates()
    {
        var controls = new CubicControls(
            new KernelPoint(0, 0), new KernelPoint(double.NaN, 1),
            new KernelPoint(2, 2), new KernelPoint(3, 3));

        Assert.Throws<ArgumentException>(() => new RhinoCubicKernel(controls));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Evaluate_RejectsInvalidParameter(double parameter)
    {
        using var kernel = new RhinoCubicKernel(new CubicControls(
            new KernelPoint(0, 0), new KernelPoint(1, 1),
            new KernelPoint(2, 1), new KernelPoint(3, 0)));

        Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Evaluate(parameter));
    }

    [Fact]
    public void Dispose_IsSafeToCallMoreThanOnce()
    {
        var kernel = new RhinoCubicKernel(new CubicControls(
            new KernelPoint(0, 0), new KernelPoint(1, 0),
            new KernelPoint(2, 0), new KernelPoint(3, 0)));

        kernel.Dispose();
        kernel.Dispose();

        Assert.Throws<ObjectDisposedException>(() => kernel.Evaluate(0.5));
    }

    private static void AssertPoint(KernelPoint expected, KernelPoint actual, double tolerance = 1e-10)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
    }

    private static void AssertSplitEvaluationNear(CubicEvaluation parent, CubicEvaluation child)
    {
        AssertPoint(parent.Position, child.Position, 1e-10);
        AssertPoint(Scale(parent.FirstDerivative, 0.5), child.FirstDerivative, 1e-10);
        AssertPoint(Scale(parent.SecondDerivative, 0.25), child.SecondDerivative, 1e-10);
    }

    private static KernelPoint Scale(KernelPoint point, double scale) =>
        new(point.X * scale, point.Y * scale);
}
