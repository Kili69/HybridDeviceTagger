using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

public interface IActiveDirectoryCredentialStore
{
    Task<StoredDirectoryCredential> GetCurrentAsync(CancellationToken cancellationToken);

    Task<StoredDirectoryCredential?> GetNextAsync(CancellationToken cancellationToken);

    Task<StoredDirectoryCredential> StageNextAsync(
        string password,
        CancellationToken cancellationToken);

    Task PromoteAsync(string password, CancellationToken cancellationToken);
}