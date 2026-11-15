using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HybridDeviceTagger.Tests;

public sealed class PortalTests
{
    private const string FunctionAppResourceId =
        "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.Web/sites/func";

    [Fact]
    public async Task TrustStore_SeedsFromPackageAndManagesAnchorsWithoutLosingTheLast()
    {
        using var packaged = CreateCertificate("CN=Packaged Root", ca: true);
        var packagePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(packagePath, packaged.ExportCertificatePem());
        try
        {
            var blob = new InMemoryBlob();
            var store = CreateTrustStore(blob, packagePath);

            var initial = await store.GetAsync(CancellationToken.None);
            Assert.Equal("package", initial.Source);
            Assert.Equal("installer", Assert.Single(initial.Certificates).AddedBy);

            using var added = CreateCertificate("CN=Second Root", ca: true);
            var afterAdd = await store.AddAsync(
                Convert.ToBase64String(added.Export(X509ContentType.Cert)), "admin@contoso.com", initial.ETag, CancellationToken.None);
            Assert.Equal("blob", afterAdd.Source);
            Assert.Equal([packaged.Thumbprint, added.Thumbprint], afterAdd.Certificates.Select(certificate => certificate.Thumbprint));

            await Assert.ThrowsAsync<ValidationException>(() => store.AddAsync(
                added.ExportCertificatePem(), "admin", afterAdd.ETag, CancellationToken.None));

            using var renewed = CreateCertificate("CN=Renewed Root", ca: true);
            var afterReplace = await store.ReplaceAsync(
                packaged.Thumbprint, renewed.ExportCertificatePem(), "admin", afterAdd.ETag, CancellationToken.None);
            var afterRemove = await store.RemoveAsync(added.Thumbprint, afterReplace.ETag, CancellationToken.None);

            Assert.Equal(renewed.Thumbprint, Assert.Single(afterRemove.Certificates).Thumbprint);
            await Assert.ThrowsAsync<ValidationException>(() =>
                store.RemoveAsync(renewed.Thumbprint, afterRemove.ETag, CancellationToken.None));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                store.RemoveAsync("00", afterRemove.ETag, CancellationToken.None));
            Assert.Equal(renewed.Thumbprint, Assert.Single(await store.GetTrustAnchorsAsync(CancellationToken.None)).Thumbprint);
            Assert.DoesNotContain("PRIVATE KEY", blob.Content, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task TrustStore_RejectsPrivateKeysExpiredAndMalformedCertificates()
    {
        var store = CreateTrustStore(new InMemoryBlob(), null);
        using var key = RSA.Create(2048);
        var expired = new CertificateRequest("CN=Expired", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1));

        await Assert.ThrowsAsync<ValidationException>(() => store.AddAsync(
            expired.ExportCertificatePem() + key.ExportPkcs8PrivateKeyPem(), "admin", null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => store.AddAsync(
            expired.ExportCertificatePem(), "admin", null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => store.AddAsync(
            "not a certificate", "admin", null, CancellationToken.None));
        Assert.Equal("operatingSystem", (await store.GetAsync(CancellationToken.None)).Source);
    }

    [Fact]
    public void CertificateValidation_ExplainsRejectionsAndMatchesAnySubjectAlternativeName()
    {
        using var certificate = CreateCertificate("CN=dc01.contoso.com", ca: false, "dc01.contoso.com", "contoso.com");
        using var other = CreateCertificate("CN=Other Root", ca: true);

        Assert.True(ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "contoso.com", certificate, [certificate], out var reason));
        Assert.Null(reason);

        Assert.False(ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc02.contoso.com", certificate, [certificate], out reason));
        Assert.Contains("dc01.contoso.com, contoso.com", reason, StringComparison.Ordinal);

        Assert.False(ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc01.contoso.com", certificate, [other], out reason));
        Assert.Contains("is not trusted", reason, StringComparison.Ordinal);
        Assert.Contains("CN=Other Root", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityQuery_UsesOnlyAllowListedValuesAndParsesRows()
    {
        var query = LogAnalyticsActivityLogReader.BuildQuery(new ActivityQuery(
            24, ["Configuration", "x') | union *"], ["Denied", "'"], 50));

        Assert.Contains("Category in ('Configuration')", query, StringComparison.Ordinal);
        Assert.Contains("Outcome in ('Denied')", query, StringComparison.Ordinal);
        Assert.DoesNotContain("union", query, StringComparison.Ordinal);
        Assert.StartsWith("traces", query, StringComparison.Ordinal);
        Assert.Contains("customDimensions.CategoryName", query, StringComparison.Ordinal);
        Assert.EndsWith("| take 50", query, StringComparison.Ordinal);

        using var document = JsonDocument.Parse("""
            {"tables":[{"columns":[{"name":"TimeGenerated"},{"name":"Category"},{"name":"Action"},{"name":"Outcome"},
            {"name":"Actor"},{"name":"Target"},{"name":"Details"}],
            "rows":[["2026-11-15T16:00:00Z","Configuration","RuleUpserted","Succeeded","admin@contoso.com","Servers",""]]}]}
            """);
        var record = Assert.Single(LogAnalyticsActivityLogReader.ParseRecords(document.RootElement));
        Assert.Equal("RuleUpserted", record.Action);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 16, 0, 0, TimeSpan.Zero), record.TimestampUtc);
    }

    [Fact]
    public async Task ChangeAccount_RestoresPreviousPasswordWhenAccountCannotBeStored()
    {
        var credentials = new Mock<IActiveDirectoryCredentialStore>();
        credentials.Setup(value => value.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredDirectoryCredential("old@contoso.com", "old-password", null));
        var replaced = new List<string>();
        credentials.Setup(value => value.ReplaceCurrentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((password, _) => replaced.Add(password))
            .ReturnsAsync(new StoredDirectoryCredential("x", "x", null));
        var configuration = new Mock<IActiveDirectoryConfigurationStore>();
        configuration.Setup(value => value.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDirectoryConfigurationSnapshot("contoso.com", "\"v1\"", null, "blob"));
        configuration.Setup(value => value.ReplaceServiceAccountAsync(
                "new@contoso.com", string.Empty, "\"v1\"", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(412, "Precondition failed"));
        var service = CreateAccountService(credentials, configuration, bindSucceeds: true, lockAvailable: true);

        await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.ChangeAccountAsync("new@contoso.com", "new-password", null, CancellationToken.None));

        Assert.Equal(["new-password", "old-password"], replaced);
        credentials.Verify(value => value.ClearNextAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeAccount_RequiresSuccessfulBindAndFreeRotationLock()
    {
        var credentials = new Mock<IActiveDirectoryCredentialStore>(MockBehavior.Strict);
        var configuration = new Mock<IActiveDirectoryConfigurationStore>(MockBehavior.Strict);

        await Assert.ThrowsAsync<OperationAlreadyRunningException>(() =>
            CreateAccountService(credentials, configuration, bindSucceeds: true, lockAvailable: false)
                .ChangeAccountAsync("new@contoso.com", "password", null, CancellationToken.None));
        await Assert.ThrowsAsync<ActiveDirectoryCredentialValidationException>(() =>
            CreateAccountService(credentials, configuration, bindSucceeds: false, lockAvailable: true)
                .ChangeAccountAsync("new@contoso.com", "password", null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateAccountService(credentials, configuration, bindSucceeds: true, lockAvailable: true)
                .ChangeAccountAsync("not-a-upn", "password", null, CancellationToken.None));
    }

    [Fact]
    public async Task ChangeAccount_RejectsDistinguishedNameOfAnotherAccount()
    {
        var credentials = new Mock<IActiveDirectoryCredentialStore>(MockBehavior.Strict);
        var configuration = new Mock<IActiveDirectoryConfigurationStore>(MockBehavior.Strict);
        var service = CreateAccountService(
            credentials, configuration, bindSucceeds: true, lockAvailable: true,
            failingBindName: "CN=old-account,DC=contoso,DC=com");

        var exception = await Assert.ThrowsAsync<ValidationException>(() => service.ChangeAccountAsync(
            "new@contoso.com", "password", "CN=old-account,DC=contoso,DC=com", CancellationToken.None));

        Assert.Contains("distinguished name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortalApi_DeniesWithoutOwnerOrContributorAndRecordsTheDenial()
    {
        var authorizer = new Mock<IManagementApiAuthorizer>(MockBehavior.Strict);
        authorizer.Setup(value => value.AuthorizeFunctionAppAdministratorAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EasyAuthAuthorizationResult.Forbidden);
        var activity = new RecordingActivityLog();
        var trustStore = new Mock<ITrustedCertificateStore>(MockBehavior.Strict);
        var function = CreatePortalApi(authorizer.Object, activity, trustStore.Object);

        var result = await function.GetCertificatesAsync(
            AuthenticatedRequest(ManagementApiRoles.RuleAdministrator), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        var entry = Assert.Single(activity.Entries);
        Assert.Equal((ActivityCategories.Access, ActivityOutcomes.Denied), (entry.Category, entry.Outcome));
        Assert.Contains("admin@contoso.com", entry.Actor, StringComparison.Ordinal);
        trustStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PortalApi_AddsCertificateAndRecordsActorAndThumbprint()
    {
        var authorizer = new Mock<IManagementApiAuthorizer>();
        authorizer.Setup(value => value.AuthorizeFunctionAppAdministratorAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EasyAuthAuthorizationResult.Authorized);
        var activity = new RecordingActivityLog();
        var store = CreateTrustStore(new InMemoryBlob(), null);
        var function = CreatePortalApi(authorizer.Object, activity, store);
        using var certificate = CreateCertificate("CN=Root", ca: true);
        var request = AuthenticatedRequest();
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { certificate = certificate.ExportCertificatePem() })));

        var result = await function.AddCertificateAsync(request, CancellationToken.None);

        var snapshot = Assert.IsType<TrustedCertificateSnapshot>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Contains("admin@contoso.com", Assert.Single(snapshot.Certificates).AddedBy, StringComparison.Ordinal);
        var entry = Assert.Single(activity.Entries);
        Assert.Equal(("TrustAnchorAdded", ActivityOutcomes.Succeeded, certificate.Thumbprint), (entry.Action, entry.Outcome, entry.Target));
    }

    [Fact]
    public async Task Authorizer_AdministratorCheckIgnoresApplicationRolesAndCachesRbac()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"value":[{"properties":{"roleDefinitionId":"/x/acdd72a7-3385-48ef-bd42-f606fba81ae7"}}]}""")
            };
        });
        var authorizer = new ManagementApiAuthorizer(
            new HttpClient(handler),
            new StubCredential(),
            Microsoft.Extensions.Options.Options.Create(new ManagementAuthorizationOptions { FunctionAppResourceId = FunctionAppResourceId }));
        var request = AuthenticatedRequest(ManagementApiRoles.RuleAdministrator, ManagementApiRoles.PasswordRotationAdministrator);

        Assert.Equal(EasyAuthAuthorizationResult.Forbidden,
            await authorizer.AuthorizeFunctionAppAdministratorAsync(request, CancellationToken.None));
        Assert.Equal(EasyAuthAuthorizationResult.Forbidden,
            await authorizer.AuthorizeFunctionAppAdministratorAsync(request, CancellationToken.None));
        Assert.Equal(1, calls);
        Assert.Equal(EasyAuthAuthorizationResult.Unauthenticated,
            await authorizer.AuthorizeFunctionAppAdministratorAsync(new DefaultHttpContext().Request, CancellationToken.None));
    }

    [Fact]
    public void DirectorySearch_EscapesInputAndMarksUnmanageableObjects()
    {
        Assert.Equal("O''Brien", GraphEntraDirectorySearch.EscapeLiteral(" O'Brien "));
        Assert.Throws<ArgumentException>(() => GraphEntraDirectorySearch.EscapeLiteral(" "));
        Assert.Throws<ArgumentException>(() => GraphEntraDirectorySearch.EscapeLiteral(new string('x', 65)));

        static DirectoryObjectSummary Group(string properties) => GraphEntraDirectorySearch.ToGroup(
            JsonDocument.Parse("""{"id":"11111111-1111-1111-1111-111111111111","displayName":"Servers",""" + properties + "}").RootElement);
        static DirectoryObjectSummary Unit(string properties) => GraphEntraDirectorySearch.ToAdministrativeUnit(
            JsonDocument.Parse("""{"id":"11111111-1111-1111-1111-111111111111","displayName":"Tier 0",""" + properties + "}").RootElement);

        Assert.True(Group("""
            "groupTypes":[],"mailEnabled":false
            """).Assignable);
        Assert.False(Group("""
            "groupTypes":["DynamicMembership"]
            """).Assignable);
        Assert.False(Group("""
            "groupTypes":[],"mailEnabled":true
            """).Assignable);
        Assert.False(Group("""
            "groupTypes":[],"onPremisesSyncEnabled":true
            """).Assignable);
        Assert.False(Unit("""
            "membershipType":"Dynamic"
            """).Assignable);
        var restricted = Unit("""
            "isMemberManagementRestricted":true
            """);
        Assert.True(restricted.Assignable);
        Assert.Contains("Restricted", restricted.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("202611.1-dev", "202611.1", "dev")]
    [InlineData("202611.2", "202611.2", null)]
    [InlineData("202611.3+abc", "202611.3", null)]
    public void ProductVersion_ShowsBranchOnlyOutsideMain(string informational, string version, string? branch) =>
        Assert.Equal((version, branch), PortalFunction.ParseProductVersion(informational));

    [Fact]
    public async Task ScheduleStore_UsesAppSettingsUntilChangedAndValidatesRanges()
    {
        var blob = new InMemoryBlob();
        var store = new BlobOperationScheduleStore(
            blob.Client.Object,
            Microsoft.Extensions.Options.Options.Create(new TaggingOptions { IntervalMinutes = 15 }),
            Microsoft.Extensions.Options.Options.Create(new PasswordRotationOptions { RotateAfterDays = 25, SecretExpiryDays = 35 }));

        var initial = await store.GetAsync(CancellationToken.None);
        Assert.Equal((15, 25, 29, "appSetting"), (initial.TaggingIntervalMinutes, initial.PasswordRotationDays, initial.MaximumPasswordRotationDays, initial.Source));

        await Assert.ThrowsAsync<ValidationException>(() => store.ReplaceAsync(4, 25, null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => store.ReplaceAsync(15, 30, null, CancellationToken.None));
        var changed = await store.ReplaceAsync(5, 7, null, CancellationToken.None);

        Assert.Equal((5, 7, "blob"), (changed.TaggingIntervalMinutes, changed.PasswordRotationDays, changed.Source));
        var reread = await store.GetAsync(CancellationToken.None);
        Assert.Equal((5, 7), (reread.TaggingIntervalMinutes, reread.PasswordRotationDays));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(10, false)]
    [InlineData(14, true)]
    [InlineData(30, true)]
    public async Task TaggingTimer_RunsOnlyWhenIntervalElapsed(int? minutesSinceLastStart, bool expected)
    {
        var now = new DateTimeOffset(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);
        var statusStore = new Mock<IOperationStatusStore>();
        statusStore.Setup(value => value.GetAsync(OperationNames.DeviceTagging, It.IsAny<CancellationToken>()))
            .ReturnsAsync(minutesSinceLastStart is null ? null : new OperationRunStatus(
                OperationNames.DeviceTagging, "id", OperationRunState.Succeeded,
                now.AddMinutes(-minutesSinceLastStart.Value), now, null, null, null, null));
        var schedule = new Mock<IOperationScheduleStore>();
        schedule.Setup(value => value.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationScheduleSnapshot(15, 25, 29, null, null, "appSetting"));
        var time = new Mock<TimeProvider>();
        time.Setup(value => value.GetUtcNow()).Returns(now);
        var function = new DeviceTaggerFunction(
            null!, Mock.Of<IRunTraceStore>(), statusStore.Object, Mock.Of<IOperationLock>(),
            NullLogger<DeviceTaggerFunction>.Instance, null, schedule.Object, time.Object);

        Assert.Equal(expected, await function.IsDueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PortalFiles_AreEmbeddedAndServedWithStrictSecurityHeaders()
    {
        var function = new PortalFunction(Microsoft.Extensions.Options.Options.Create(new ManagementApiOptions
        {
            ClientId = "11111111-1111-1111-1111-111111111111",
            TenantId = "22222222-2222-2222-2222-222222222222"
        }));

        foreach (var file in new[] { null, "app.js", "app.css" })
        {
            var request = new DefaultHttpContext().Request;
            var result = Assert.IsType<FileStreamResult>(function.GetFile(request, file));
            using var reader = new StreamReader(result.FileStream);
            Assert.NotEmpty(await reader.ReadToEndAsync());
            var policy = request.HttpContext.Response.Headers.ContentSecurityPolicy.ToString();
            Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
            Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
            Assert.Contains("https://login.microsoftonline.com", policy, StringComparison.Ordinal);
        }

        var configRequest = new DefaultHttpContext().Request;
        var config = Assert.IsType<OkObjectResult>(function.GetFile(configRequest, "config.json"));
        Assert.Contains("api://11111111-1111-1111-1111-111111111111/access_as_user", JsonSerializer.Serialize(config.Value), StringComparison.Ordinal);
        Assert.IsType<NotFoundResult>(function.GetFile(new DefaultHttpContext().Request, "../host.json"));
    }

    private static BlobTrustedCertificateStore CreateTrustStore(InMemoryBlob blob, string? packagePath) =>
        new(blob.Client.Object,
            Microsoft.Extensions.Options.Options.Create(new ActiveDirectoryOptions { TrustedCertificatePath = packagePath }),
            TimeProvider.System);

    private static ActiveDirectoryAccountService CreateAccountService(
        Mock<IActiveDirectoryCredentialStore> credentials,
        Mock<IActiveDirectoryConfigurationStore> configuration,
        bool bindSucceeds,
        bool lockAvailable,
        string? failingBindName = null)
    {
        var passwordClient = new Mock<IActiveDirectoryPasswordClient>();
        passwordClient.Setup(value => value.ValidateAccessAsync(It.IsAny<StoredDirectoryCredential>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoredDirectoryCredential credential, CancellationToken _) =>
            {
                var succeeds = bindSucceeds && credential.Username != failingBindName;
                return new ActiveDirectoryAccessValidationResult(succeeds, !succeeds, succeeds ? null : "Invalid Credentials", []);
            });
        var operationLock = new Mock<IOperationLock>();
        operationLock.Setup(value => value.TryAcquireAsync(OperationNames.PasswordRotation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockAvailable ? Mock.Of<IOperationLease>() : null);
        return new ActiveDirectoryAccountService(
            credentials.Object,
            passwordClient.Object,
            configuration.Object,
            operationLock.Object,
            NullLogger<ActiveDirectoryAccountService>.Instance);
    }

    private static PortalApiFunction CreatePortalApi(
        IManagementApiAuthorizer authorizer, IActivityLog activityLog, ITrustedCertificateStore trustStore) =>
        new(authorizer,
            activityLog,
            Mock.Of<IActivityLogReader>(),
            trustStore,
            Mock.Of<IActiveDirectoryAccountService>(),
            Mock.Of<IActiveDirectoryConfigurationStore>(),
            Microsoft.Extensions.Options.Options.Create(new ActiveDirectoryOptions()),
            Mock.Of<IEntraDirectorySearch>(),
            Mock.Of<IOperationScheduleStore>());

    private static HttpRequest AuthenticatedRequest(params string[] roles)
    {
        var request = new DefaultHttpContext().Request;
        var claims = new List<object>
        {
            new { typ = "oid", val = "33333333-3333-3333-3333-333333333333" },
            new { typ = "preferred_username", val = "admin@contoso.com" }
        };
        claims.AddRange(roles.Select(role => (object)new { typ = "roles", val = role }));
        request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { auth_typ = "aad", claims })));
        return request;
    }

    private static X509Certificate2 CreateCertificate(string subject, bool ca, params string[] dnsNames)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
        if (!ca)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));
            var names = new SubjectAlternativeNameBuilder();
            foreach (var name in dnsNames)
            {
                names.AddDnsName(name);
            }
            request.CertificateExtensions.Add(names.Build());
        }
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(certificate.Export(X509ContentType.Cert));
    }

    private sealed class InMemoryBlob
    {
        private int _version;

        public InMemoryBlob()
        {
            Client.Setup(value => value.DownloadContentAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Content is null
                    ? throw new RequestFailedException(404, "Not found")
                    : Response.FromValue(
                        BlobsModelFactory.BlobDownloadResult(
                            BinaryData.FromString(Content),
                            BlobsModelFactory.BlobDownloadDetails(eTag: new ETag($"\"{_version}\""))),
                        Mock.Of<Response>()));
            Client.Setup(value => value.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BinaryData data, BlobUploadOptions options, CancellationToken _) =>
                {
                    var current = new ETag($"\"{_version}\"");
                    if ((Content is null && options.Conditions.IfMatch is not null) ||
                        (Content is not null && options.Conditions.IfMatch != current))
                    {
                        throw new RequestFailedException(412, "Precondition failed");
                    }
                    Content = data.ToString();
                    _version++;
                    return Response.FromValue(
                        BlobsModelFactory.BlobContentInfo(new ETag($"\"{_version}\""), DateTimeOffset.UtcNow, null, null, null, null, 0),
                        Mock.Of<Response>());
                });
        }

        public Mock<BlobClient> Client { get; } = new();

        public string? Content { get; private set; }
    }

    private sealed class RecordingActivityLog : IActivityLog
    {
        public List<ActivityEntry> Entries { get; } = [];

        public void Record(ActivityEntry entry) => Entries.Add(entry);
    }

    private sealed class StubCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
