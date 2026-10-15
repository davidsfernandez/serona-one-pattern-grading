using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

internal static class GradingLeaseGuard
{
    internal static async Task FenceAdmissionAsync(GradingDbContext db, CancellationToken ct)
    {
        var row = await db.Admission.SingleAsync(a => a.Id == 1, ct);
        row.Version = checked(row.Version + 1);
    }
    internal static bool Current(GradingJobRecord job, GradingWorkItem work, DateTime now)
        => job.State == GradingJobState.Running && job.LeaseToken == work.LeaseToken && job.ClaimVersion == work.ClaimVersion &&
            job.LeaseExpiresAtUtc > now && job.ModelId == work.ModelId && job.RevisionId == work.RevisionId &&
            job.CompanyId == work.Scope.CompanyId && job.OwnerId == work.Scope.OwnerId;
    internal static void ClearLease(GradingJobRecord job) { job.LeaseToken = null; job.LeaseExpiresAtUtc = null; }
    internal static void Cancel(GradingJobRecord job, DateTime now)
    {
        job.State = GradingJobState.Cancelled; job.CancelledAtUtc = now; ClearLease(job); job.Version = checked(job.Version + 1);
    }
    internal static async Task<GradingJobRecord?> JobAsync(GradingDbContext db, GradingWorkItem work, CancellationToken ct)
        => await db.Jobs.SingleOrDefaultAsync(j => j.Id == work.JobId && j.CompanyId == work.Scope.CompanyId && j.OwnerId == work.Scope.OwnerId, ct);
    internal static async Task TouchDocumentAsync(GradingDbContext db, GradingJobRecord job, string action, DateTime now, CancellationToken ct)
    {
        var doc = await ModelingWriteGuard.DocumentAsync(db, new(job.CompanyId, job.OwnerId), job.ModelId, ct);
        ModelingWriteGuard.Touch(doc); ModelingWriteGuard.Audit(db, doc, action, now, job.Id);
    }
}
