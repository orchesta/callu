using System.Reflection;
using System.Text.RegularExpressions;
using Callu.Application.Plugins;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services;

namespace Callu.Tests;

/// <summary>Every paging mechanism the product ships must be called, and every channel dispatcher built, by some test.</summary>
public class MechanismCoverageTests
{
    /// <summary>The interfaces on the path between an incident and a ringing phone.</summary>
    private static readonly Type[] PagingCriticalTypes =
    [
        typeof(IEscalationOrchestrator),        // decides WHICH step is paged, and when
        typeof(INotificationDispatcher),        // decides WHO is paged, on which channel, and retries it
        typeof(INotificationChannelDispatcher), // THE THING THAT DIALS THE PHONE (see below)
        typeof(IIncidentEventDispatcher),       // tells the external system the incident was taken
        typeof(IScheduleMaterializer)           // decides who "on call" MEANS at a given instant
    ];

    private static IEnumerable<MethodInfo> PublicMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);   // property accessors are not mechanisms

    /// <summary>The test project's own code — comments and string literals blanked, so a method named only in prose does not count as covered.</summary>
    private static readonly Lazy<string> TestSources = new(() =>
        string.Join("\n", SourceScanner.Files(["Callu.Tests"]).Select(SourceScanner.Code)));

    /// <summary>Matches a call, not a mention: the leading dot separates it from a fake's declaration of the same name.</summary>
    private static bool IsCalledByATest(MethodInfo method) =>
        Regex.IsMatch(TestSources.Value, $@"\.\s*{Regex.Escape(method.Name)}\s*(<[^;()]*>)?\s*\(");

    [Fact]
    public void EveryPagingMechanism_IsCalledBySomeTest()
    {
        var untested = PagingCriticalTypes
            .SelectMany(t => PublicMethods(t).Select(m => (Type: t, Method: m)))
            .Where(x => !IsCalledByATest(x.Method))
            .Select(x => $"{x.Type.Name}.{x.Method.Name}")
            .Order()
            .ToList();

        Assert.True(untested.Count == 0,
            "These public methods page (or decide who pages) a human being, and NOTHING IN THE TEST "
            + "SUITE CALLS THEM: " + string.Join(", ", untested)
            + ".\n\nThat is not a gap in coverage — it is a mechanism whose first execution will be in "
            + "production, during an incident, at 3am. EscalateNowAsync shipped exactly like this and "
            + "rang back the responder who had just said they could not take the call, while 837 tests "
            + "stayed green.\n\nWrite a test that drives it. If it genuinely cannot be driven, say so "
            + "and say why.");
    }

    /// <summary>The detector's control group: a method nobody has ever called must read as uncalled.</summary>
    [Fact]
    public void TheGuard_WouldNotice_AnUncalledMechanism()
    {
        var neverCalled = typeof(INeverImplemented).GetMethods().Single();

        Assert.False(IsCalledByATest(neverCalled),
            "the coverage detector reports a method nobody has ever called as covered — it is not "
            + "detecting anything, and every mechanism it 'guards' is unguarded");
    }

    /// <summary>...and it must not report a method that IS called as uncalled, or it cries wolf until it is deleted.</summary>
    [Fact]
    public void TheGuard_Recognises_ACallItCanSee()
    {
        var called = typeof(IEscalationOrchestrator).GetMethod(nameof(IEscalationOrchestrator.EscalateNowAsync))!;

        Assert.True(IsCalledByATest(called));
    }

    /// <summary>The premise: reflection must actually find methods, or the guard passes over an empty list forever.</summary>
    [Fact]
    public void TheGuardedSurface_IsNotEmpty()
    {
        Assert.All(PagingCriticalTypes, t => Assert.NotEmpty(PublicMethods(t)));

        // The one that started all this had better be in there.
        Assert.Contains(
            PublicMethods(typeof(IEscalationOrchestrator)),
            m => m.Name == nameof(IEscalationOrchestrator.EscalateNowAsync));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Interface coverage is not class coverage: the rule below is about the class that dials the phone.

    /// <summary>Channel-dispatcher class declarations, read from product source so an unreferenced project cannot hide one.</summary>
    private static readonly Regex ChannelDispatcherDeclaration = new(
        @"\b(?<mods>(?:(?:public|internal|protected|private|sealed|abstract|partial|static|file)\s+)+)class\s+(?<name>\w+)"
        + @"[^{;]*?:\s*[^{;]*?\b(?:INotificationChannelDispatcher|PhoneChannelDispatcher)\b",
        RegexOptions.Compiled);

    /// <summary>Every concrete channel dispatcher in the product. Abstract bases are not shipped alone.</summary>
    private static IEnumerable<string> ProductChannelDispatchers() =>
        SourceScanner.ProductFiles(includeMigrations: false)
            .SelectMany(f => ChannelDispatcherDeclaration.Matches(SourceScanner.Code(f)))
            .Where(m => !m.Groups["mods"].Value.Contains("abstract", StringComparison.Ordinal))
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

    /// <summary>Matches a construction, because a class that is never instantiated is a class whose code has never run.</summary>
    private static bool IsBuiltByATest(string typeName) =>
        Regex.IsMatch(TestSources.Value, $@"new\s+{Regex.Escape(typeName)}\s*[({{]");

    [Fact]
    public void EveryChannelDispatcher_IsBuiltBySomeTest()
    {
        var unbuilt = ProductChannelDispatchers()
            .Where(name => !IsBuiltByATest(name))
            .ToList();

        Assert.True(unbuilt.Count == 0,
            "These classes DELIVER THE PAGE — they are what places the call, sends the message, sends the "
            + "mail — and no test in this suite ever constructs one: " + string.Join(", ", unbuilt)
            + ".\n\nA channel dispatcher that is never built is a channel whose 'no provider registered', "
            + "'the provider said no' and 'this incident is already resolved' branches run for the first "
            + "time in production. VoiceCallChannelDispatcher and SmsChannelDispatcher shipped exactly like "
            + "this: the suite drove a test double that marked every row Delivered, so a cold provider "
            + "registry — a window that opens on every Worker start and every registry reload — wrote the "
            + "page off as dead while the escalation step reported the responder as reached.\n\nBuild it and "
            + "drive it. A fake that implements the interface is not this class.");
    }

    /// <summary>The source scan is cross-checked against reflection, so a regex that stopped matching cannot pass.</summary>
    [Fact]
    public void TheGuardedDispatchers_AreTheOnesThatExist()
    {
        var found = ProductChannelDispatchers().ToList();

        Assert.NotEmpty(found);

        // Reflection sees the referenced product assemblies. Everything IT can see, the source scan must
        // have seen too (the scan also covers projects this assembly does not reference, so it may see
        // more — never less).
        var reflected = new[] { typeof(INotificationChannelDispatcher).Assembly, typeof(EmailChannelDispatcher).Assembly }
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(INotificationChannelDispatcher).IsAssignableFrom(t))
            .Select(t => t.Name);

        foreach (var name in reflected)
            Assert.Contains(name, found, StringComparer.Ordinal);

        // The three that page a human, named out loud: this list going quiet is itself the bug.
        Assert.Contains(nameof(VoiceCallChannelDispatcher), found, StringComparer.Ordinal);
        Assert.Contains(nameof(SmsChannelDispatcher), found, StringComparer.Ordinal);
        Assert.Contains(nameof(EmailChannelDispatcher), found, StringComparer.Ordinal);

        // ...and the abstract base is NOT one of them: it ships nothing on its own, and demanding a
        // `new PhoneChannelDispatcher(...)` would be a demand no test can meet.
        Assert.DoesNotContain(nameof(PhoneChannelDispatcher), found, StringComparer.Ordinal);
    }

    /// <summary>The build detector's control group: a dispatcher nobody constructs must read as unbuilt.</summary>
    [Fact]
    public void TheGuard_WouldNotice_ADispatcherNobodyBuilds()
    {
        Assert.False(IsBuiltByATest(nameof(ADispatcherNoTestEverBuilds)),
            "the build detector reports a class nobody has ever constructed as built — it is not detecting "
            + "anything, and every channel dispatcher it 'guards' is unguarded");

        // ...and it must not cry wolf over one that IS built, or it gets weakened until it is useless.
        Assert.True(IsBuiltByATest(nameof(VoiceCallChannelDispatcher)));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Push sits off to the side of the dispatcher registry, so both guards above are blind to it.

    /// <summary>Push is not a paging channel, and the three that can reach a human away from a screen are.</summary>
    [Fact]
    public void PushIsNotAPagingChannel_AndTheChannelsThatRingAHumanAre()
    {
        Assert.False(Notification.ChannelCanPage(NotificationType.Push),
            "A push is a toast in a browser that is already open. It cannot wake anybody, and SignalR "
            + "reports a send to a hub group with ZERO connected clients as a SUCCESS — so it cannot even "
            + "tell you it reached nobody. Counting it as a page is how an escalation step reported a "
            + "responder as reached while their phone never rang.");

        Assert.True(Notification.ChannelCanPage(NotificationType.VoiceCall));
        Assert.True(Notification.ChannelCanPage(NotificationType.Sms));
        Assert.True(Notification.ChannelCanPage(NotificationType.Email));
    }

    /// <summary>The paging classification is an allowlist, so a screen or machine channel cannot drift into it.</summary>
    [Theory]
    [InlineData(NotificationType.Push)]
    [InlineData(NotificationType.Slack)]
    [InlineData(NotificationType.MsTeams)]
    [InlineData(NotificationType.Webhook)]
    public void ScreenAndMachineChannels_DoNotCountAsPagingAHuman(NotificationType type)
    {
        Assert.False(Notification.ChannelCanPage(type),
            $"{type} was allowed to declare a human reached. Nothing here can wake somebody who is "
            + "asleep, and none of them carry a delivery receipt from a PERSON. If this channel really "
            + "does ring a phone, argue it into ChannelCanPage on purpose — do not let it drift in.");
    }

    /// <summary>The push path must be driven with a constructed double that behaves like SignalR, not an interface stub.</summary>
    [Fact]
    public void ThePushPath_IsDrivenBySomeTest_WithARealDouble()
    {
        Assert.True(IsBuiltByATest(nameof(RecordingPushService)),
            "No test in this suite hands the notification funnel a working push service. That is how push "
            + "came to count as a page: EVERY test of the reached count ran with push DISABLED — which "
            + "production never is, the default is on — so the branch that counted a browser toast as a "
            + "paged human was never once executed by a test, and 1000+ of them stayed green over it.");
    }

    /// <summary>Push is on by default, which is why it may not count towards the reached total.</summary>
    [Fact]
    public void PushIsOnByDefault_WhichIsWhyItMayNotCount()
    {
        Assert.True(new NotificationPreference().PushEnabled,
            "push is no longer on by default — if that is deliberate, the tests that lean on the default "
            + "being ON (see RecordingPushService) need re-reading");

        Assert.False(Notification.ChannelCanPage(NotificationType.Push));
    }

    /// <summary>A method nobody has ever called, ever, by construction. The detector's control group.</summary>
    private interface INeverImplemented
    {
        void AMechanismNoTestHasEverInvoked();
    }

    /// <summary>A class whose name appears in this project only as an identifier, never after a <c>new</c>.</summary>
    private sealed class ADispatcherNoTestEverBuilds;
}
