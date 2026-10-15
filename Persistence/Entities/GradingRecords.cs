using GestionProduccion.Application.ModelingGrading;

namespace GestionProduccion.Grading.Persistence.Entities;

internal sealed class GradingJobRecord
{
    public Guid Id { get; set; }
    public Guid ModelId { get; set; }
    public Guid RevisionId { get; set; }
    public string CompanyId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public GradingJobState State { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CancellationRequestedAtUtc { get; set; }
    public string? CancellationRequestedBy { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public long ClaimVersion { get; set; }
    public string? LastDiagnosticCode { get; set; }
}
internal sealed class GradingAdmissionRecord
{
    public int Id { get; set; }
    public long Version { get; set; }
}
internal sealed class GradingResultRecord
{
    public Guid Id { get; set; }
    public Guid ModelId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid JobId { get; set; }
    public bool IsValid { get; set; }
    public long SourceJobVersion { get; set; }
    public Guid? SourceLeaseToken { get; set; }
    public string ArtifactKey { get; set; } = "";
    public string ArtifactHash { get; set; } = "";
    public long ArtifactLength { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
internal sealed class GradingApprovalRecord
{
    public Guid Id { get; set; }
    public Guid ModelId { get; set; }
    public Guid ResultId { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTime ApprovedAtUtc { get; set; }
}
internal sealed class ModelingAuditRecord
{
    public Guid Id { get; set; }
    public Guid ModelId { get; set; }
    public string Action { get; set; } = "";
    public string ActorId { get; set; } = "";
    public DateTime AtUtc { get; set; }
    public Guid? ResourceId { get; set; }
}
internal sealed class ModelingOperationRecord
{
    public Guid Id { get; set; }
    public string CompanyId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public Guid OperationId { get; set; }
    public string Kind { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public Guid ModelId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid? JobId { get; set; }
    public Guid? ApprovalId { get; set; }
}
