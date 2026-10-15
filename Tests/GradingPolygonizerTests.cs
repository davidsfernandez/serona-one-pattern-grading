using System.Collections.Immutable;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Shared.ModelingGrading;
using GestionProduccion.Nesting.Core;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingPolygonizerTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public GradingPolygonizerTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;
    private static GradingPolygonizer Adapter(int depth = 24, int vertices = 200) => new(new(depth, vertices));
    private static MaterialContour Shape(decimal outgoing = 0, decimal incoming = 0) => new("outer",
        MaterialContourKind.OuterMaterial, true,
        [new("a", new(0, 0), default, new(3, outgoing), true),
         new("b", new(10, 0), new(-3, incoming), default, true),
         new("c", new(10, 10), default, default, true),
         new("d", new(0, 10), default, default, true)],
        [new("ab", "a", "b", outgoing == 0 && incoming == 0 ? MaterialSegmentKind.Line : MaterialSegmentKind.CubicBezier),
         new("bc", "b", "c", MaterialSegmentKind.Line), new("cd", "c", "d", MaterialSegmentKind.Line),
         new("da", "d", "a", MaterialSegmentKind.Line)], []);

    [Fact] public void IntegralLinesRemainSingleEdgesWithExactClosure()
    {
        var result = Adapter().Convert(Shape());
        Assert.Equal(new SourcePoint[] { new(0, 0), new(400, 0), new(400, 400), new(0, 400), new(0, 0) }, result.Ring.Points);
        Assert.Equal(4, result.Segments.Length);
        Assert.All(result.Segments, s => Assert.Equal(0m, s.MaximumDeviationMm));
    }

    [Theory] [InlineData(1, 1)] [InlineData(8, 8)] [InlineData(8, -8)] [InlineData(.1, .1)]
    public void CurvesRefineDeterministicallyAndPreserveSource(double outgoing, double incoming)
    {
        var source = Shape((decimal)outgoing, (decimal)incoming); var snapshot = source.Nodes;
        var first = Adapter().Convert(source); var second = Adapter().Convert(source);
        Assert.Equal(first.Ring.Points, second.Ring.Points);
        Assert.Equal(first.Segments, second.Segments);
        Assert.Equal(snapshot, source.Nodes);
        Assert.True(first.Segments.Length > 4);
        Assert.Equal(first.Ring.Points[0], first.Ring.Points[^1]);
        Assert.All(first.Segments, s => Assert.InRange(s.MaximumDeviationMm, 0m, .025m));
        Assert.Equal(first.Segments.Length + 1, first.Ring.Points.Count);
        var sampled = AssertSampledBidirectional(source, first);
        output.WriteLine($"ConservativeBoundMm={first.MaximumDeviationMm}; SecondarySampledMaximumMm={sampled:R}");
    }

    [Theory] [InlineData(0, 200)] [InlineData(24, 4)]
    public void ExhaustedBudgetsProduceNoPartialPolygon(int depth, int vertices)
        => Assert.Equal("grading_nesting_accuracy_unproved", Assert.Throws<ModelingPersistenceException>(() => Adapter(depth, vertices).Convert(Shape(8, 8))).Code);

    [Fact] public void MissingBudgetFailsClosed()
        => Assert.Equal("grading_workload_not_configured", Assert.Throws<ModelingPersistenceException>(() => new GradingPolygonizer(null).Convert(Shape())).Code);

    [Theory] [InlineData("open")] [InlineData("order")] [InlineData("kind")] [InlineData("duplicate")]
    public void MalformedContoursAreRejected(string variant)
    {
        var c = Shape(); c = variant switch
        {
            "open" => c with { Closed = false },
            "order" => c with { Segments = c.Segments.SetItem(0, c.Segments[0] with { EndNodeId = "c" }) },
            "kind" => c with { Segments = c.Segments.SetItem(0, c.Segments[0] with { Kind = (MaterialSegmentKind)99 }) },
            _ => c with { Nodes = c.Nodes.SetItem(1, c.Nodes[1] with { Id = "a" }) }
        };
        Assert.Throws<ModelingPersistenceException>(() => Adapter().Convert(c));
    }

    [Fact] public void QuantizationCollapseIsExplicitlyRejected()
    {
        var c = Shape(); c = c with { Nodes = c.Nodes.SetItem(1, c.Nodes[1] with { Position = new(.001m, 0) }) };
        Assert.Equal("grading_nesting_quantization_degenerate", Assert.Throws<ModelingPersistenceException>(() => Adapter().Convert(c)).Code);
    }

    [Fact] public void OverflowIsExplicitlyRejected()
    {
        var c = Shape(); c = c with { Nodes = c.Nodes.SetItem(0, c.Nodes[0] with { Position = new(decimal.MaxValue, 0) }) };
        Assert.Equal("grading_nesting_coordinate_overflow", Assert.Throws<ModelingPersistenceException>(() => Adapter().Convert(c)).Code);
    }

    [Fact] public void CancellationDoesNotReturnPartialGeometry()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Adapter().Convert(Shape(8, 8), cancellation.Token));
    }

    [Fact] public void FinalGridUsesCanonicalMillimetresAndEvenMidpointRounding()
    {
        var c = Shape(); c = c with { Nodes = c.Nodes.Select(n => n with
            { Position = new(n.Position.X + .0125m, n.Position.Y + .0125m) }).ToImmutableArray() };
        var result = Adapter().Convert(c);
        Assert.Equal(new SourcePoint(0, 0), result.Ring.Points[0]);
        Assert.All(result.Segments, s => Assert.InRange(s.MaximumDeviationMm, .017677669529m, .017677669530m));
    }

    [Fact] public void MultipleCurvesRetainSegmentOrderAndIndependentPaths()
    {
        var c = Shape(3, 3); c = c with
        {
            Nodes = c.Nodes.SetItem(1, c.Nodes[1] with { OutgoingHandle = new(2, 3) })
                .SetItem(2, c.Nodes[2] with { IncomingHandle = new(2, -3) }),
            Segments = c.Segments.SetItem(1, c.Segments[1] with { Kind = MaterialSegmentKind.CubicBezier })
        };
        var result = Adapter().Convert(c);
        Assert.Equal(new[] { "ab", "bc", "cd", "da" }, result.Segments.Select(s => s.SegmentId).Distinct());
        Assert.True(result.Segments.Count(s => s.SegmentId == "bc") > 1);
        Assert.All(result.Segments.Where(s => s.SegmentId is "ab" or "bc"), s => Assert.NotEmpty(s.SubdivisionPath));
    }

    [Fact] public void StraightSourceEdgesIgnoreUnusedHandles()
    {
        var c = Shape(); c = c with { Nodes = c.Nodes.SetItem(0, c.Nodes[0] with { OutgoingHandle = new(999, -999) }) };
        Assert.Equal(Adapter().Convert(Shape()).Ring.Points, Adapter().Convert(c).Ring.Points);
    }

    [Fact] public void EffectiveNestingVertexBudgetRemainsTwoHundred()
    {
        var c = Shape(1000, 1000);
        Assert.Equal("grading_nesting_accuracy_unproved", Assert.Throws<ModelingPersistenceException>(() => Adapter(24, 20000).Convert(c)).Code);
    }

    // Sampling is secondary evidence: production acceptance uses exact Bernstein control bounds.
    private static double AssertSampledBidirectional(MaterialContour source, GradingPolygonizedContour result)
    {
        var a = source.Nodes[0]; var b = source.Nodes[1];
        var samples = Enumerable.Range(0, 2001).Select(i => Evaluate(a, b, i / 2000d)).ToArray();
        var edges = result.Segments.Where(s => s.SegmentId == "ab").ToArray();
        double maximum = 0;
        foreach (var p in samples)
        {
            var distance = edges.Min(e => Distance(p, Mm(e.Start), Mm(e.End)));
            Assert.True(distance <= .025000001); maximum = Math.Max(maximum, distance);
        }
        foreach (var edge in edges)
            for (var i = 0; i <= 10; i++)
            {
                var aMm = Mm(edge.Start); var bMm = Mm(edge.End); var t = i / 10d;
                var p = (aMm.X + t * (bMm.X - aMm.X), aMm.Y + t * (bMm.Y - aMm.Y));
                var distance = samples.Zip(samples.Skip(1)).Min(s => Distance(p, s.First, s.Second));
                Assert.True(distance <= .02501); maximum = Math.Max(maximum, distance);
            }
        return maximum;
    }
    private static (double X, double Y) Mm(SourcePoint p) => (p.X / 40d, p.Y / 40d);
    private static (double X, double Y) Evaluate(MaterialNode a, MaterialNode b, double t)
    {
        var u = 1 - t; var p = a.Position; var q = p + a.OutgoingHandle; var r = b.Position + b.IncomingHandle; var s = b.Position;
        return (u*u*u*(double)p.X + 3*u*u*t*(double)q.X + 3*u*t*t*(double)r.X + t*t*t*(double)s.X,
            u*u*u*(double)p.Y + 3*u*u*t*(double)q.Y + 3*u*t*t*(double)r.Y + t*t*t*(double)s.Y);
    }
    private static double Distance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X-a.X; var dy = b.Y-a.Y; var t = Math.Clamp(((p.X-a.X)*dx+(p.Y-a.Y)*dy)/(dx*dx+dy*dy), 0, 1);
        return Math.Sqrt(Math.Pow(p.X-a.X-t*dx, 2)+Math.Pow(p.Y-a.Y-t*dy, 2));
    }
}
