using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Core;

internal sealed record ExactBezierSegment(ExactGeometryPoint[] Controls)
{
    internal static ExactBezierSegment From(MaterialContour contour, int index)
    {
        var a = contour.Nodes[index]; var b = contour.Nodes[(index + 1) % contour.Nodes.Length];
        var start = ExactGeometryPoint.From(a.Position); var end = ExactGeometryPoint.From(b.Position);
        return contour.Segments[index].Kind == MaterialSegmentKind.Line
            ? new([start, start, end, end])
            : new([start, start + ExactGeometryPoint.From(a.OutgoingHandle), end + ExactGeometryPoint.From(b.IncomingHandle), end]);
    }

    internal (ExactBezierSegment Left, ExactBezierSegment Right) Split()
    {
        var p = Controls;
        var a = (p[0] + p[1]) / 2; var b = (p[1] + p[2]) / 2; var c = (p[2] + p[3]) / 2;
        var d = (a + b) / 2; var e = (b + c) / 2; var middle = (d + e) / 2;
        return (new([p[0], a, d, middle]), new([middle, e, c, p[3]]));
    }

    internal bool IsInjective()
    {
        var direction = Controls[3] - Controls[0];
        if (direction.IsZero) return false;
        // Nonnegative Bernstein derivative coefficients and one positive coefficient imply strict interior monotonicity.
        var differences = Enumerable.Range(0, 3).Select(i => ExactGeometryPoint.Dot(direction, Controls[i + 1] - Controls[i]).Sign).ToArray();
        return differences.All(s => s >= 0) && differences.Any(s => s > 0);
    }

    internal ExactGeometryNumber TwiceSignedArea()
    {
        var p = Controls;
        var coefficients = new[] { p[0], (p[1] - p[0]) * 3, (p[2] - p[1] * 2 + p[0]) * 3,
            p[3] - p[2] * 3 + p[1] * 3 - p[0] };
        var area = ExactGeometryNumber.Zero;
        for (var i = 0; i <= 3; i++)
            for (var j = 1; j <= 3; j++)
                area += ExactGeometryPoint.Cross(coefficients[i], coefficients[j]) * new ExactGeometryNumber(j) / (i + j);
        return area;
    }
}
