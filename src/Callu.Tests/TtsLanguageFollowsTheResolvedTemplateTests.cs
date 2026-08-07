using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Resolution reports the language it actually produced, not the one it was asked for.</summary>
// Falling back to another language's template and still claiming the requested language is what sends
// Turkish words to an English voice: the number rules never load and "1" is read "one".
public class TtsLanguageFollowsTheResolvedTemplateTests : IDisposable
{
    private readonly ApplicationDbContext _ctx = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tts-lang-{Guid.NewGuid():N}").Options);

    public void Dispose() => _ctx.Dispose();

    private sealed class PassThroughCache : HybridCache
    {
        public override ValueTask RemoveAsync(string key, CancellationToken ct = default) => ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;

        public override ValueTask SetAsync<T>(
            string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken ct = default) => ValueTask.CompletedTask;

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken ct = default) => factory(state, ct);
    }

    private ITtsTemplateService Service() => new TtsTemplateService(
        new TtsMessageTemplateRepository(_ctx, NullLogger<TtsMessageTemplateRepository>.Instance),
        Substitute.For<ITransactionManager>(),
        new PassThroughCache(),
        NullLogger<TtsTemplateService>.Instance);

    private async Task GivenTemplateAsync(string language, bool isDefault, string messagesJson)
    {
        _ctx.Add(new TtsMessageTemplate
        {
            LanguageCode = language,
            DisplayName = language,
            IsDefault = isDefault,
            MessagesJson = messagesJson
        });
        await _ctx.SaveChangesAsync();
    }

    private const string TurkishPrompt = """{"dtmf_prompt":"Onaylamak için 1'e basın."}""";

    /// <summary>The installation configured one template and marked it default; that is what gets spoken.</summary>
    [Fact]
    public async Task FallingBackToTheDefaultTemplate_ReportsThatTemplatesLanguage()
    {
        await GivenTemplateAsync("tr-TR", isDefault: true, TurkishPrompt);

        var resolved = await Service().ResolveMessagesAsync("en-US");

        Assert.Equal("tr-TR", resolved.LanguageCode);
        Assert.Equal("Onaylamak için 1'e basın.", resolved.Messages["dtmf_prompt"]);
    }

    [Fact]
    public async Task ATemplateForTheRequestedLanguage_KeepsThatLanguage()
    {
        await GivenTemplateAsync("tr-TR", isDefault: true, TurkishPrompt);
        await GivenTemplateAsync("en-US", isDefault: false, """{"dtmf_prompt":"Press 1 to acknowledge."}""");

        var resolved = await Service().ResolveMessagesAsync("en-US");

        Assert.Equal("en-US", resolved.LanguageCode);
        Assert.Equal("Press 1 to acknowledge.", resolved.Messages["dtmf_prompt"]);
    }

    /// <summary>Nothing configured at all: the built-in defaults are the language that was asked for.</summary>
    [Fact]
    public async Task NoTemplateAnywhere_KeepsTheRequestedLanguage()
    {
        var resolved = await Service().ResolveMessagesAsync("tr-TR");

        Assert.Equal("tr-TR", resolved.LanguageCode);
        Assert.NotEmpty(resolved.Messages);
    }

    /// <summary>An unreadable template contributes no text, so it cannot decide the language either.</summary>
    [Fact]
    public async Task ATemplateThatCannotBeParsed_KeepsTheRequestedLanguage()
    {
        await GivenTemplateAsync("tr-TR", isDefault: true, "not json at all");

        var resolved = await Service().ResolveMessagesAsync("en-US");

        Assert.Equal("en-US", resolved.LanguageCode);
    }

    /// <summary>A template whose every message is blank leaves the defaults standing, language included.</summary>
    [Fact]
    public async Task ATemplateWithNothingInIt_KeepsTheRequestedLanguage()
    {
        await GivenTemplateAsync("tr-TR", isDefault: true, """{"dtmf_prompt":"   "}""");

        var resolved = await Service().ResolveMessagesAsync("en-US");

        Assert.Equal("en-US", resolved.LanguageCode);
    }
}
