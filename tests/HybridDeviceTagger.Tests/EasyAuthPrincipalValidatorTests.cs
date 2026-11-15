using System.Text;
using HybridDeviceTagger.Services;
using Microsoft.AspNetCore.Http;

namespace HybridDeviceTagger.Tests;

public sealed class EasyAuthPrincipalValidatorTests
{
    [Fact]
    public void IsAuthenticated_AcceptsEntraPrincipalHeader()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("""{"auth_typ":"aad","claims":[{"typ":"oid","val":"test"}]}"""));

        Assert.True(EasyAuthPrincipalValidator.IsAuthenticated(request));
    }

    [Theory]
    [InlineData("roles")]
    [InlineData("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")]
    public void Authorize_AcceptsSupportedRoleClaimTypes(string roleClaimType)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                $$"""{"auth_typ":"aad","claims":[{"typ":"oid","val":"test"},{"typ":"{{roleClaimType}}","val":"{{ManagementApiRoles.Reader}}"}]}"""));

        var result = EasyAuthPrincipalValidator.Authorize(request, ManagementApiRoles.Reader);

        Assert.Equal(EasyAuthAuthorizationResult.Authorized, result);
    }

    [Fact]
    public void Authorize_RejectsAuthenticatedPrincipalWithoutRequiredRole()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("""{"auth_typ":"aad","claims":[{"typ":"oid","val":"test"}]}"""));

        var result = EasyAuthPrincipalValidator.Authorize(request, ManagementApiRoles.Reader);

        Assert.Equal(EasyAuthAuthorizationResult.Forbidden, result);
    }

    [Theory]
    [InlineData("oid")]
    [InlineData("http://schemas.microsoft.com/identity/claims/objectidentifier")]
    public void TryGetObjectId_AcceptsSupportedObjectIdClaims(string objectIdClaimType)
    {
        var expected = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var request = new DefaultHttpContext().Request;
        request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                $$"""{"auth_typ":"aad","claims":[{"typ":"{{objectIdClaimType}}","val":"{{expected:D}}"}]}"""));

        Assert.True(EasyAuthPrincipalValidator.TryGetObjectId(request, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-base64")]
    public void IsAuthenticated_RejectsMissingOrInvalidHeader(string? header)
    {
        var request = new DefaultHttpContext().Request;
        if (header is not null)
        {
            request.Headers["X-MS-CLIENT-PRINCIPAL"] = header;
        }

        Assert.False(EasyAuthPrincipalValidator.IsAuthenticated(request));
    }
}