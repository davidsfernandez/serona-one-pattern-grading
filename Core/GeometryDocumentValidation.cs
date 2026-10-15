using System.Collections.Immutable;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Core;

/// <summary>Validates import/draft prerequisites without calculating derived grading geometry in Web/API.</summary>
public static class GeometryDocumentValidation
{
    public static ImmutableArray<GradingDiagnostic> Structure(InternalGeometryDocument document) => GeometryContractValidator.Validate(document);
    public static ImmutableArray<GradingDiagnostic> Import(InternalGeometryDocument document, GeometryProofBudget budget)
    {
        var errors = Structure(document).ToBuilder();
        if (errors.Count > 0) return errors.ToImmutable();
        if (budget.MaximumDepth <= 0 || budget.MaximumWork <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        foreach (var piece in document.Pieces)
        {
            var c = piece.MaterialContours[0]; var diagnostic = MaterialTopologyValidator.Validate(c, budget);
            if (diagnostic is not null) errors.Add(diagnostic with { PieceId = piece.Id, SizeId = document.SizeSet.BaseSizeId, ContourId = c.Id });
        }
        return errors.ToImmutable();
    }
    public static ImmutableArray<GradingDiagnostic> CalculationRequest(InternalGeometryDocument document)
    {
        var errors = Structure(document).ToBuilder();
        if (errors.Count > 0) return errors.ToImmutable();
        foreach (var p in document.Pieces)
            foreach (var size in p.AvailableSizeIds)
                ConsecutiveGradeAccumulator.Accumulate(document, p, size, errors);
        return errors.ToImmutable();
    }
}
