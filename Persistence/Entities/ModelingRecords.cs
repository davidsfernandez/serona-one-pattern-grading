namespace GestionProduccion.Grading.Persistence.Entities;

internal sealed class ModelingDocumentRecord
{
    public Guid Id { get; set; }
    public string CompanyId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public bool IsArchived { get; set; }
    public long Version { get; set; }
    public Guid? CurrentApprovedResultId { get; set; }
    public string OriginalKey { get; set; } = "";
    public string OriginalHash { get; set; } = "";
    public long OriginalLength { get; set; }
    public string FixedGeometryHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
internal sealed class ModelingDraftRecord
{
    public Guid ModelId { get; set; }
    public long Version { get; set; }
    public string ArtifactKey { get; set; } = "";
    public string ArtifactHash { get; set; } = "";
    public long ArtifactLength { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}
internal sealed class ModelingRevisionRecord
{
    public Guid Id { get; set; }
    public Guid ModelId { get; set; }
    public int Number { get; set; }
    public long DraftVersion { get; set; }
    public string ArtifactKey { get; set; } = "";
    public string ArtifactHash { get; set; } = "";
    public long ArtifactLength { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
