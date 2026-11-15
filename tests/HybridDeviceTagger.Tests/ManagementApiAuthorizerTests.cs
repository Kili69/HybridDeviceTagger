using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Tests;

public sealed class ManagementApiAuthorizerTests
{
    private const string FunctionAppResourceId =
        "/subscriptions/11111111-1111-1111-1111-111111111111" +
        "/resourceGroups/rg-test/providers/Microsoft.Web/sites/function-test";

    [Theory]
    [InlineData(
        "8e3af657-a8ff-443c-a75c-2fe8c4bcb635",
        ManagementApiRoles.RuleAdministrator)]
    [InlineData(
        "b24988ac-6180-42a0-ab88-20f7382dd24c",
        ManagementApiRoles.PasswordRotationAdministrator)]
    public async Task AuthorizeAsync_AcceptsOwnerAndContributor(
        string roleDefinitionId,
        string requiredRole)
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            $$"""
            {
              "value": [
                {
                  "properties": {
                    "roleDefinitionId": "/subscriptions/test/providers/Microsoft.Authorization/roleDefinitions/{{roleDefinitionId}}"
                  }
                }
              ]
            }
            """));
        var authorizer = CreateAuthorizer(handler);

        var result = await authorizer.AuthorizeAsync(
            AuthenticatedRequest(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            [requiredRole],
            CancellationToken.None);

        Assert.Equal(EasyAuthAuthorizationResult.Authorized, result);
        Assert.Contains(
            "assignedTo%28%2722222222-2222-2222-2222-222222222222%27%29",
            handler.RequestUri?.Query,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-token", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task AuthorizeAsync_RejectsOtherAzureRoles()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """
            {
              "value": [
                {
                  "properties": {
                    "roleDefinitionId": "/subscriptions/test/providers/Microsoft.Authorization/roleDefinitions/acdd72a7-3385-48ef-bd42-f606fba81ae7"
                  }
                }
              ]
            }
            """));
        var authorizer = CreateAuthorizer(handler);

        var result = await authorizer.AuthorizeAsync(
            AuthenticatedRequest(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            [ManagementApiRoles.RuleAdministrator],
            CancellationToken.None);

        Assert.Equal(EasyAuthAuthorizationResult.Forbidden, result);
    }

    [Fact]
    public async Task AuthorizeAsync_DoesNotUseAzureRbacForOtherManagementRoles()
    {
        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("ARM must not be called."));
        var authorizer = CreateAuthorizer(handler);

        var result = await authorizer.AuthorizeAsync(
            AuthenticatedRequest(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            [ManagementApiRoles.TaggingOperator],
            CancellationToken.None);

        Assert.Equal(EasyAuthAuthorizationResult.Forbidden, result);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task AuthorizeAsync_PropagatesAzureRbacFailures()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden));
        var authorizer = CreateAuthorizer(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            authorizer.AuthorizeAsync(
                AuthenticatedRequest(Guid.Parse("22222222-2222-2222-2222-222222222222")),
                [ManagementApiRoles.PasswordRotationAdministrator],
                CancellationToken.None));
    }

    private static ManagementApiAuthorizer CreateAuthorizer(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            new StubTokenCredential(),
            Microsoft.Extensions.Options.Options.Create(new ManagementAuthorizationOptions
            {
                FunctionAppResourceId = FunctionAppResourceId
            }));

    private static HttpRequest AuthenticatedRequest(Guid objectId)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                auth_typ = "aad",
                claims = new[]
                {
                    new { typ = "oid", val = objectId.ToString("D") }
                }
            })));
        return request;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(responseFactory(request));
        }
    }
}
