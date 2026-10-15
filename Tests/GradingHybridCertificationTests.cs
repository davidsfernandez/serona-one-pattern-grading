using System.Collections.Immutable;
using System.Numerics;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Nesting.Core;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingHybridCertificationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(100000000000)]
    [InlineData(-100000000000)]
    public void LargeAnchorsWithTinyHandlesCannotHideDecimalAdditionLoss(long anchor)
    {
        var source = Translate(Contour(), anchor, anchor);
        source = source with { Nodes = source.Nodes
            .SetItem(0, source.Nodes[0] with { OutgoingHandle = new(4.000000000000000000000000001m, -2.000000000000000000000000001m) })
            .SetItem(1, source.Nodes[1] with { IncomingHandle = new(-3.000000000000000000000000001m, .000000000000000000000000001m) }) };
        var snapshot = source;
        GradingPolygonizedContour? result = null;
        var error = Record.Exception(() => result = new GradingPolygonizer(new(24, 200)).Convert(source));
        Assert.Equal(snapshot, source);
        if (error is null)
        {
            Assert.NotNull(result);
            AssertIndependentCertificate(source, result);
        }
        else
        {
            var rejection = Assert.IsType<ModelingPersistenceException>(error);
            Assert.Contains(rejection.Code, new[] { "grading_nesting_accuracy_unproved", "grading_nesting_numeric_capacity",
                "grading_nesting_quantization_degenerate", "grading_nesting_coordinate_overflow" });
            Assert.Null(result);
        }
    }

    [Theory]
    [InlineData(8, -8)]
    [InlineData(-8, 8)]
    [InlineData(12, 12)]
    public void InflectionsAndStrongCurvatureHaveExactBoundsAgainstFinalGrid(int outgoing, int incoming)
    {
        var source = Contour();
        source = source with { Nodes = source.Nodes
            .SetItem(0, source.Nodes[0] with { OutgoingHandle = new(4, outgoing) })
            .SetItem(1, source.Nodes[1] with { IncomingHandle = new(-3, incoming) }) };
        AssertIndependentCertificate(source, new GradingPolygonizer(new(24, 200)).Convert(source));
    }

    [Theory]
    [InlineData("0.0124999999999999999999999999")]
    [InlineData("0.0125")]
    [InlineData("0.0125000000000000000000000001")]
    [InlineData("-0.0124999999999999999999999999")]
    [InlineData("-0.0125")]
    [InlineData("-0.0125000000000000000000000001")]
    public void DecimalHalfTickCasesCertifyTheActualEmittedEdges(string text)
    {
        var shift = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var source = Translate(Contour(), shift, -shift);
        AssertIndependentCertificate(source, new GradingPolygonizer(new(24, 200)).Convert(source));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000000)]
    [InlineData(-100000000)]
    public void MixedMultiSpanContoursAreDeterministicAndPreserveAllSourceData(long translation)
    {
        var source = Translate(Contour(), translation + .0125m, translation - .0125m);
        var snapshot = source with { Nodes = source.Nodes.ToImmutableArray(), Segments = source.Segments.ToImmutableArray(),
            ControlSpans = source.ControlSpans.ToImmutableArray() };
        var adapter = new GradingPolygonizer(new(24, 200));
        var first = adapter.Convert(source);
        var second = adapter.Convert(source);
        Assert.Equal(first.Ring.Points, second.Ring.Points);
        Assert.Equal(first.Segments, second.Segments);
        Assert.Equal(snapshot, source);
        AssertIndependentCertificate(source, first);
        Assert.Equal(source.Segments.Select(s => s.Id), first.Segments.Select(s => s.SegmentId).Distinct());
        Assert.True(first.Segments.Count(s => s.SegmentId == "e0") > 1);
        Assert.True(first.Segments.Count(s => s.SegmentId == "e1") > 1);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(24, 4)]
    public void ExhaustionRejectsTheWholeConversionWithoutChangingSource(int depth, int vertices)
    {
        var source = Contour();
        var nodes = source.Nodes;
        var spans = source.ControlSpans;
        var failure = Assert.Throws<ModelingPersistenceException>(() =>
            new GradingPolygonizer(new(depth, vertices)).Convert(source));
        Assert.Equal("grading_nesting_accuracy_unproved", failure.Code);
        Assert.Equal(nodes, source.Nodes);
        Assert.Equal(spans, source.ControlSpans);
        AssertIndependentCertificate(source, new GradingPolygonizer(new(24, 200)).Convert(source));
    }

    private static MaterialContour Contour() => SyntheticGeometry.Curved().Pieces[0].MaterialContours[0];

    private static MaterialContour Translate(MaterialContour source, decimal x, decimal y) => source with
    {
        Nodes = source.Nodes.Select(n => n with { Position = new(n.Position.X + x, n.Position.Y + y) }).ToImmutableArray()
    };

    private void AssertIndependentCertificate(MaterialContour source, GradingPolygonizedContour result)
    {
        output.WriteLine($"ConservativeBoundMm={result.MaximumDeviationMm.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        Assert.Equal(result.Segments.Length + 1, result.Ring.Points.Count);
        Assert.Equal(result.Ring.Points[0], result.Ring.Points[^1]);
        Assert.Equal(result.Segments.Max(s => s.MaximumDeviationMm), result.MaximumDeviationMm);
        for (var i = 0; i < result.Segments.Length; i++)
        {
            var edge = result.Segments[i];
            Assert.Equal(result.Ring.Points[i], edge.Start);
            Assert.Equal(result.Ring.Points[i + 1], edge.End);
            Assert.NotEqual(edge.Start, edge.End);
            Assert.InRange(edge.MaximumDeviationMm, 0m, .025m);
            var segment = Assert.Single(source.Segments.Where(s => s.Id == edge.SegmentId));
            var a = source.Nodes.Single(n => n.Id == segment.StartNodeId);
            var b = source.Nodes.Single(n => n.Id == segment.EndNodeId);
            var boundSquared = Fraction.From(edge.MaximumDeviationMm).Square();
            var q0 = Point.From(edge.Start); var q3 = Point.From(edge.End);
            if (segment.Kind == MaterialSegmentKind.Line)
            {
                Assert.Equal("", edge.SubdivisionPath);
                Assert.True((Point.From(a.Position) - q0).SquaredNorm() <= boundSquared);
                Assert.True((Point.From(b.Position) - q3).SquaredNorm() <= boundSquared);
                continue;
            }
            var (start, end) = Interval(edge.SubdivisionPath);
            Point[] controls = [Point.From(a.Position), Point.From(a.Position) + Point.From(a.OutgoingHandle),
                Point.From(b.Position) + Point.From(b.IncomingHandle), Point.From(b.Position)];
            // Blossom evaluation derives restricted controls directly from the source cubic,
            // independently of library splitting and the production certifier's arithmetic.
            Point[] restricted = [Blossom(controls, start, start, start), Blossom(controls, start, start, end),
                Blossom(controls, start, end, end), Blossom(controls, end, end, end)];
            Point[] chord = [q0, (q0 * 2 + q3) / 3, (q0 + q3 * 2) / 3, q3];
            for (var c = 0; c < 4; c++)
                Assert.True((restricted[c] - chord[c]).SquaredNorm() <= boundSquared,
                    $"Certificate underestimates emitted edge {edge.SegmentId}/{edge.SubdivisionPath}, control {c}.");
        }
        foreach (var segment in source.Segments.Where(s => s.Kind == MaterialSegmentKind.CubicBezier))
        {
            var intervals = result.Segments.Where(e => e.SegmentId == segment.Id)
                .Select(e => Interval(e.SubdivisionPath)).ToArray();
            Assert.Equal(Fraction.Zero, intervals[0].Start);
            Assert.Equal(Fraction.One, intervals[^1].End);
            for (var i = 1; i < intervals.Length; i++) Assert.Equal(intervals[i - 1].End, intervals[i].Start);
        }
    }

    private static (Fraction Start, Fraction End) Interval(string path)
    {
        BigInteger numerator = 0;
        foreach (var bit in path)
        {
            Assert.True(bit is '0' or '1');
            numerator = numerator * 2 + (bit - '0');
        }
        var denominator = BigInteger.One << path.Length;
        return (new(numerator, denominator), new(numerator + 1, denominator));
    }

    private static Point Blossom(Point[] p, Fraction a, Fraction b, Fraction c)
    {
        static Point Lerp(Point x, Point y, Fraction t) => x * (Fraction.One - t) + y * t;
        var x = Lerp(p[0], p[1], a); var y = Lerp(p[1], p[2], a); var z = Lerp(p[2], p[3], a);
        return Lerp(Lerp(x, y, b), Lerp(y, z, b), c);
    }

    private readonly record struct Point(Fraction X, Fraction Y)
    {
        public static Point From(GeometryPoint p) => new(Fraction.From(p.X), Fraction.From(p.Y));
        public static Point From(SourcePoint p) => new(new(p.X, 40), new(p.Y, 40));
        public Fraction SquaredNorm() => X.Square() + Y.Square();
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static Point operator *(Point a, int b) => a * new Fraction(b, 1);
        public static Point operator *(Point a, Fraction b) => new(a.X * b, a.Y * b);
        public static Point operator /(Point a, int b) => a * new Fraction(1, b);
    }

    private readonly record struct Fraction
    {
        private readonly BigInteger numerator;
        private readonly BigInteger denominator;
        public static Fraction Zero => new(0, 1);
        public static Fraction One => new(1, 1);
        public Fraction(BigInteger n, BigInteger d)
        {
            var gcd = BigInteger.GreatestCommonDivisor(n, d);
            numerator = n / gcd; denominator = d / gcd;
        }
        public static Fraction From(decimal value)
        {
            var bits = decimal.GetBits(value);
            var n = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
            if (bits[3] < 0) n = -n;
            return new(n, BigInteger.Pow(10, (bits[3] >> 16) & 255));
        }
        public Fraction Square() => this * this;
        public static Fraction operator +(Fraction a, Fraction b) => new(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator);
        public static Fraction operator -(Fraction a, Fraction b) => new(a.numerator * b.denominator - b.numerator * a.denominator, a.denominator * b.denominator);
        public static Fraction operator *(Fraction a, Fraction b) => new(a.numerator * b.numerator, a.denominator * b.denominator);
        public static bool operator <=(Fraction a, Fraction b) => a.numerator * b.denominator <= b.numerator * a.denominator;
        public static bool operator >=(Fraction a, Fraction b) => b <= a;
    }
}
