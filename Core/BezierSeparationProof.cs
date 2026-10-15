namespace GestionProduccion.Grading.Core;

internal static class BezierSeparationProof
{
    internal static bool Separated(ExactBezierSegment a, ExactBezierSegment b, ExactGeometryPoint? shared)
    {
        var points = a.Controls.Concat(b.Controls).ToArray();
        var directions = new List<ExactGeometryPoint>
        { new(new(1), new(0)), new(new(0), new(1)), a.Controls[3] - a.Controls[0], b.Controls[3] - b.Controls[0] };
        for (var i = 0; i < points.Length; i++)
            for (var j = i + 1; j < points.Length; j++)
            { var delta = points[j] - points[i]; directions.Add(delta); directions.Add(delta.Normal); }
        foreach (var direction in directions.Where(d => !d.IsZero))
        {
            var left = a.Controls.Select(p => ExactGeometryPoint.Dot(direction, p)).ToArray();
            var right = b.Controls.Select(p => ExactGeometryPoint.Dot(direction, p)).ToArray();
            if (left.Max().CompareTo(right.Min()) < 0 || right.Max().CompareTo(left.Min()) < 0) return true;
            if (shared is { } endpoint && SharedEndpointOnly(a, b, endpoint, direction)) return true;
        }
        return false;
    }

    private static bool SharedEndpointOnly(ExactBezierSegment a, ExactBezierSegment b,
        ExactGeometryPoint endpoint, ExactGeometryPoint direction)
    {
        var left = a.Controls.Select(p => ExactGeometryPoint.Dot(direction, p - endpoint).Sign).ToArray();
        var right = b.Controls.Select(p => ExactGeometryPoint.Dot(direction, p - endpoint).Sign).ToArray();
        // All interior Bernstein weights are positive: strict opposite signs exclude every contact except the declared endpoint.
        return (left.All(s => s <= 0) && left.Any(s => s < 0) && right.All(s => s >= 0) && right.Any(s => s > 0)) ||
               (right.All(s => s <= 0) && right.Any(s => s < 0) && left.All(s => s >= 0) && left.Any(s => s > 0));
    }
}
