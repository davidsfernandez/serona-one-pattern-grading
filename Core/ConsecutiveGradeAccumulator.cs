using System.Collections.Immutable;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Core;

internal static class ConsecutiveGradeAccumulator
{
    internal static Dictionary<string, GeometryPoint>? Accumulate(InternalGeometryDocument doc, GradingPiece piece,
        string sizeId, ImmutableArray<GradingDiagnostic>.Builder errors)
    {
        var sizes = doc.SizeSet.Sizes.OrderBy(s => s.Order).ToArray();
        var baseIndex = Array.FindIndex(sizes, s => s.Id == doc.SizeSet.BaseSizeId);
        var target = Array.FindIndex(sizes, s => s.Id == sizeId);
        var contour = piece.MaterialContours[0];
        var points = contour.Nodes.Where(n => n.IsGradingControl).ToDictionary(n => n.Id, n => n.Position);
        var rules = doc.Rules.Where(r => r.PieceId == piece.Id)
            .ToDictionary(r => (r.ControlNodeId, r.FromSizeId, r.ToSizeId));
        var valid = true; var direction = Math.Sign(target - baseIndex);
        for (var index = baseIndex; index != target; index += direction)
        {
            var from = sizes[index].Id; var to = sizes[index + direction].Id;
            foreach (var node in contour.Nodes.Where(n => n.IsGradingControl))
            {
                if (!rules.TryGetValue((node.Id, from, to), out var rule))
                { errors.Add(new("grading_missing_rule", piece.Id, to, contour.Id, node.Id)); valid = false; }
                else
                {
                    try { points[node.Id] += rule.Displacement; }
                    catch (OverflowException)
                    { errors.Add(new("grading_numeric_capacity_exceeded", piece.Id, to, contour.Id, rule.Id)); valid = false; }
                }
            }
        }
        return valid ? points : null;
    }
}
