using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingTwoAnchorAdapterTests
{
    [Fact]
    public void SupportedTwoCubicLensProducesClosedNondegenerateBoundedPolygon()
    {
        var contour = new MaterialContour("lens", MaterialContourKind.OuterMaterial, true,
            [new("left", new(0, 0), new(0, -4), new(0, 4), true),
             new("right", new(10, 0), new(0, 4), new(0, -4), true)],
            [new("upper", "left", "right", MaterialSegmentKind.CubicBezier),
             new("lower", "right", "left", MaterialSegmentKind.CubicBezier)], []);
        var converted = new GradingPolygonizer(new(24, 200)).Convert(contour);
        Assert.True(converted.Ring.Points.Count > 4);
        Assert.Equal(converted.Ring.Points[0], converted.Ring.Points[^1]);
        Assert.InRange(converted.MaximumDeviationMm, 0m, 0.025m);
        Assert.Contains(converted.Segments, s => s.SegmentId == "upper");
        Assert.Contains(converted.Segments, s => s.SegmentId == "lower");
    }
}
