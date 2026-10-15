using System.Numerics;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Core;

/// <summary>Exact decimal-domain arithmetic for proof predicates, not a manufacturing tolerance.</summary>
internal readonly struct ExactGeometryNumber : IComparable<ExactGeometryNumber>
{
    private readonly BigInteger numerator;
    private readonly BigInteger denominator;
    internal int Sign => numerator.Sign;
    internal static ExactGeometryNumber Zero => new(0);
    internal ExactGeometryNumber(long value) { numerator = value; denominator = BigInteger.One; }
    private ExactGeometryNumber(BigInteger n, BigInteger d)
    {
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(n), BigInteger.Abs(d));
        numerator = d.Sign < 0 ? -n / divisor : n / divisor;
        denominator = BigInteger.Abs(d) / divisor;
    }
    internal static ExactGeometryNumber FromDecimal(decimal value)
    {
        var bits = decimal.GetBits(value);
        var integer = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        if ((bits[3] & int.MinValue) != 0) integer = -integer;
        return new(integer, BigInteger.Pow(10, (bits[3] >> 16) & 255));
    }
    public int CompareTo(ExactGeometryNumber other) => (numerator * other.denominator).CompareTo(other.numerator * denominator);
    public static ExactGeometryNumber operator +(ExactGeometryNumber a, ExactGeometryNumber b)
        => new(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator);
    public static ExactGeometryNumber operator -(ExactGeometryNumber a, ExactGeometryNumber b)
        => new(a.numerator * b.denominator - b.numerator * a.denominator, a.denominator * b.denominator);
    public static ExactGeometryNumber operator *(ExactGeometryNumber a, ExactGeometryNumber b)
        => new(a.numerator * b.numerator, a.denominator * b.denominator);
    public static ExactGeometryNumber operator /(ExactGeometryNumber a, int b) => new(a.numerator, a.denominator * b);
    internal long RoundScaled(long scale)
    {
        var scaled = numerator * scale;
        var whole = BigInteger.DivRem(scaled, denominator, out var remainder);
        var comparison = (BigInteger.Abs(remainder) * 2).CompareTo(denominator);
        if (comparison > 0 || comparison == 0 && !whole.IsEven) whole += scaled.Sign;
        return checked((long)whole);
    }
    internal decimal CeilingSquareRoot()
    {
        if (Sign < 0) throw new InvalidOperationException("A nonnegative squared bound is required.");
        const long scale = 1_000_000_000_000;
        var target = numerator * scale * scale;
        BigInteger low = 0, high = scale;
        while (high * high * denominator < target) high *= 2;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (middle * middle * denominator < target) low = middle; else high = middle;
        }
        return target == 0 ? 0m : checked((decimal)high) / scale;
    }
}

internal readonly record struct ExactGeometryPoint(ExactGeometryNumber X, ExactGeometryNumber Y)
{
    internal static ExactGeometryPoint From(GeometryPoint p) => new(ExactGeometryNumber.FromDecimal(p.X), ExactGeometryNumber.FromDecimal(p.Y));
    internal static ExactGeometryPoint Zero => new(ExactGeometryNumber.Zero, ExactGeometryNumber.Zero);
    internal bool IsZero => X.Sign == 0 && Y.Sign == 0;
    internal static bool Same(ExactGeometryPoint a, ExactGeometryPoint b) => (a - b).IsZero;
    internal static ExactGeometryNumber Dot(ExactGeometryPoint a, ExactGeometryPoint b) => a.X * b.X + a.Y * b.Y;
    internal static ExactGeometryNumber Cross(ExactGeometryPoint a, ExactGeometryPoint b) => a.X * b.Y - a.Y * b.X;
    internal ExactGeometryPoint Normal => new(ExactGeometryNumber.Zero - Y, X);
    public static ExactGeometryPoint operator +(ExactGeometryPoint a, ExactGeometryPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static ExactGeometryPoint operator -(ExactGeometryPoint a, ExactGeometryPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static ExactGeometryPoint operator *(ExactGeometryPoint a, int b) => new(a.X * new ExactGeometryNumber(b), a.Y * new ExactGeometryNumber(b));
    public static ExactGeometryPoint operator /(ExactGeometryPoint a, int b) => new(a.X / b, a.Y / b);
}
