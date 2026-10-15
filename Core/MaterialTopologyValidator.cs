using GestionProduccion.Shared.ModelingGrading;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;

namespace GestionProduccion.Grading.Core;

internal static class MaterialTopologyValidator
{
    internal static GradingDiagnostic? Validate(MaterialContour contour, GeometryProofBudget budget, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (SignedArea(contour).Sign == 0) return new("grading_zero_area");
        if (contour.Segments.All(s => s.Kind == MaterialSegmentKind.Line))
        {
            var polygonError = ValidatePolygon(contour);
            if (polygonError is not null) return polygonError;
            // Decimal source precision may exceed NTS doubles; exact segment separation guards that boundary.
        }
        ct.ThrowIfCancellationRequested();
        var proof = new BezierContourProof(budget, ct);
        return proof.Prove(contour) ? null : new("grading_geometry_validity_unresolved", EntityId: proof.FailedSegmentId,
            RelatedEntityId: proof.RelatedSegmentId);
    }

    internal static ExactGeometryNumber SignedArea(MaterialContour contour)
    {
        var area = ExactGeometryNumber.Zero;
        for (var i = 0; i < contour.Segments.Length; i++) area += ExactBezierSegment.From(contour, i).TwiceSignedArea();
        return area;
    }

    private static GradingDiagnostic? ValidatePolygon(MaterialContour contour)
    {
        if (contour.Nodes.Select(n => n.Position).Distinct().Count() < 3) return new("grading_polygon_invalid");
        var coordinates = contour.Nodes.Select(n => new Coordinate((double)n.Position.X, (double)n.Position.Y)).ToArray();
        for (var i = 0; i < coordinates.Length; i++)
            if (coordinates[i].Equals2D(coordinates[(i + 1) % coordinates.Length]))
                return new("grading_polygon_numeric_capacity", EntityId: contour.Segments[i].Id);
        var ring = new GeometryFactory().CreatePolygon(coordinates.Append(coordinates[0].Copy()).ToArray());
        // This existing NTS capability applies only to source polygons, never to approximated Bézier geometry.
        return new IsValidOp(ring).IsValid ? null : new("grading_polygon_invalid");
    }
}
