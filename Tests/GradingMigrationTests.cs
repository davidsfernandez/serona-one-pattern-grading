using GestionProduccion.Grading.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingMigrationTests
{
    [Fact] public void InitialMigrationIsAdditiveAndContainsOnlyTheApprovedPrivateTables()
    {
        using var db = new GradingDbContext(new DbContextOptionsBuilder<GradingDbContext>()
            .UseMySql("Server=127.0.0.1;Database=offline;User=offline;Password=offline", new MySqlServerVersion(new Version(8, 0, 36))).Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.Equal(3, assembly.Migrations.Count);
        var entry = assembly.Migrations.OrderBy(m => m.Key).First();
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        var expected = new[] { "ModelingDocuments", "ModelingDrafts", "ModelingRevisions", "GradingJobs", "GradingResults", "GradingApprovals", "ModelingAudit", "ModelingOperations" };
        Assert.Equal(expected.Order(), migration.UpOperations.OfType<CreateTableOperation>().Select(t => t.Name).Order());
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation or AddForeignKeyOperation or AlterDatabaseOperation));
        Assert.All(migration.UpOperations.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys), fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.NotNull(assembly.ModelSnapshot);
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(assembly.ModelSnapshot!.Model, designTime: true);
        Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(snapshot.GetRelationalModel(),
            db.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
        var sql = db.GetService<IMigrator>().GenerateScript();
        Assert.DoesNotContain("NestingJobs", sql); Assert.DoesNotContain("NestingAdmission", sql);
        Assert.DoesNotContain("DROP TABLE", sql); Assert.DoesNotContain("DELETE FROM", sql);
        Assert.Contains("ModelingOperations", sql);
        var model = db.GetService<IDesignTimeModel>().Model;
        Assert.All(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(model.GetEntityTypes().SelectMany(e => e.GetIndexes()), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "CompanyId", "OwnerId", "OperationId" }));
        Assert.Contains(model.GetEntityTypes().SelectMany(e => e.GetProperties()), p => p.Name == "Version" && p.IsConcurrencyToken);
    }

    [Fact] public void LifecycleMigrationAddsOnlyGradingLeaseColumnsAndAdmissionFence()
    {
        using var db = new GradingDbContext(new DbContextOptionsBuilder<GradingDbContext>()
            .UseMySql("Server=127.0.0.1;Database=offline;User=offline;Password=offline", new MySqlServerVersion(new Version(8, 0, 36))).Options);
        var assembly = db.GetService<IMigrationsAssembly>(); Assert.Equal(3, assembly.Migrations.Count);
        var migration = assembly.CreateMigration(assembly.Migrations.Single(m => m.Key.EndsWith("AddGradingDurableLifecycle")).Value, db.Database.ProviderName!);
        Assert.Equal("GradingAdmission", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), c => Assert.True(c.Table == "GradingJobs" ||
            c.Table == "GradingResults" && c.Name == "SourceLeaseToken"));
        Assert.Contains(migration.UpOperations.OfType<AddColumnOperation>(), c => c.Name == "LeaseToken");
        Assert.Contains(migration.UpOperations.OfType<InsertDataOperation>(), i => i.Table == "GradingAdmission");
        Assert.All(migration.UpOperations, o => Assert.True(o is CreateTableOperation or AddColumnOperation or CreateIndexOperation or InsertDataOperation));
    }
    [Fact] public void HandoffMigrationAddsOnlyPinnedGradingPreparationMetadata()
    {
        using var db = new GradingDbContext(new DbContextOptionsBuilder<GradingDbContext>()
            .UseMySql("Server=127.0.0.1;Database=offline;User=offline;Password=offline", new MySqlServerVersion(new Version(8, 0, 36))).Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.Equal(3, assembly.Migrations.Count);
        var migration = assembly.CreateMigration(assembly.Migrations.Single(m => m.Key.EndsWith("AddGradingNestingHandoffs")).Value, db.Database.ProviderName!);
        Assert.Equal("GradingNestingHandoffs", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        Assert.All(migration.UpOperations, o => Assert.True(o is CreateTableOperation or CreateIndexOperation));
        Assert.All(migration.UpOperations.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys), fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
    }
}
