namespace HybridDeviceTagger.Models;

public sealed record StoredDirectoryCredential(
    string Username,
    string Password,
    DateTimeOffset? CreatedOn);