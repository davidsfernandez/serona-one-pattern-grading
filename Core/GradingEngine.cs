using System.Collections.Immutable;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Core;

/// <summary>Caller-supplied proof budgets; production values are derived in the operational limits phase.</summary>
public sealed record GeometryProofBudget(int MaximumDepth, int MaximumWork);

public sealed class GradingEngine
{
    public GradingCalculation Calculate(InternalGeometryDocument document, GeometryProofBudget budget, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ct.ThrowIfCancellationRequested();
        if (budget.MaximumDepth <= 0 || budget.MaximumWork <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        var errors = GeometryContractValidator.Validate(document).ToBuilder();
        if (errors.Count > 0) return new([], errors.ToImmutable(), []);
        var geometry = ImmutableArray.CreateBuilder<DerivedPieceSize>();
        foreach (var piece in document.Pieces)
        {
            ct.ThrowIfCancellationRequested();
            Validate(piece.MaterialContours[0], piece.Id, document.SizeSet.BaseSizeId, budget, errors, ct);
            foreach (var size in document.SizeSet.Sizes.OrderBy(s => s.Order).Where(s => piece.AvailableSizeIds.Contains(s.Id)))
                CalculateSize(document, piece, size.Id, budget, geometry, errors, ct);
        }
        // Partial valid geometry is not published as an approvable subset of a failed version.
        var ungraded = document.Pieces.SelectMany(p => p.AdditionalContent.Select(c => new UngradedSourceContentReference(p.Id, c))).ToImmutableArray();
        return new(errors.Count == 0 ? geometry.ToImmutable() : [], errors.ToImmutable(), ungraded);
    }

    private static void CalculateSize(InternalGeometryDocument doc, GradingPiece piece, string size,
        GeometryProofBudget budget, ImmutableArray<DerivedPieceSize>.Builder geometry,
        ImmutableArray<GradingDiagnostic>.Builder errors, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var controls = ConsecutiveGradeAccumulator.Accumulate(doc, piece, size, errors);
            if (controls is null) return;
            var source = piece.MaterialContours[0];
            var contour = size == doc.SizeSet.BaseSizeId ? source : ControlSpanGrader.Grade(source, controls, piece.Id, size, errors);
            if (contour is not null && Validate(contour, piece.Id, size, budget, errors, ct))
            {
                if (MaterialTopologyValidator.SignedArea(source).Sign != MaterialTopologyValidator.SignedArea(contour).Sign)
                    errors.Add(new("grading_orientation_changed", piece.Id, size, contour.Id));
                else geometry.Add(new(piece.Id, size, contour, piece.Provenance));
            }
        }
        catch (OverflowException) { errors.Add(new("grading_numeric_capacity_exceeded", piece.Id, size, piece.MaterialContours[0].Id)); }
    }

    private static bool Validate(MaterialContour contour, string piece, string size, GeometryProofBudget budget,
        ImmutableArray<GradingDiagnostic>.Builder errors, CancellationToken ct)
    {
        var diagnostic = MaterialTopologyValidator.Validate(contour, budget, ct);
        if (diagnostic is null) return true;
        errors.Add(diagnostic with { PieceId = piece, SizeId = size, ContourId = contour.Id });
        return false;
    }
}
