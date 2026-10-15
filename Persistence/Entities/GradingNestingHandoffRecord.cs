namespace GestionProduccion.Grading.Persistence.Entities;

internal sealed class GradingNestingHandoffRecord
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public Guid ModelId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid ResultId { get; set; }
    public Guid ApprovalId { get; set; }
    public string CompanyId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string ArtifactKey { get; set; } = "";
    public string ArtifactHash { get; set; } = "";
    public long ArtifactLength { get; set; }
    public string GeometryHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
