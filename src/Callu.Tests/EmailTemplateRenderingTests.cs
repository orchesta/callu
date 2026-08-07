using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Email;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>
/// The variable renderer's escaping contract and the template resolver's DB-first / file-fallback
/// semantics, including cache invalidation.
/// </summary>
public class EmailTemplateRenderingTests
{
    // ---- TemplateVariableRenderer ----

    [Fact]
    public void ReplaceVariables_SubstitutesAndHtmlEncodes()
    {
        var html = TemplateVariableRenderer.ReplaceVariables(
            "<p>{{Title}}</p>", new Dictionary<string, string> { ["Title"] = "<img src=x onerror=alert(1)>" });

        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void ReplaceVariables_RawSuffix_BypassesEscaping()
    {
        var html = TemplateVariableRenderer.ReplaceVariables(
            "{{button_raw}}", new Dictionary<string, string> { ["button_raw"] = "<a href=\"x\">go</a>" });

        Assert.Equal("<a href=\"x\">go</a>", html);
    }

    [Fact]
    public void ReplaceVariables_UnknownToken_StaysIntact()
    {
        var html = TemplateVariableRenderer.ReplaceVariables(
            "{{Known}} {{Unknown}}", new Dictionary<string, string> { ["Known"] = "v" });

        Assert.Equal("v {{Unknown}}", html);
    }

    [Fact]
    public void ExtractVariables_ReturnsDistinctNames()
    {
        var vars = TemplateVariableRenderer.ExtractVariables("{{A}} {{B}} {{A}}");
        Assert.Equal(new[] { "A", "B" }, vars);
    }

    [Fact]
    public void SeedTemplates_CoverAllLivePipelineKeys_WithTokensIntact()
    {
        var seeds = EmailTemplates.GetSeedTemplates();
        var keys = seeds.Select(s => s.Key).ToArray();

        Assert.Equal(
            new[] { "invitation", "password_reset", "oncall_notification", "conference_invite", "status_page_subscription_confirmation" },
            keys);
        Assert.All(seeds, s => Assert.NotEmpty(TemplateVariableRenderer.ExtractVariables(s.HtmlBody + s.Subject)));
        // Canonical tokens survive the base-wrap (PascalCase, replaceable at send time).
        Assert.Contains("{{ResetLink}}", seeds.First(s => s.Key == "password_reset").HtmlBody);
        Assert.Contains("{{ConferenceUrl}}", seeds.First(s => s.Key == "conference_invite").HtmlBody);
    }

    // ---- DbEmailTemplateResolver ----

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"b4-{Guid.NewGuid():N}").Options);

    private static HybridCache NewCache() =>
        new ServiceCollection().AddHybridCache().Services
            .BuildServiceProvider().GetRequiredService<HybridCache>();

    private static DbEmailTemplateResolver Resolver(ApplicationDbContext ctx, HybridCache cache) =>
        new(new EmailTemplateRepository(ctx, NullLogger<EmailTemplateRepository>.Instance),
            cache, NullLogger<DbEmailTemplateResolver>.Instance);

    private static EmailTemplate Row(string key, bool active = true, string subject = "S {{Name}}", string body = "B {{Name}}") => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        Subject = subject,
        HtmlBody = body,
        IsSystem = true,
        IsActive = active,
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task NoRow_ReturnsNull_SoFileFallbackApplies()
    {
        using var ctx = NewContext();
        var result = await Resolver(ctx, NewCache()).TryRenderAsync("invitation", new Dictionary<string, string>());
        Assert.Null(result);
    }

    [Fact]
    public async Task InactiveRow_ReturnsNull()
    {
        using var ctx = NewContext();
        ctx.Add(Row("invitation", active: false));
        await ctx.SaveChangesAsync();

        Assert.Null(await Resolver(ctx, NewCache()).TryRenderAsync("invitation", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task ActiveRow_RendersSubjectAndBody()
    {
        using var ctx = NewContext();
        ctx.Add(Row("invitation"));
        await ctx.SaveChangesAsync();

        var result = await Resolver(ctx, NewCache()).TryRenderAsync(
            "invitation", new Dictionary<string, string> { ["Name"] = "Ada" });

        Assert.NotNull(result);
        Assert.Equal("S Ada", result!.Subject);
        Assert.Equal("B Ada", result.HtmlBody);
    }

    [Fact]
    public async Task Invalidate_DropsCachedCopy_SoEditsApplyImmediately()
    {
        using var ctx = NewContext();
        var cache = NewCache();
        var resolver = Resolver(ctx, cache);
        var row = Row("invitation", subject: "old", body: "old");
        ctx.Add(row);
        await ctx.SaveChangesAsync();

        var first = await resolver.TryRenderAsync("invitation", new Dictionary<string, string>());
        Assert.Equal("old", first!.Subject);

        row.Subject = "new";
        row.HtmlBody = "new";
        await ctx.SaveChangesAsync();

        // Still cached...
        var cached = await resolver.TryRenderAsync("invitation", new Dictionary<string, string>());
        Assert.Equal("old", cached!.Subject);

        // ...until invalidated (what EmailTemplateService does post-commit).
        await resolver.InvalidateAsync("invitation");
        var fresh = await resolver.TryRenderAsync("invitation", new Dictionary<string, string>());
        Assert.Equal("new", fresh!.Subject);
    }
}
