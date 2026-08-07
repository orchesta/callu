using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>The TTS template cache is invalidated after the commit, not inside it.</summary>
public class TtsTemplateCacheInvalidationTests : IDisposable
{
    private readonly ApplicationDbContext _ctx = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tts-{Guid.NewGuid():N}").Options);

    public void Dispose() => _ctx.Dispose();

    /// <summary>Records the order in which the commit and the invalidation happen.</summary>
    private sealed class Journal
    {
        private int _next;

        public int? Committed { get; private set; }

        public int? Invalidated { get; private set; }

        public void RecordCommit() => Committed ??= ++_next;

        public void RecordInvalidation() => Invalidated ??= ++_next;
    }

    private sealed class JournallingCache(Journal journal) : HybridCache
    {
        public List<string> RemovedKeys { get; } = [];

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            RemovedKeys.Add(key);
            journal.RecordInvalidation();
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public override ValueTask SetAsync<T>(
            string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
            => factory(state, cancellationToken);
    }

    private sealed class JournallingTransactionManager(ApplicationDbContext ctx, Journal journal) : ITransactionManager
    {
        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
        {
            var result = await operation();
            await ctx.SaveChangesAsync(cancellationToken);
            journal.RecordCommit();
            return result;
        }

        public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            await operation();
            await ctx.SaveChangesAsync(cancellationToken);
            journal.RecordCommit();
        }

        public bool IsInTransaction() => false;
    }

    private (ITtsTemplateService Service, Journal Journal, JournallingCache Cache) Build()
    {
        var journal = new Journal();
        var cache = new JournallingCache(journal);

        var service = new TtsTemplateService(
            new TtsMessageTemplateRepository(_ctx, NullLogger<TtsMessageTemplateRepository>.Instance),
            new JournallingTransactionManager(_ctx, journal),
            cache,
            NullLogger<TtsTemplateService>.Instance);

        return (service, journal, cache);
    }

    private static TtsTemplateSaveRequest Save(string language) => new()
    {
        LanguageCode = language,
        DisplayName = language,
        IsDefault = false,
        Messages = new Dictionary<string, string> { ["incident_message"] = "Corrected prompt" }
    };

    [Fact]
    public async Task Save_InvalidatesAfterTheCommit()
    {
        var (service, journal, cache) = Build();

        await service.SaveAsync(Save("tr-TR"));

        Assert.Contains("tts-messages:tr-TR", cache.RemovedKeys);
        Assert.NotNull(journal.Committed);
        Assert.NotNull(journal.Invalidated);
        Assert.True(
            journal.Invalidated > journal.Committed,
            $"invalidation ran at step {journal.Invalidated}, commit at step {journal.Committed} — a "
            + "concurrent read in that window repopulates the entry with the pre-commit text");
    }

    [Fact]
    public async Task Delete_InvalidatesAfterTheCommit()
    {
        _ctx.TtsMessageTemplates.Add(new TtsMessageTemplate
        {
            Id = Guid.NewGuid(),
            LanguageCode = "de-DE",
            DisplayName = "Deutsch",
            MessagesJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await _ctx.SaveChangesAsync();

        var (service, journal, cache) = Build();

        await service.DeleteAsync("de-DE");

        Assert.Contains("tts-messages:de-DE", cache.RemovedKeys);
        Assert.True(journal.Invalidated > journal.Committed);
    }

    [Fact]
    public async Task Delete_OfSomethingThatIsNotThere_InvalidatesNothing()
    {
        var (service, _, cache) = Build();

        await service.DeleteAsync("fr-FR");

        Assert.Empty(cache.RemovedKeys);
    }
}
