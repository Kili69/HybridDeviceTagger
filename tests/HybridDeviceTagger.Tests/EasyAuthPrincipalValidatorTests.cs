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