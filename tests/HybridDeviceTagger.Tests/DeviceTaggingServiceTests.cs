using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Tests;

public sealed class DeviceTaggingServiceTests
{
    [Fact]
    public async Task TagDevicesAsync_UpdatesOnlyUntaggedHybridDevice()
    {
        var updateDeviceId = Guid.NewGuid();
        var unchangedDeviceId = Guid.NewGuid();
        var missingDeviceId = Guid.NewGuid();
        var nonHybridDeviceId = Guid.NewGuid();
        var directorySource = new StubDirectoryDeviceSource(
        [
            CreateDirectoryDevice(updateDeviceId),
            CreateDirectoryDevice(unchangedDeviceId),
            CreateDirectoryDevice(missingDeviceId),
            CreateDirectoryDevice(nonHybridDeviceId)
        ]);
        var entraClient = new StubEntraDeviceClient(new Dictionary<Guid, EntraDevice>
        {
            [updateDeviceId] = CreateEntraDevice(updateDeviceId, "old value", "ServerAd"),
            [unchangedDeviceId] = CreateEntraDevice(unchangedDeviceId, "Tier 0", "ServerAd"),
            [nonHybridDeviceId] = CreateEntraDevice(nonHybridDeviceId, null, "AzureAd")
        });
        var service = new DeviceTaggingService(
            directorySource,
            entraClient,
            Microsoft.Extensions.Options.Options.Create(
                new TaggingOptions { ExtensionAttribute1Value = "Tier 0" }),
            NullLogger<DeviceTaggingService>.Instance);

        var result = await service.TagDevicesAsync(CancellationToken.None);

        Assert.Equal(new TaggingResult(4, 1, 1, 1, 1), result);
        var update = Assert.Single(entraClient.Updates);
        Assert.Equal(updateDeviceId.ToString("D"), update.ObjectId);
        Assert.Equal("Tier 0", update.Value);
    }

    private static DirectoryDevice CreateDirectoryDevice(Guid deviceId) =>
        new(deviceId, $"Computer-{deviceId:N}", $"CN={deviceId:N},OU=Computers,DC=example,DC=com");

    private static EntraDevice CreateEntraDevice(
        Guid deviceId,
        string? extensionAttribute1,
        string trustType) =>
        new(deviceId.ToString("D"), deviceId, $"Computer-{deviceId:N}", trustType, extensionAttribute1);

    private sealed class StubDirectoryDeviceSource : IActiveDirectoryDeviceSource
    {
        private readonly IReadOnlyList<DirectoryDevice> _devices;

        public StubDirectoryDeviceSource(IReadOnlyList<DirectoryDevice> devices)
        {
            _devices = devices;
        }

        public Task<IReadOnlyList<DirectoryDevice>> GetHybridJoinedDevicesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(_devices);
    }

    private sealed class StubEntraDeviceClient : IEntraDeviceClient
    {
        private readonly IReadOnlyDictionary<Guid, EntraDevice> _devices;

        public StubEntraDeviceClient(IReadOnlyDictionary<Guid, EntraDevice> devices)
        {
            _devices = devices;
        }

        public List<(string ObjectId, string Value)> Updates { get; } = [];

        public Task<EntraDevice?> FindByDeviceIdAsync(
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            _devices.TryGetValue(deviceId, out var device);
            return Task.FromResult(device);
        }

        public Task SetExtensionAttribute1Async(
            string entraObjectId,
            string value,
            CancellationToken cancellationToken)
        {
            Updates.Add((entraObjectId, value));
            return Task.CompletedTask;
        }
    }
}