using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Nesting.Core;

namespace GestionProduccion.Grading.Tests;

public sealed class ReviewedImportBoundaryTests
{
    internal static ImportReviewResult Review()
    {
        SourcePoint[] points = [new(0, 0), new(800, 0), new(800, 400), new(0, 400), new(0, 0)];
        var vertices = points.Select((p, i) => new ImportedSourceVertex(p, i, i + 1, i * 4)).ToArray();
        var range = new SourceStrokeRange(0, 0, 4);
        var candidate = new ImportedMaterialCandidate("original-piece", 1, range, default, new(points), vertices, MaterialCandidateState.Valid, "valid");
        var source = new ImportedGeometrySnapshot(Guid.NewGuid(), 1, new(1, 40), new string('a', 64),
            [new(0, vertices)], [candidate]);
        return new(source, [new("original-piece", 1, "Synthetic", 1, default, new(points), vertices)],
            [new(range, ImportTraceRole.Material, "original-piece"), new(new(1, 0, 2), ImportTraceRole.SourceOnly, Reason: "unclassified")], []);
    }
    [Fact] public void ReviewedMaterialMapsExactlyAndRetainsUnassociatedContentWithoutSemantics()
    {
        var source = Review(); var imported = ReviewedImportModelingBoundary.Convert(source);
        var piece = Assert.Single(imported.Configuration.Pieces);
        Assert.Equal(20m, piece.MaterialContours[0].Nodes[1].Position.X);
        Assert.Equal(10m, piece.MaterialContours[0].Nodes[2].Position.Y);
        Assert.Single(piece.AvailableSizeIds); Assert.Empty(imported.Configuration.Rules);
        Assert.Empty(piece.AdditionalContent); Assert.Single(imported.UnassociatedSourceContent);
        Assert.Contains(source.Source.ArtifactId.ToString("N"), piece.Provenance.SourceIdentity);
    }
    [Fact] public void BoundaryRejectsUnreviewedGeometryAndUnsupportedScale()
    {
        var review = Review();
        Assert.Throws<ModelingPersistenceException>(() => ReviewedImportModelingBoundary.Convert(review with { UnassignedRanges = [new(0, 0, 1)] }));
        Assert.Throws<ModelingPersistenceException>(() => ReviewedImportModelingBoundary.Convert(review with { Source = review.Source with { UnitScale = new(1, 1) } }));
        var piece = review.Pieces[0];
        Assert.Throws<ModelingPersistenceException>(() => ReviewedImportModelingBoundary.Convert(review with {
            Pieces = [piece with { LocalContour = new(piece.LocalContour.Points.Take(4).ToArray()) }] }));
    }
}
