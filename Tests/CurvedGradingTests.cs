using System.Collections.Immutable;
using GestionProduccion.Grading.Core;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Tests;

public sealed class CurvedGradingTests
{
    private static GradingCalculation Calculate(InternalGeometryDocument doc) => new GradingEngine().Calculate(doc, new(24, 20000));

    [Fact] public void SimilarityRecalculatesIntermediateAnchorsAndRelativeHandlesFromBase()
    {
        var doc = SyntheticGeometry.WithRule(SyntheticGeometry.Curved(), "n2", "large", new(0, -4));
        var result = Calculate(doc);
        Assert.True(result.IsApprovable);
        var contour = result.Geometry.Single(g => g.SizeId == "large").Contour;
        Assert.Equal(new GeometryPoint(9.4m, -5), contour.Nodes[1].Position);
        Assert.Equal(new GeometryPoint(-3, .6m), contour.Nodes[1].IncomingHandle);
        Assert.Equal(new GeometryPoint(3, -.6m), contour.Nodes[1].OutgoingHandle);
        Assert.Equal(new GeometryPoint(-4.4m, -1.2m), contour.Nodes[2].IncomingHandle);
        Assert.Equal(new GeometryPoint(0, 0), contour.Nodes[2].OutgoingHandle);
        Assert.Equal(doc.Pieces[0].MaterialContours[0].Segments, contour.Segments);
        Assert.Equal(doc.Pieces[0].MaterialContours[0].Nodes, result.Geometry.Single(g => g.SizeId == "base").Contour.Nodes);
    }

    [Fact] public void EachIncidentHandleUsesItsOwnControlSpan()
    {
        var doc = SyntheticGeometry.Curved(); var c = doc.Pieces[0].MaterialContours[0];
        c = c with { Nodes = c.Nodes.SetItem(2, c.Nodes[2] with { OutgoingHandle = new(1, 3) })
                .SetItem(3, c.Nodes[3] with { IncomingHandle = new(1, -3) }),
            Segments = c.Segments.SetItem(2, c.Segments[2] with { Kind = MaterialSegmentKind.CubicBezier }),
            ControlSpans = c.ControlSpans.Add(new("second", "n2", "n3", ["e2"])) };
        doc = SyntheticGeometry.WithRule(SyntheticGeometry.WithContour(doc, c), "n2", "large", new(0, -4));
        var result = Calculate(doc); Assert.True(result.IsApprovable);
        var node = result.Geometry.Single(g => g.SizeId == "large").Contour.Nodes[2];
        Assert.Equal(new GeometryPoint(-4.4m, -1.2m), node.IncomingHandle);
        Assert.Equal(new GeometryPoint(1.4m, 4.2m), node.OutgoingHandle);
    }

    [Theory] [InlineData("continuous")] [InlineData("uncovered")] [InlineData("wrap")] [InlineData("noncontrol-rule")]
    public void UnevidencedCurvedVariantNeverFallsBackToRigidTranslation(string variant)
    {
        var doc = SyntheticGeometry.Curved(); var c = doc.Pieces[0].MaterialContours[0];
        doc = variant switch
        {
            "continuous" => doc with { CurvePolicy = "molde-continuous-unknown" },
            "uncovered" => SyntheticGeometry.WithContour(doc, c with { ControlSpans = [] }),
            "wrap" => SyntheticGeometry.WithContour(doc, c with { ControlSpans = [new("wrap", "n4", "n2", ["e4", "e0", "e1"])] }),
            _ => doc with { Rules = doc.Rules.Add(new("bad", "piece", "mid", "base", "large", default)) }
        };
        var result = Calculate(doc); Assert.False(result.IsApprovable); Assert.Empty(result.Geometry);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact] public void DegenerateDerivedControlSpanIsRejectedWithoutRepair()
    {
        var doc = SyntheticGeometry.WithRule(SyntheticGeometry.Curved(), "n2", "large", new(-20, 0));
        var result = Calculate(doc); Assert.False(result.IsApprovable);
        Assert.Contains(result.Diagnostics, d => d.Code == "grading_curve_span_degenerate" && d.EntityId == "span" && d.SizeId == "large");
    }

    [Fact] public void ChangingOneTransitionRecalculatesFartherSizesWithoutTouchingTheOtherSide()
    {
        var source = SyntheticGeometry.Curved(); var old = Calculate(source);
        var changed = Calculate(SyntheticGeometry.WithRule(source, "n2", "large", new(0, -4)));
        Assert.True(changed.IsApprovable);
        Assert.Equal(old.Geometry.Single(g => g.SizeId == "small").Contour.Nodes, changed.Geometry.Single(g => g.SizeId == "small").Contour.Nodes);
        Assert.Equal(changed.Geometry.Single(g => g.SizeId == "large").Contour.Nodes, changed.Geometry.Single(g => g.SizeId == "xl").Contour.Nodes);
        Assert.NotEqual(old.Geometry.Single(g => g.SizeId == "xl").Contour.Nodes, changed.Geometry.Single(g => g.SizeId == "xl").Contour.Nodes);
    }

    [Fact] public void ProofBudgetExhaustionIsExplicitAndNeverProducesAnApprovedSubset()
    {
        var result = new GradingEngine().Calculate(SyntheticGeometry.Curved(), new(1, 1));
        Assert.False(result.IsApprovable); Assert.Empty(result.Geometry);
        Assert.Contains(result.Diagnostics, d => d.Code == "grading_geometry_validity_unresolved" && d.EntityId is not null);
    }
}
