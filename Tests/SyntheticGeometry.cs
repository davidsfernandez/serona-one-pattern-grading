using System.Collections.Immutable;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Tests;

internal static class SyntheticGeometry
{
    internal static readonly GeometryProvenance Provenance = new("synthetic", "synthetic-digest", "serona-test-v1");
    internal static InternalGeometryDocument Rectangle()
    {
        var nodes = new[] { new GeometryPoint(0, 0), new(20, 0), new(20, 10), new(0, 10) }
            .Select((p, i) => new MaterialNode($"n{i}", p, default, default, true)).ToImmutableArray();
        var segments = Enumerable.Range(0, 4).Select(i => new MaterialSegment($"e{i}", $"n{i}",
            $"n{(i + 1) % 4}", MaterialSegmentKind.Line)).ToImmutableArray();
        var contour = new MaterialContour("outer", MaterialContourKind.OuterMaterial, true, nodes, segments, []);
        var sizes = new OrderedSizeSet("base", [new("small", "S", 0), new("base", "BASE", 1), new("large", "L", 2), new("xl", "XL", 3)]);
        var rules = nodes.SelectMany(n => new[] { ("base", "small"), ("base", "large"), ("large", "xl") }
            .Select(t => new PointGradeIncrement($"{n.Id}-{t.Item2}", "piece", n.Id, t.Item1, t.Item2, default))).ToImmutableArray();
        return new(1, "mm", "x-right-y-up", InternalGeometryDocument.SupportedCurvePolicy, Provenance, sizes,
            [new("piece", Provenance, [contour], ["small", "base", "large", "xl"], [])], rules);
    }

    internal static InternalGeometryDocument WithContour(InternalGeometryDocument doc, MaterialContour contour)
        => doc with { Pieces = [doc.Pieces[0] with { MaterialContours = [contour] }] };
    internal static InternalGeometryDocument WithRule(InternalGeometryDocument doc, string node, string to, GeometryPoint delta)
        => doc with { Rules = doc.Rules.Select(r => r.ControlNodeId == node && r.ToSizeId == to
            ? r with { Displacement = delta } : r).ToImmutableArray() };

    internal static InternalGeometryDocument Curved()
    {
        MaterialNode[] nodes = [new("n0", new(0, 0), default, new(4, -2), true),
            new("mid", new(10, -3), new(-3, 0), new(3, 0), false),
            new("n2", new(20, 0), new(-4, -2), default, true),
            new("n3", new(20, 10), default, default, true), new("n4", new(0, 10), default, default, true)];
        var segments = Enumerable.Range(0, nodes.Length).Select(i => new MaterialSegment($"e{i}", nodes[i].Id,
            nodes[(i + 1) % nodes.Length].Id, i < 2 ? MaterialSegmentKind.CubicBezier : MaterialSegmentKind.Line)).ToImmutableArray();
        var contour = new MaterialContour("outer", MaterialContourKind.OuterMaterial, true, nodes.ToImmutableArray(), segments,
            [new("span", "n0", "n2", ["e0", "e1"])]);
        var doc = WithContour(Rectangle(), contour);
        var rules = nodes.Where(n => n.IsGradingControl).SelectMany(n => new[] { ("base", "small"), ("base", "large"), ("large", "xl") }
            .Select(t => new PointGradeIncrement($"{n.Id}-{t.Item2}", "piece", n.Id, t.Item1, t.Item2, default))).ToImmutableArray();
        return doc with { Rules = rules };
    }
}
