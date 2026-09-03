using HybridDeviceTagger.Services;

namespace HybridDeviceTagger.Tests;

public sealed class ActiveDirectorySearchPathTests
{
    [Theory]
    [InlineData("OU=Tier0,OU=Admin")]
    [InlineData("OU=Tier0\\,Privileged,OU=Admin")]
    public void OuOnlyPath_IsForestRelative(string path)
    {
        Assert.True(ActiveDirectorySearchPath.IsForestRelative(path));
        Assert.True(ActiveDirectorySearchPath.IsOuPath(path));
    }

    [Fact]
    public void FullDistinguishedName_IsDomainSpecific()
    {
        const string distinguishedName =
            "OU=Tier0,OU=Admin,DC=child,DC=example,DC=com";

        Assert.False(ActiveDirectorySearchPath.IsForestRelative(distinguishedName));
        Assert.Equal(
            "child.example.com",
            ActiveDirectorySearchPath.GetDomainDnsName(distinguishedName));
    }

    [Fact]
    public void CombineWithDomain_AppendsDomainNamingContext()
    {
        Assert.Equal(
            "OU=Tier0,OU=Admin,DC=example,DC=com",
            ActiveDirectorySearchPath.CombineWithDomain(
                "OU=Tier0,OU=Admin",
                "DC=example,DC=com"));
    }

    [Fact]
    public void RelativePathContainingNonOuComponent_IsRejected()
    {
        Assert.True(ActiveDirectorySearchPath.IsForestRelative("CN=Computers"));
        Assert.False(ActiveDirectorySearchPath.IsOuPath("CN=Computers"));
    }
}