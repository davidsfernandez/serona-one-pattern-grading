using System.Collections.Immutable;
using GestionProduccion.Grading.Core;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Tests;

public sealed class AuthoritativeTopologyTests
{
    [Fact] public void CubicInteriorLoopWithDistinctEndpointsIsNotApprovable()
    {
        // This synthetic polynomial repeats the origin at t=1/4 and 3/4; its endpoints are distinct.
        var doc = CubicRectangle([new(9, -4.5m), new(9, 4.5m), new(30, 4.5m), new(30, -4.5m)],
            0, new(-16, 11), new(-16, -11));
        var result = Calculate(doc);
        Assert.False(result.IsApprovable);
        Assert.Contains(result.Diagnostics, d => d.EntityId is not null && d.ContourId == "outer");
    }

    [Fact] public void NonAdjacentInteriorTangencyIsRejectedEvenWithoutCrossing()
    {
        var doc = CubicRectangle([new(0, 0), new(30, 0), new(30, 30), new(0, 30)],
            2, new(-10, -40), new(10, -40));
        Assert.False(Calculate(doc).IsApprovable);
    }

    [Fact] public void PositiveGapIsNotSnappedIntoTheNearbyTangency()
    {
        var doc = CubicRectangle([new(0, 0), new(30, 0), new(30, 30), new(0, 30)],
            2, new(-10, -39.999m), new(10, -39.999m));
        Assert.True(Calculate(doc).IsApprovable);
    }

    [Fact] public void CollinearCubicWithRepeatedHandlesIsValidWithoutSimplification()
    {
        var doc = CubicRectangle([new(0, 0), new(20, 0), new(20, 10), new(0, 10)], 0, default, default);
        var result = Calculate(doc); Assert.True(result.IsApprovable);
        Assert.Equal(MaterialSegmentKind.CubicBezier, Assert.Single(result.Geometry).Contour.Segments[0].Kind);
    }

    [Fact] public void AdjacentOverlapCannotBeAuthorizedByTheSharedEndpoint()
    {
        var doc = CubicRectangle([new(0, 0), new(20, 0), new(10, 0), new(0, 10)], 0, default, default);
        Assert.False(Calculate(doc).IsApprovable);
    }

    [Fact] public void ClosingAdjacencyIsCheckedAndZeroAreaIsRejected()
    {
        var doc = CubicRectangle([new(0, 0), new(10, 0), new(20, 0), new(30, 0)], 0, default, default);
        Assert.Contains(Calculate(doc).Diagnostics, d => d.Code == "grading_zero_area");
    }

    [Fact] public void InvalidSecondPieceCannotLeaveTheFirstPiecePublished()
    {
        var doc = SyntheticGeometry.Rectangle();
        var invalid = doc.Pieces[0] with { Id = "other", AvailableSizeIds = ["base"],
            MaterialContours = [doc.Pieces[0].MaterialContours[0] with { Nodes = doc.Pieces[0].MaterialContours[0].Nodes.SetItem(2,
                doc.Pieces[0].MaterialContours[0].Nodes[2] with { Position = new(-10, 5) }) }] };
        doc = doc with { Pieces = doc.Pieces.Add(invalid) };
        var result = Calculate(doc); Assert.False(result.IsApprovable); Assert.Empty(result.Geometry);
        Assert.Contains(result.Diagnostics, d => d.PieceId == "other");
    }

    [Fact] public void NumericOverflowIsDiagnosedInsteadOfChangingCoordinates()
    {
        var doc = SyntheticGeometry.WithRule(SyntheticGeometry.Rectangle(), "n2", "large", new(decimal.MaxValue, 0));
        Assert.Contains(Calculate(doc).Diagnostics, d => d.Code == "grading_numeric_capacity_exceeded");
    }

    [Fact] public void OneCubicAndClosingLineMayFormOneValidOuterContourWithTwoAnchors()
    {
        var doc = SyntheticGeometry.Rectangle();
        var c = new MaterialContour("outer", MaterialContourKind.OuterMaterial, true,
            [new("a", new(0, 0), default, new(0, 10), true), new("b", new(20, 0), new(0, 10), default, true)],
            [new("curve", "a", "b", MaterialSegmentKind.CubicBezier), new("close", "b", "a", MaterialSegmentKind.Line)],
            [new("span", "a", "b", ["curve"])]);
        doc = doc with { Pieces = [doc.Pieces[0] with { MaterialContours = [c], AvailableSizeIds = ["base"] }], Rules = [] };
        Assert.True(Calculate(doc).IsApprovable);
    }

    private static GradingCalculation Calculate(InternalGeometryDocument doc) => new GradingEngine().Calculate(doc, new(24, 20000));

    private static InternalGeometryDocument CubicRectangle(GeometryPoint[] points, int segment, GeometryPoint outgoing, GeometryPoint incoming)
    {
        var doc = SyntheticGeometry.Rectangle(); var c = doc.Pieces[0].MaterialContours[0];
        var nodes = c.Nodes.Select((n, i) => n with { Position = points[i] }).ToImmutableArray();
        nodes = nodes.SetItem(segment, nodes[segment] with { OutgoingHandle = outgoing })
            .SetItem(segment + 1, nodes[segment + 1] with { IncomingHandle = incoming });
        c = c with { Nodes = nodes, Segments = c.Segments.SetItem(segment, c.Segments[segment] with { Kind = MaterialSegmentKind.CubicBezier }),
            ControlSpans = [new("span", nodes[segment].Id, nodes[segment + 1].Id, [c.Segments[segment].Id])] };
        return SyntheticGeometry.WithContour(doc, c) with { Pieces = [doc.Pieces[0] with { MaterialContours = [c], AvailableSizeIds = ["base"] }] };
    }
}
