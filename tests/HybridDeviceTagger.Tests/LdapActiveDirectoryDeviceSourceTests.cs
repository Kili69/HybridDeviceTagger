using System.DirectoryServices.Protocols;
using HybridDeviceTagger.Services;

namespace HybridDeviceTagger.Tests;

public sealed class LdapActiveDirectoryDeviceSourceTests
{
    [Fact]
    public void SearchConfiguration_UsesHybridJoinCertificateAndObjectGuid()
    {
        Assert.Equal(
            "(&(objectCategory=computer)(userCertificate=*))",
            LdapActiveDirectoryDeviceSource.ComputerFilter);
        Assert.Equal("objectGUID", LdapActiveDirectoryDeviceSource.DeviceIdAttribute);
    }

    [Fact]
    public void TryReadGuid_ConvertsBinaryObjectGuid()
    {
        var expected = Guid.Parse("fa95010e-8a87-4a85-924f-ce9c433900d0");
        var attribute = new DirectoryAttribute("objectGUID");
        attribute.Add(expected.ToByteArray());

        var parsed = LdapActiveDirectoryDeviceSource.TryReadGuid(attribute, out var actual);

        Assert.True(parsed);
        Assert.Equal(expected, actual);
    }
}