using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence.Entities;

namespace GestionProduccion.Grading.Persistence;

internal static class ModelingSnapshotMapping
{
    internal static ModelingDocumentSnapshot Snapshot(this ModelingDocumentRecord x) => new(x.Id, new(x.CompanyId, x.OwnerId),
        x.IsArchived, x.Version, x.CurrentApprovedResultId, new(x.OriginalKey, x.OriginalHash, x.OriginalLength), x.FixedGeometryHash);
    internal static ModelingDraftSnapshot Snapshot(this ModelingDraftRecord x) => new(x.ModelId, x.Version, new(x.ArtifactKey, x.ArtifactHash, x.ArtifactLength));
    internal static ModelingRevisionSnapshot Snapshot(this ModelingRevisionRecord x) => new(x.Id, x.ModelId, x.Number, x.DraftVersion,
        new(x.ArtifactKey, x.ArtifactHash, x.ArtifactLength));
    internal static GradingRequestSnapshot Snapshot(this GradingJobRecord x) => new(x.Id, x.RevisionId, x.ModelId, x.State, x.Version,
        x.LastDiagnosticCode, x.CancellationRequestedAtUtc is { } requested ? Utc(requested) : null,
        x.CancelledAtUtc is { } cancelled ? Utc(cancelled) : null);
    internal static GradingResultSnapshot Snapshot(this GradingResultRecord x) => new(x.Id, x.JobId, x.RevisionId, x.ModelId, x.IsValid,
        new(x.ArtifactKey, x.ArtifactHash, x.ArtifactLength), x.SourceJobVersion, x.SourceLeaseToken);
    internal static GradingApprovalSnapshot Snapshot(this GradingApprovalRecord x) => new(x.Id, x.ResultId, x.ModelId, x.ApprovedBy, Utc(x.ApprovedAtUtc));
    internal static ModelingAuditSnapshot Snapshot(this ModelingAuditRecord x) => new(x.Id, x.ModelId, x.Action, x.ActorId, Utc(x.AtUtc), x.ResourceId);
    internal static ModelingOperationSnapshot Snapshot(this ModelingOperationRecord x) => new(x.OperationId, x.Kind, x.Fingerprint,
        x.ModelId, x.RevisionId, x.JobId, x.ApprovalId);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
