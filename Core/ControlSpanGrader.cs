using System.Collections.Immutable;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Core;

internal static class ControlSpanGrader
{
    internal static MaterialContour? Grade(MaterialContour source, Dictionary<string, GeometryPoint> controls,
        string pieceId, string sizeId, ImmutableArray<GradingDiagnostic>.Builder errors)
    {
        var nodes = source.Nodes.Select(n => controls.TryGetValue(n.Id, out var position)
            ? n with { Position = position } : n).ToArray();
        foreach (var span in source.ControlSpans)
        {
            var start = source.Nodes.FindIndex(n => n.Id == span.StartControlId);
            var end = source.Nodes.FindIndex(n => n.Id == span.EndControlId);
            var oldStart = source.Nodes[start].Position; var oldVector = source.Nodes[end].Position - oldStart;
            var newStart = controls[span.StartControlId]; var newVector = controls[span.EndControlId] - newStart;
            if (newVector == default)
            { errors.Add(new("grading_curve_span_degenerate", pieceId, sizeId, source.Id, span.Id)); return null; }
            var (real, imaginary) = Similarity(oldVector, newVector);
            // This similarity maps BASE endpoints to graded controls; each incident handle belongs to its span.
            for (var i = start; i <= end; i++)
            {
                var original = source.Nodes[i]; var node = nodes[i];
                if (i > start && i < end) node = node with { Position = newStart + Transform(original.Position - oldStart, real, imaginary) };
                if (i > start) node = node with { IncomingHandle = Transform(original.IncomingHandle, real, imaginary) };
                if (i < end) node = node with { OutgoingHandle = Transform(original.OutgoingHandle, real, imaginary) };
                nodes[i] = node;
            }
        }
        return source with { Nodes = nodes.ToImmutableArray() };
    }

    private static GeometryPoint Transform(GeometryPoint point, decimal real, decimal imaginary)
    {
        var transformed = new GeometryPoint(real * point.X - imaginary * point.Y, imaginary * point.X + real * point.Y);
        if (point != default && transformed == default) throw new OverflowException("A nonzero vector exceeds decimal precision.");
        return transformed;
    }

    private static (decimal Real, decimal Imaginary) Similarity(GeometryPoint source, GeometryPoint target)
    {
        if (source == target) return (1, 0);
        // Normalization prevents a nonzero decimal span from becoming zero when its squared length underflows.
        var scale = Math.Max(Math.Abs(source.X), Math.Abs(source.Y));
        var x = source.X / scale; var y = source.Y / scale;
        var denominator = x * x + y * y;
        var real = (x * target.X + y * target.Y) / denominator / scale;
        var imaginary = (x * target.Y - y * target.X) / denominator / scale;
        if (real == 0 && imaginary == 0) throw new OverflowException("The similarity exceeds decimal precision.");
        return (real, imaginary);
    }
}
