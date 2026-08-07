using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Push;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

public class FirebaseSettingsServiceTests
{
    private readonly IFirebaseSettingsRepository _repo = Substitute.For<IFirebaseSettingsRepository>();
    private readonly ITransactionManager _tx = Substitute.For<ITransactionManager>();
    private readonly HybridCache _cache;

    public FirebaseSettingsServiceTests()
    {
        _tx.ExecuteInTransactionAsync(Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<bool>>>()());
        _cache = new ServiceCollection().AddHybridCache().Services
            .BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private FirebaseSettingsService Sut()
    {
        var protector = new FirebaseCredentialProtector(
            new EphemeralDataProtectionProvider(),
            NullLogger<FirebaseCredentialProtector>.Instance);
        var access = new FcmAccessTokenProvider(
            Substitute.For<IHttpClientFactory>(),
            NullLogger<FcmAccessTokenProvider>.Instance);
        return new FirebaseSettingsService(
            _repo, _tx, _cache, protector, access, NullLogger<FirebaseSettingsService>.Instance);
    }

    [Fact]
    public void TryParseServiceAccount_RequiresProjectClientKey()
    {
        Assert.False(FirebaseSettingsService.TryParseServiceAccount("{}", out _, out var err));
        Assert.Contains("project_id", err);

        var ok = """{"project_id":"demo-project","client_email":"a@b.com","private_key":"k"}""";
        Assert.True(FirebaseSettingsService.TryParseServiceAccount(ok, out var pid, out _));
        Assert.Equal("demo-project", pid);
    }

    // The project id is interpolated into the FCM URL path.
    [Theory]
    [InlineData("../../v1/projects/other")]
    [InlineData("demo project")]
    [InlineData("Demo")]
    [InlineData("a")]
    public void TryParseServiceAccount_RejectsAProjectIdThatIsNotAProjectId(string projectId)
    {
        var json = $$"""{"project_id":"{{projectId}}","client_email":"a@b.com","private_key":"k"}""";

        Assert.False(FirebaseSettingsService.TryParseServiceAccount(json, out var pid, out var err));
        Assert.Equal("", pid);
        Assert.Contains("project_id", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_DropsTheCachedSettingsSoTheNextReadSeesTheNewRow()
    {
        var stored = new FirebaseSettings { Id = FirebaseSettings.SingletonId, ProjectId = "before" };
        _repo.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(stored);
        var sut = Sut();

        Assert.Equal("before", (await sut.GetSettingsAsync()).ProjectId);

        await sut.SaveSettingsAsync(new UpdateFirebaseSettingsRequest { ProjectId = "after" });

        Assert.Equal("after", (await sut.GetSettingsAsync()).ProjectId);
    }

    [Fact]
    public async Task Save_ProtectsServiceAccountAndSetsConfigured()
    {
        _repo.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns((FirebaseSettings?)null);
        FirebaseSettings? saved = null;
        await _repo.AddAsync(Arg.Do<FirebaseSettings>(s => saved = s), Arg.Any<CancellationToken>());

        var json =
            """{"project_id":"demo","client_email":"svc@demo.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----\n"}""";
        var ok = await Sut().SaveSettingsAsync(new UpdateFirebaseSettingsRequest
        {
            ServiceAccountJson = json
        });

        Assert.True(ok);
        Assert.NotNull(saved);
        Assert.Equal(FirebaseSettings.SingletonId, saved!.Id);
        Assert.Equal("demo", saved.ProjectId);
        Assert.True(saved.IsConfigured);
        Assert.NotEqual(json, saved.ServiceAccountJson);
        Assert.False(string.IsNullOrEmpty(saved.ServiceAccountJson));
    }

    [Fact]
    public async Task Save_InvalidJson_Throws()
    {
        _repo.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns((FirebaseSettings?)null);

        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveSettingsAsync(
            new UpdateFirebaseSettingsRequest { ServiceAccountJson = "{not-json" }));
    }
}
