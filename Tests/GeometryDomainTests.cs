using System.Collections.Immutable;
using System.Text.Json;
using GestionProduccion.Grading.Core;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Tests;

public sealed class GeometryDomainTests
{
    private static GradingCalculation Calculate(InternalGeometryDocument doc) => new GradingEngine().Calculate(doc, new(24, 20000));

    [Fact] public void ExplicitZeroProducesAllAvailableSizesWithoutChangingSource()
    {
        var doc = SyntheticGeometry.Rectangle();
        var before = JsonSerializer.Serialize(doc);
        var result = Calculate(doc);
        Assert.True(result.IsApprovable);
        Assert.Equal(4, result.Geometry.Length);
        Assert.Equal(before, JsonSerializer.Serialize(doc));
        Assert.Equal(doc.Pieces[0].MaterialContours[0].Nodes, result.Geometry.Single(g => g.SizeId == "base").Contour.Nodes);
    }

    [Fact] public void ConsecutiveIncrementsAccumulateFromBaseIndependentlyOnBothSides()
    {
        var doc = SyntheticGeometry.WithRule(SyntheticGeometry.Rectangle(), "n2", "large", new(2, 3));
        doc = SyntheticGeometry.WithRule(doc, "n2", "xl", new(1, 4));
        doc = SyntheticGeometry.WithRule(doc, "n2", "small", new(-1, -2));
        var result = Calculate(doc);
        Assert.True(result.IsApprovable);
        Assert.Equal(new GeometryPoint(22, 13), Point(result, "large"));
        Assert.Equal(new GeometryPoint(23, 17), Point(result, "xl"));
        Assert.Equal(new GeometryPoint(19, 8), Point(result, "small"));
        Assert.Equal(new GeometryPoint(20, 10), Point(result, "base"));
    }

    [Fact] public void MissingRuleNeverMeansZeroAndReturnsPreciseDiagnostic()
    {
        var doc = SyntheticGeometry.Rectangle();
        doc = doc with { Rules = doc.Rules.Where(r => r.ControlNodeId != "n2" || r.ToSizeId != "large").ToImmutableArray() };
        var result = Calculate(doc);
        Assert.False(result.IsApprovable);
        Assert.Empty(result.Geometry);
        Assert.Contains(result.Diagnostics, d => d.Code == "grading_missing_rule" && d.PieceId == "piece" && d.SizeId == "large" && d.EntityId == "n2");
    }

    [Fact] public void UnavailableIntermediateSizeIsNotPublishedButItsTransitionIsRequired()
    {
        var doc = SyntheticGeometry.Rectangle();
        doc = doc with { Pieces = [doc.Pieces[0] with { AvailableSizeIds = ["xl"] }] };
        var result = Calculate(doc);
        Assert.True(result.IsApprovable);
        Assert.Equal("xl", Assert.Single(result.Geometry).SizeId);
        doc = doc with { Rules = doc.Rules.Where(r => r.ToSizeId != "large").ToImmutableArray() };
        Assert.False(Calculate(doc).IsApprovable);
    }

    [Theory] [InlineData("units")] [InlineData("axes")] [InlineData("version")]
    public void UnsupportedContractIsRejected(string field)
    {
        var doc = SyntheticGeometry.Rectangle();
        doc = field switch { "units" => doc with { Units = "cm" }, "axes" => doc with { Axes = "screen" }, _ => doc with { ContractVersion = 2 } };
        Assert.Contains(Calculate(doc).Diagnostics, d => d.Code == "grading_contract_unsupported");
    }

    [Fact] public void InternalDocumentRoundTripPreservesStableIdentitiesAndDecimalCoordinates()
    {
        var doc = SyntheticGeometry.Rectangle();
        var copy = JsonSerializer.Deserialize<InternalGeometryDocument>(JsonSerializer.Serialize(doc))!;
        Assert.True(Calculate(copy).IsApprovable);
        Assert.Equal(JsonSerializer.Serialize(doc), JsonSerializer.Serialize(copy));
    }

    [Theory] [InlineData("open")] [InlineData("multiple")] [InlineData("identity")] [InlineData("segments")]
    public void StructuralAmbiguityRejectsTheWholeDocument(string defect)
    {
        var doc = SyntheticGeometry.Rectangle(); var c = doc.Pieces[0].MaterialContours[0];
        doc = defect switch
        {
            "open" => SyntheticGeometry.WithContour(doc, c with { Closed = false }),
            "multiple" => doc with { Pieces = [doc.Pieces[0] with { MaterialContours = [c, c with { Id = "second" }] }] },
            "identity" => SyntheticGeometry.WithContour(doc, c with { Nodes = c.Nodes.SetItem(1, c.Nodes[1] with { Id = "n0" }) }),
            _ => SyntheticGeometry.WithContour(doc, c with { Segments = c.Segments.SetItem(1, c.Segments[1] with { EndNodeId = "n0" }) })
        };
        Assert.False(Calculate(doc).IsApprovable); Assert.Empty(Calculate(doc).Geometry);
    }

    [Fact] public void AdditionalContentRemainsPreservedAndNeverBecomesGradedMaterial()
    {
        var doc = SyntheticGeometry.Rectangle();
        doc = doc with { Pieces = [doc.Pieces[0] with { AdditionalContent = [new("trace-1", "unclassified", SyntheticGeometry.Provenance)] }] };
        Assert.True(Calculate(doc).IsApprovable);
        Assert.Equal("trace-1", doc.Pieces[0].AdditionalContent[0].Id);
        Assert.Equal(doc.Pieces[0].AdditionalContent[0], Assert.Single(Calculate(doc).UngradedSourceContent).Content);
        Assert.All(Calculate(doc).Geometry, g => Assert.Equal(doc.Pieces[0].Provenance, g.Provenance));
        Assert.All(Calculate(doc).Geometry, g => Assert.Equal(4, g.Contour.Segments.Length));
    }

    [Fact] public void OneInvalidRequestedSizePreventsPublicationOfEveryPieceSize()
    {
        var doc = SyntheticGeometry.WithRule(SyntheticGeometry.Rectangle(), "n2", "large", new(-30, -20));
        var result = Calculate(doc);
        Assert.False(result.IsApprovable); Assert.Empty(result.Geometry);
        Assert.Contains(result.Diagnostics, d => d.SizeId == "large" && d.ContourId == "outer");
    }

    private static GeometryPoint Point(GradingCalculation result, string size)
        => result.Geometry.Single(g => g.SizeId == size).Contour.Nodes[2].Position;
}
