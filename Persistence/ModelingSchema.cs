using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GestionProduccion.Grading.Persistence;

internal static class ModelingSchema
{
    internal static void Configure(ModelBuilder b, bool mysql)
    {
        var document = b.Entity<ModelingDocumentRecord>();
        document.ToTable("ModelingDocuments"); document.HasKey(x => x.Id);
        document.Property(x => x.Version).IsConcurrencyToken();
        document.HasOne<GradingResultRecord>().WithMany().HasForeignKey(x => new { x.CurrentApprovedResultId, x.Id })
            .HasPrincipalKey(x => new { x.Id, x.ModelId }).OnDelete(DeleteBehavior.Restrict);
        document.HasIndex(x => new { x.CompanyId, x.OwnerId, x.IsArchived });
        document.Property(x => x.CompanyId).HasMaxLength(128); document.Property(x => x.OwnerId).HasMaxLength(128);
        var draft = b.Entity<ModelingDraftRecord>(); draft.ToTable("ModelingDrafts"); draft.HasKey(x => x.ModelId);
        draft.Property(x => x.Version).IsConcurrencyToken();
        draft.HasOne<ModelingDocumentRecord>().WithOne().HasForeignKey<ModelingDraftRecord>(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
        var revision = b.Entity<ModelingRevisionRecord>(); revision.ToTable("ModelingRevisions"); revision.HasKey(x => x.Id);
        revision.HasIndex(x => new { x.ModelId, x.Number }).IsUnique();
        revision.HasAlternateKey(x => new { x.Id, x.ModelId });
        revision.HasOne<ModelingDocumentRecord>().WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
        ConfigureJobsAndResults(b);
        var admission = b.Entity<GradingAdmissionRecord>(); admission.ToTable("GradingAdmission"); admission.HasKey(x => x.Id);
        admission.Property(x => x.Version).IsConcurrencyToken(); admission.HasData(new GradingAdmissionRecord { Id = 1, Version = 1 });
        var approval = b.Entity<GradingApprovalRecord>(); approval.ToTable("GradingApprovals"); approval.HasKey(x => x.Id);
        approval.HasOne<GradingResultRecord>().WithMany().HasForeignKey(x => new { x.ResultId, x.ModelId })
            .HasPrincipalKey(x => new { x.Id, x.ModelId }).OnDelete(DeleteBehavior.Restrict);
        var audit = b.Entity<ModelingAuditRecord>(); audit.ToTable("ModelingAudit"); audit.HasKey(x => x.Id);
        audit.HasOne<ModelingDocumentRecord>().WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
        var operation = b.Entity<ModelingOperationRecord>(); operation.ToTable("ModelingOperations"); operation.HasKey(x => x.Id);
        operation.HasIndex(x => new { x.CompanyId, x.OwnerId, x.OperationId }).IsUnique();
        operation.HasOne<ModelingRevisionRecord>().WithMany().HasForeignKey(x => new { x.RevisionId, x.ModelId })
            .HasPrincipalKey(x => new { x.Id, x.ModelId }).OnDelete(DeleteBehavior.Restrict);
        ConfigureHandoffs(b);
        ConfigureStrings(b, mysql);
    }

    private static void ConfigureHandoffs(ModelBuilder b)
    {
        var handoff = b.Entity<GradingNestingHandoffRecord>();
        handoff.ToTable("GradingNestingHandoffs"); handoff.HasKey(x => x.Id);
        handoff.HasIndex(x => new { x.CompanyId, x.OwnerId, x.OperationId }).IsUnique();
        handoff.HasOne<ModelingDocumentRecord>().WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
        handoff.HasOne<ModelingRevisionRecord>().WithMany().HasForeignKey(x => new { x.RevisionId, x.ModelId })
            .HasPrincipalKey(x => new { x.Id, x.ModelId }).OnDelete(DeleteBehavior.Restrict);
        handoff.HasOne<GradingResultRecord>().WithMany().HasForeignKey(x => new { x.ResultId, x.ModelId })
            .HasPrincipalKey(x => new { x.Id, x.ModelId }).OnDelete(DeleteBehavior.Restrict);
        handoff.HasOne<GradingApprovalRecord>().WithMany().HasForeignKey(x => x.ApprovalId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureJobsAndResults(ModelBuilder b)
    {
        var job = b.Entity<GradingJobRecord>(); job.ToTable("GradingJobs"); job.HasKey(x => x.Id);
        job.Property(x => x.State).HasConversion<string>().HasMaxLength(24); job.Property(x => x.Version).IsConcurrencyToken();
        job.HasIndex(x => new { x.ModelId, x.State }); job.HasIndex(x => new { x.CompanyId, x.OwnerId, x.State });
        job.HasIndex(x => new { x.State, x.LeaseExpiresAtUtc });
        job.HasAlternateKey(x => new { x.Id, x.RevisionId, x.ModelId });
        job.HasOne<ModelingRevisionRecord>().WithMany().HasForeignKey(x => new { x.RevisionId, x.ModelId })
            .HasPrincipalKey(x => new { x.Id, x.ModelId }).OnDelete(DeleteBehavior.Restrict);
        var result = b.Entity<GradingResultRecord>(); result.ToTable("GradingResults"); result.HasKey(x => x.Id);
        result.HasIndex(x => x.JobId).IsUnique(); result.HasAlternateKey(x => new { x.Id, x.ModelId });
        result.HasOne<GradingJobRecord>().WithOne().HasForeignKey<GradingResultRecord>(x => new { x.JobId, x.RevisionId, x.ModelId })
            .HasPrincipalKey<GradingJobRecord>(x => new { x.Id, x.RevisionId, x.ModelId }).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureStrings(ModelBuilder b, bool mysql)
    {
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(string)))
            {
                property.SetMaxLength(property.Name.Contains("Hash") || property.Name == "Fingerprint" ? 64 : 160);
                if (property.Name is "OwnerId" or "CompanyId")
                { property.SetMaxLength(128); if (mysql) property.SetCollation("utf8mb4_bin"); }
            }
    }
}
