using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

public interface IActiveDirectoryPasswordClient
{
    Task<bool> CanBindAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken);

    Task ChangePasswordAsync(
        StoredDirectoryCredential currentCredential,
        string newPassword,
        CancellationToken cancellationToken);
}