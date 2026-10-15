using System.Collections.Immutable;
using System.Text.Json;
using GestionProduccion.Grading.Core;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Tests;

public sealed class ContractBoundaryTests
{
    [Theory] [InlineData("size-order")] [InlineData("size-id")] [InlineData("base")] [InlineData("availability")]
    public void InvalidSizeContractIsRejectedBeforeCalculation(string defect)
    {
        var doc = SyntheticGeometry.Rectangle(); var set = doc.SizeSet;
        doc = defect switch
        {
            "size-order" => doc with { SizeSet = set with { Sizes = set.Sizes.SetItem(0, set.Sizes[0] with { Order = 1 }) } },
            "size-id" => doc with { SizeSet = set with { Sizes = set.Sizes.SetItem(0, set.Sizes[0] with { Id = "base" }) } },
            "base" => doc with { SizeSet = set with { BaseSizeId = "missing" } },
            _ => doc with { Pieces = [doc.Pieces[0] with { AvailableSizeIds = ["not-declared"] }] }
        };
        Assert.False(Calculate(doc).IsApprovable);
    }

    [Theory] [InlineData("duplicate")] [InlineData("reverse")] [InlineData("unknown-node")]
    public void InvalidRuleCannotOverrideOrBypassExplicitCorrespondence(string defect)
    {
        var doc = SyntheticGeometry.Rectangle(); var rule = doc.Rules[0];
        doc = defect switch
        {
            "duplicate" => doc with { Rules = doc.Rules.Add(rule with { Id = "another" }) },
            "reverse" => doc with { Rules = doc.Rules.SetItem(0, rule with { FromSizeId = rule.ToSizeId, ToSizeId = rule.FromSizeId }) },
            _ => doc with { Rules = doc.Rules.SetItem(0, rule with { ControlNodeId = "missing" }) }
        };
        Assert.Contains(Calculate(doc).Diagnostics, d => d.Code == "grading_rule_invalid");
    }

    [Fact] public void BaseOnlyPieceDoesNotRequireDerivedRules()
    {
        var doc = SyntheticGeometry.Rectangle();
        doc = doc with { Pieces = [doc.Pieces[0] with { AvailableSizeIds = ["base"] }], Rules = [] };
        Assert.True(Calculate(doc).IsApprovable); Assert.Single(Calculate(doc).Geometry);
    }

    [Fact] public void UnsupportedPrimitiveIsRejectedRatherThanMadeIntoALine()
    {
        var doc = SyntheticGeometry.Rectangle(); var c = doc.Pieces[0].MaterialContours[0];
        doc = SyntheticGeometry.WithContour(doc, c with { Segments = c.Segments.SetItem(0, c.Segments[0] with { Kind = (MaterialSegmentKind)90 }) });
        Assert.Contains(Calculate(doc).Diagnostics, d => d.Code == "grading_geometry_unsupported");
    }

    [Fact] public void DefaultCollectionsAndNullEntitiesProduceDiagnosticsNotPartialGeometry()
    {
        var doc = SyntheticGeometry.Rectangle();
        Assert.False(Calculate(doc with { Pieces = default }).IsApprovable);
        Assert.False(Calculate(doc with { Pieces = [doc.Pieces[0] with { MaterialContours = [null!] }] }).IsApprovable);
        Assert.False(Calculate(doc with { Rules = [null!] }).IsApprovable);
    }

    [Fact] public void NonFiniteNumbersCannotEnterTheDecimalInterchangeContract()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GeometryPoint>("{\"X\":\"NaN\",\"Y\":0}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GeometryPoint>("{\"X\":1e999,\"Y\":0}"));
    }

    [Fact] public void ClockwiseBaseRetainsItsOrientationWithoutNormalization()
    {
        var doc = SyntheticGeometry.Rectangle(); var c = doc.Pieces[0].MaterialContours[0];
        c = c with { Nodes = c.Nodes.Select(n => n with { Position = new(n.Position.X, -n.Position.Y) }).ToImmutableArray() };
        doc = SyntheticGeometry.WithContour(doc, c);
        Assert.True(Calculate(doc).IsApprovable);
        Assert.Equal(c.Nodes, Calculate(doc).Geometry.Single(g => g.SizeId == "base").Contour.Nodes);
    }

    [Fact] public void MissingProvenanceAndUnknownMaterialClassificationAreExplicit()
    {
        var doc = SyntheticGeometry.Rectangle();
        Assert.Contains(Calculate(doc with { Provenance = null! }).Diagnostics, d => d.Code == "grading_provenance_invalid");
        var c = doc.Pieces[0].MaterialContours[0];
        Assert.False(Calculate(SyntheticGeometry.WithContour(doc, c with { Kind = MaterialContourKind.Unsupported })).IsApprovable);
    }

    [Fact] public void ExplicitOrderingAndStableSizeIdentityDoNotDependOnArrayOrderOrLabels()
    {
        var doc = SyntheticGeometry.Rectangle();
        doc = doc with { SizeSet = doc.SizeSet with { Sizes = doc.SizeSet.Sizes.Reverse().Select(s => s with { Label = "same label" }).ToImmutableArray() } };
        doc = SyntheticGeometry.WithRule(doc, "n2", "large", new(0, 2));
        var result = Calculate(doc); Assert.True(result.IsApprovable);
        Assert.Equal(new GeometryPoint(20, 12), result.Geometry.Single(g => g.SizeId == "large").Contour.Nodes[2].Position);
        Assert.Equal(new[] { "small", "base", "large", "xl" }, result.Geometry.Select(g => g.SizeId));
    }

    [Fact] public void UngradedReferencesAreRetainedEvenWhenTheCalculatedVersionIsInvalid()
    {
        var doc = SyntheticGeometry.Rectangle();
        doc = doc with { Pieces = [doc.Pieces[0] with { AdditionalContent = [new("private-trace", "unclassified", SyntheticGeometry.Provenance)] }] };
        doc = SyntheticGeometry.WithRule(doc, "n2", "large", new(-30, -20));
        var result = Calculate(doc); Assert.False(result.IsApprovable); Assert.Empty(result.Geometry);
        Assert.Equal("private-trace", Assert.Single(result.UngradedSourceContent).Content.Id);
    }

    private static GradingCalculation Calculate(InternalGeometryDocument doc) => new GradingEngine().Calculate(doc, new(24, 20000));
}
