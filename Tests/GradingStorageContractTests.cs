using System.Security.Cryptography;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Application.Storage;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingStorageContractTests
{
    [Fact]
    public async Task ArtifactStoreUsesOpaqueGuidAndNormalizesLocalStorageHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "serona-grading-storage", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalPrivateFileStorage(new PrivateFileStorageOptions { PrivateRoot = root });
            var artifacts = new ModelingArtifactStore(storage, 4096);
            var payload = new SyntheticPayload("synthetic-json");

            var artifact = await artifacts.WriteDocumentAsync(payload);
            var restored = await artifacts.ReadAsync<SyntheticPayload>(artifact);
            await using var bytes = await storage.OpenReadAsync(artifact.Key);
            using var buffer = new MemoryStream();
            await bytes.CopyToAsync(buffer);

            Assert.True(Guid.TryParseExact(artifact.Key, "N", out _));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant(), artifact.Sha256);
            Assert.Equal(payload, restored);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed record SyntheticPayload(string Value);
}
