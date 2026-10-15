using System.Security.Cryptography;
using GestionProduccion.Application.Storage;

namespace GestionProduccion.Grading.Tests;

internal sealed class PrivateArtifactFixture : IPrivateFileStorage
{
    internal Dictionary<string, byte[]> Bytes { get; } = new();
    internal int Writes { get; private set; }
    internal int? FailAtWrite { get; set; }
    public async Task<StoredPrivateFile> StoreAsync(string key, Stream content, long? maximumLengthBytes = null, CancellationToken ct = default)
    {
        if (++Writes == FailAtWrite) throw new IOException("synthetic storage failure");
        using var buffer = new MemoryStream(); await content.CopyToAsync(buffer, ct); var bytes = buffer.ToArray();
        if (maximumLengthBytes.HasValue && bytes.Length > maximumLengthBytes) throw new IOException("synthetic capacity");
        Bytes.Add(key, bytes); return new(key, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream(Bytes[key], false));
    public Task DeleteAsync(string key, CancellationToken ct = default) { Bytes.Remove(key); return Task.CompletedTask; }
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(Bytes.ContainsKey(key));
}
