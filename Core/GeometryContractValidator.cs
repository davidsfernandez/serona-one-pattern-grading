using System.Collections.Immutable;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Core;

internal static class GeometryContractValidator
{
    internal static ImmutableArray<GradingDiagnostic> Validate(InternalGeometryDocument doc)
    {
        var errors = ImmutableArray.CreateBuilder<GradingDiagnostic>();
        if (doc is null || doc.ContractVersion != InternalGeometryDocument.SupportedVersion ||
            doc.Units != InternalGeometryDocument.CanonicalUnits || doc.Axes != InternalGeometryDocument.CanonicalAxes)
        { errors.Add(new("grading_contract_unsupported")); return errors.ToImmutable(); }
        if (!ProvenanceValid(doc.Provenance)) errors.Add(new("grading_provenance_invalid"));
        if (!SizesValid(doc.SizeSet)) errors.Add(new("grading_sizes_invalid"));
        if (doc.Pieces.IsDefaultOrEmpty || doc.Rules.IsDefault ||
            !Unique(doc.Pieces.Select(p => p?.Id))) errors.Add(new("grading_identity_invalid"));
        if (errors.Count > 0) return errors.ToImmutable();
        foreach (var piece in doc.Pieces) ValidatePiece(piece, doc, errors);
        if (errors.Count > 0) return errors.ToImmutable();
        ValidateRules(doc, errors);
        return errors.ToImmutable();
    }

    private static bool SizesValid(OrderedSizeSet? set) => set is not null && !set.Sizes.IsDefaultOrEmpty &&
        Unique(set.Sizes.Select(s => s?.Id)) && set.Sizes.All(s => !string.IsNullOrWhiteSpace(s.Label)) &&
        set.Sizes.Select(s => s.Order).Distinct().Count() == set.Sizes.Length && set.Sizes.Any(s => s.Id == set.BaseSizeId);
    private static bool ProvenanceValid(GeometryProvenance? p) => p is not null &&
        !string.IsNullOrWhiteSpace(p.SourceIdentity) && !string.IsNullOrWhiteSpace(p.SourceDigest) && !string.IsNullOrWhiteSpace(p.SourceContract);
    private static bool Unique(IEnumerable<string?> ids)
    {
        var values = ids.ToArray();
        return values.All(s => !string.IsNullOrWhiteSpace(s)) && values.Distinct(StringComparer.Ordinal).Count() == values.Length;
    }

    private static void ValidatePiece(GradingPiece piece, InternalGeometryDocument doc,
        ImmutableArray<GradingDiagnostic>.Builder errors)
    {
        if (!ProvenanceValid(piece.Provenance) || piece.AdditionalContent.IsDefault ||
            !Unique(piece.AdditionalContent.Select(a => a?.Id)) ||
            piece.AdditionalContent.Any(a => !ProvenanceValid(a.Provenance) || string.IsNullOrWhiteSpace(a.DiagnosticCode)))
            errors.Add(new("grading_provenance_invalid", piece.Id));
        if (piece.AvailableSizeIds.IsDefaultOrEmpty || !Unique(piece.AvailableSizeIds) ||
            piece.AvailableSizeIds.Any(id => !doc.SizeSet.Sizes.Any(s => s.Id == id)))
            errors.Add(new("grading_availability_invalid", piece.Id));
        if (piece.MaterialContours.IsDefault || piece.MaterialContours.Length != 1 || piece.MaterialContours[0] is null)
        { errors.Add(new("grading_outer_contour_required", piece.Id)); return; }
        ValidateContour(piece.Id, piece.MaterialContours[0], doc.CurvePolicy, errors);
    }

    private static void ValidateContour(string piece, MaterialContour c, string policy,
        ImmutableArray<GradingDiagnostic>.Builder errors)
    {
        if (string.IsNullOrWhiteSpace(c.Id) || c.Kind != MaterialContourKind.OuterMaterial || !c.Closed ||
            c.Nodes.IsDefaultOrEmpty || c.Segments.IsDefaultOrEmpty || c.ControlSpans.IsDefault ||
            c.Nodes.Length < 2 || c.Nodes.Length != c.Segments.Length ||
            !Unique(c.Nodes.Select(n => n?.Id)) || !Unique(c.Segments.Select(s => s?.Id)))
        { errors.Add(new("grading_contour_structure_invalid", piece, ContourId: c.Id)); return; }
        var validOrder = true;
        for (var i = 0; i < c.Segments.Length; i++)
        {
            var s = c.Segments[i]; var a = c.Nodes[i]; var b = c.Nodes[(i + 1) % c.Nodes.Length];
            if (s.StartNodeId != a.Id || s.EndNodeId != b.Id)
            { errors.Add(new("grading_segment_correspondence_invalid", piece, ContourId: c.Id, EntityId: s.Id)); validOrder = false; }
            if (!Enum.IsDefined(s.Kind)) errors.Add(new("grading_geometry_unsupported", piece, ContourId: c.Id, EntityId: s.Id));
            if (s.Kind == MaterialSegmentKind.Line && (a.OutgoingHandle != default || b.IncomingHandle != default))
                errors.Add(new("grading_line_handles_unsupported", piece, ContourId: c.Id, EntityId: s.Id));
        }
        if (validOrder) ValidateSpans(piece, c, policy, errors);
    }

    private static void ValidateSpans(string piece, MaterialContour c, string policy,
        ImmutableArray<GradingDiagnostic>.Builder errors)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        if (!Unique(c.ControlSpans.Select(s => s?.Id)))
        { errors.Add(new("grading_span_identity_invalid", piece, ContourId: c.Id)); return; }
        foreach (var span in c.ControlSpans)
        {
            var start = c.Nodes.FindIndex(n => n.Id == span.StartControlId);
            var end = c.Nodes.FindIndex(n => n.Id == span.EndControlId);
            if (start < 0 || end <= start || !c.Nodes[start].IsGradingControl || !c.Nodes[end].IsGradingControl ||
                span.SegmentIds.IsDefaultOrEmpty || span.SegmentIds.Length != end - start ||
                c.Nodes.Skip(start + 1).Take(end - start - 1).Any(n => n.IsGradingControl) ||
                !span.SegmentIds.SequenceEqual(c.Segments.Skip(start).Take(end - start).Select(s => s.Id)))
            { errors.Add(new("grading_curve_span_unsupported", piece, ContourId: c.Id, EntityId: span.Id)); continue; }
            if (c.Nodes[start].Position == c.Nodes[end].Position)
                errors.Add(new("grading_curve_span_degenerate", piece, ContourId: c.Id, EntityId: span.Id));
            foreach (var id in span.SegmentIds)
                if (!covered.Add(id)) errors.Add(new("grading_span_overlap", piece, ContourId: c.Id, EntityId: id));
        }
        foreach (var s in c.Segments)
        {
            if (s.Kind == MaterialSegmentKind.CubicBezier &&
                (policy != InternalGeometryDocument.SupportedCurvePolicy || !covered.Contains(s.Id)))
                errors.Add(new("grading_curve_policy_unsupported", piece, ContourId: c.Id, EntityId: s.Id));
            if (!covered.Contains(s.Id) && (!c.Nodes.Single(n => n.Id == s.StartNodeId).IsGradingControl ||
                !c.Nodes.Single(n => n.Id == s.EndNodeId).IsGradingControl))
                errors.Add(new("grading_uncontrolled_segment", piece, ContourId: c.Id, EntityId: s.Id));
        }
    }

    private static void ValidateRules(InternalGeometryDocument doc, ImmutableArray<GradingDiagnostic>.Builder errors)
    {
        if (!Unique(doc.Rules.Select(r => r?.Id)))
        { errors.Add(new("grading_rule_identity_invalid")); return; }
        var sizes = doc.SizeSet.Sizes.OrderBy(s => s.Order).ToArray();
        var baseIndex = Array.FindIndex(sizes, s => s.Id == doc.SizeSet.BaseSizeId);
        var keys = new HashSet<(string, string, string, string)>();
        foreach (var rule in doc.Rules)
        {
            var p = doc.Pieces.SingleOrDefault(p => p.Id == rule.PieceId);
            var from = Array.FindIndex(sizes, s => s.Id == rule.FromSizeId);
            var to = Array.FindIndex(sizes, s => s.Id == rule.ToSizeId);
            var hasControl = p is not null && p.MaterialContours.Length == 1 &&
                !p.MaterialContours[0].Nodes.IsDefault && p.MaterialContours[0].Nodes.Any(n => n?.Id == rule.ControlNodeId && n.IsGradingControl);
            if (!hasControl || from < 0 || to < 0 || Math.Abs(from - to) != 1 ||
                Math.Abs(to - baseIndex) != Math.Abs(from - baseIndex) + 1 ||
                !keys.Add((rule.PieceId, rule.ControlNodeId, rule.FromSizeId, rule.ToSizeId)))
                errors.Add(new("grading_rule_invalid", rule.PieceId, rule.ToSizeId, EntityId: rule.Id));
        }
    }
}

internal static class GeometryIdentityLookup
{
    internal static int FindIndex<T>(this ImmutableArray<T> items, Func<T, bool> predicate)
    {
        for (var i = 0; i < items.Length; i++) if (predicate(items[i])) return i;
        return -1;
    }
}
