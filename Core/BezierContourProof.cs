using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Core;

internal sealed class BezierContourProof
{
    private readonly GeometryProofBudget budget;
    private int remainingWork;
    private readonly CancellationToken cancellation;
    internal string? FailedSegmentId { get; private set; }
    internal string? RelatedSegmentId { get; private set; }
    internal BezierContourProof(GeometryProofBudget budget, CancellationToken cancellation = default)
    { this.budget = budget; remainingWork = budget.MaximumWork; this.cancellation = cancellation; }

    internal bool Prove(MaterialContour contour)
    {
        var leaves = new List<ProofLeaf>();
        for (var i = 0; i < contour.Segments.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var curve = ExactBezierSegment.From(contour, i);
            var proved = contour.Segments.Length == 2
                ? InitialClosedPairLeaves(curve, leaves, contour.Segments[i].Id)
                : InjectiveLeaves(curve, 0, leaves, contour.Segments[i].Id);
            if (!proved) { FailedSegmentId = contour.Segments[i].Id; return false; }
        }
        // Every pair is checked, including leaves of the same original cubic and the closing adjacency.
        for (var i = 0; i < leaves.Count; i++)
            for (var j = i + 1; j < leaves.Count; j++)
            {
                cancellation.ThrowIfCancellationRequested();
                ExactGeometryPoint? shared = j == i + 1 ? leaves[i].Curve.Controls[3] :
                    i == 0 && j == leaves.Count - 1 ? leaves[0].Curve.Controls[0] : null;
                if (!Pair(leaves[i].Curve, leaves[j].Curve, shared, 0))
                { FailedSegmentId = leaves[i].SegmentId; RelatedSegmentId = leaves[j].SegmentId; return false; }
            }
        return true;
    }

    private bool InjectiveLeaves(ExactBezierSegment curve, int depth, List<ProofLeaf> leaves, string segmentId)
    {
        cancellation.ThrowIfCancellationRequested();
        if (--remainingWork < 0) return false;
        if (curve.IsInjective()) { leaves.Add(new(curve, segmentId)); return true; }
        if (depth >= budget.MaximumDepth) return false;
        var (left, right) = curve.Split();
        return InjectiveLeaves(left, depth + 1, leaves, segmentId) && InjectiveLeaves(right, depth + 1, leaves, segmentId);
    }

    private bool InitialClosedPairLeaves(ExactBezierSegment curve, List<ProofLeaf> leaves, string segmentId)
    {
        // A two-segment ring has two declared shared endpoints. Partition first so each leaf pair owns at most one.
        var (left, right) = curve.Split();
        return InjectiveLeaves(left, 1, leaves, segmentId) && InjectiveLeaves(right, 1, leaves, segmentId);
    }

    private bool Pair(ExactBezierSegment a, ExactBezierSegment b, ExactGeometryPoint? shared, int depth)
    {
        cancellation.ThrowIfCancellationRequested();
        if (--remainingWork < 0) return false;
        if (BezierSeparationProof.Separated(a, b, shared)) return true;
        if (depth >= budget.MaximumDepth) return false;
        // Subdivision retains the exact curve; it is a proof partition, never a polygonal conversion.
        var (a0, a1) = a.Split(); var (b0, b1) = b.Split();
        return Pair(a0, b0, Shared(a0, b0, shared), depth + 1) &&
               Pair(a0, b1, Shared(a0, b1, shared), depth + 1) &&
               Pair(a1, b0, Shared(a1, b0, shared), depth + 1) &&
               Pair(a1, b1, Shared(a1, b1, shared), depth + 1);
    }

    private static ExactGeometryPoint? Shared(ExactBezierSegment a, ExactBezierSegment b, ExactGeometryPoint? shared)
        => shared is { } e && (ExactGeometryPoint.Same(a.Controls[0], e) || ExactGeometryPoint.Same(a.Controls[3], e)) &&
            (ExactGeometryPoint.Same(b.Controls[0], e) || ExactGeometryPoint.Same(b.Controls[3], e)) ? e : null;

    private sealed record ProofLeaf(ExactBezierSegment Curve, string SegmentId);
}
