using System.Collections.Immutable;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Tests;

internal static class GradingOperationalFixtures
{
    internal static InternalGeometryDocument Create(string shape, int pieceCount, int sizeCount)
    {
        var source = shape == "rectangle" ? SyntheticGeometry.Rectangle() : SyntheticGeometry.Curved();
        var contour = source.Pieces[0].MaterialContours[0];
        if (shape is "strong-curve" or "near-linear")
        {
            var factor = shape == "strong-curve" ? 3m : .01m;
            contour = contour with { Nodes = contour.Nodes.Select(n => n with
            {
                Position = n.Position.Y < 0 ? new(n.Position.X, n.Position.Y * factor) : n.Position,
                IncomingHandle = new(n.IncomingHandle.X, n.IncomingHandle.Y * factor),
                OutgoingHandle = new(n.OutgoingHandle.X, n.OutgoingHandle.Y * factor)
            }).ToImmutableArray() };
        }
        var sizes = Enumerable.Range(0, sizeCount).Select(i => new GradingSize($"size-{i}", $"SIZE {i}", i)).ToImmutableArray();
        var pieces = Enumerable.Range(0, pieceCount).Select(i => source.Pieces[0] with
        {
            Id = $"piece-{i}", MaterialContours = [contour], AvailableSizeIds = sizes.Select(s => s.Id).ToImmutableArray()
        }).ToImmutableArray();
        // Explicit zero increments exercise the supported span policy without claiming new grading variants.
        var rules = pieces.SelectMany(p => contour.Nodes.Where(n => n.IsGradingControl).SelectMany(n =>
            Enumerable.Range(1, sizeCount - 1).Select(i => new PointGradeIncrement(
                $"{p.Id}-{n.Id}-{i}", p.Id, n.Id, sizes[i - 1].Id, sizes[i].Id, default)))).ToImmutableArray();
        return source with { SizeSet = new(sizes[0].Id, sizes), Pieces = pieces, Rules = rules };
    }
}
