using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

public sealed class GradingDbContext(DbContextOptions<GradingDbContext> options) : DbContext(options)
{
    internal DbSet<ModelingDocumentRecord> Documents => Set<ModelingDocumentRecord>();
    internal DbSet<ModelingDraftRecord> Drafts => Set<ModelingDraftRecord>();
    internal DbSet<ModelingRevisionRecord> Revisions => Set<ModelingRevisionRecord>();
    internal DbSet<GradingJobRecord> Jobs => Set<GradingJobRecord>();
    internal DbSet<GradingResultRecord> Results => Set<GradingResultRecord>();
    internal DbSet<GradingApprovalRecord> Approvals => Set<GradingApprovalRecord>();
    internal DbSet<ModelingAuditRecord> Audit => Set<ModelingAuditRecord>();
    internal DbSet<ModelingOperationRecord> Operations => Set<ModelingOperationRecord>();
    internal DbSet<GradingAdmissionRecord> Admission => Set<GradingAdmissionRecord>();
    internal DbSet<GradingNestingHandoffRecord> Handoffs => Set<GradingNestingHandoffRecord>();
    protected override void OnModelCreating(ModelBuilder builder) => ModelingSchema.Configure(builder, Database.IsMySql());
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    { AssertMutable(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    { AssertMutable(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); }
    private void AssertMutable()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries())
            if ((entry.State == EntityState.Deleted) || (entry.State == EntityState.Modified &&
                entry.Entity is ModelingRevisionRecord or GradingResultRecord or GradingApprovalRecord or ModelingAuditRecord or ModelingOperationRecord or GradingNestingHandoffRecord))
                throw new InvalidOperationException("modeling_immutable_history");
        foreach (var entry in ChangeTracker.Entries<GradingJobRecord>().Where(e => e.State == EntityState.Modified))
            if (entry.OriginalValues[nameof(GradingJobRecord.State)] is Application.ModelingGrading.GradingJobState state &&
                state is Application.ModelingGrading.GradingJobState.Succeeded or Application.ModelingGrading.GradingJobState.Failed or Application.ModelingGrading.GradingJobState.Cancelled)
                throw new InvalidOperationException("modeling_terminal_job_immutable");
    }
}
