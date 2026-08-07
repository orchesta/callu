using Callu.Application.Services;
using Callu.Domain.Enums;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Two changes that decide who a page reaches, and who is allowed to make them.</summary>
// Neither was recorded: an override could reroute every page for a week and a role could be
// escalated to Admin without either leaving a trace.
public class OverrideAndRoleAuditTests
{
    private static readonly string[] AuditWritingSources =
    {
        Path.Combine("Callu.Infrastructure", "Services", "OnCallOverrideService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "UserManagementService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "AuthService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "EscalationService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "ScheduleService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "OrganizationSettingsService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "IncidentNoteService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "ProfileService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "TeamService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "MaintenanceWindowService.cs"),
        Path.Combine("Callu.Infrastructure", "Services", "WebhookConfigService.cs"),
    };

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CalluApp.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null, "solution root not found");
        return dir!.FullName;
    }

    private static string Read(string relative)
    {
        var path = Path.Combine(SolutionRoot(), relative);
        Assert.True(File.Exists(path), $"missing: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void TheScanSeesTheServices()
    {
        foreach (var file in AuditWritingSources)
            Assert.NotEmpty(Read(file));
    }

    [Theory]
    [InlineData("EscalationService.cs", "Created")]
    [InlineData("EscalationService.cs", "Updated")]
    [InlineData("EscalationService.cs", "Deleted")]
    [InlineData("ScheduleService.cs", "Updated")]
    [InlineData("OrganizationSettingsService.cs", "SettingsChanged")]
    [InlineData("IncidentNoteService.cs", "Updated")]
    [InlineData("IncidentNoteService.cs", "Deleted")]
    [InlineData("OnCallOverrideService.cs", "OverrideCreated")]
    [InlineData("OnCallOverrideService.cs", "OverrideCancelled")]
    [InlineData("UserManagementService.cs", "RoleAssigned")]
    [InlineData("UserManagementService.cs", "Created")]
    [InlineData("UserManagementService.cs", "Deleted")]
    [InlineData("ProfileService.cs", "PasswordChanged")]
    [InlineData("TeamService.cs", "Created")]
    [InlineData("TeamService.cs", "Updated")]
    [InlineData("TeamService.cs", "Deleted")]
    [InlineData("MaintenanceWindowService.cs", "Created")]
    [InlineData("MaintenanceWindowService.cs", "Updated")]
    [InlineData("MaintenanceWindowService.cs", "Deleted")]
    [InlineData("WebhookConfigService.cs", "SettingsChanged")]
    [InlineData("AuthService.cs", "Login")]
    [InlineData("AuthService.cs", "Logout")]
    [InlineData("AuthService.cs", "LoginFailed")]
    public void TheServiceWritesTheAction(string file, string action)
    {
        var source = Read(Path.Combine("Callu.Infrastructure", "Services", file));

        Assert.Contains($"AuditAction.{action}", source, StringComparison.Ordinal);
    }

    /// <summary>Every service that logs an audit row takes the service that writes it.</summary>
    [Theory]
    [InlineData("OnCallOverrideService.cs")]
    [InlineData("UserManagementService.cs")]
    [InlineData("AuthService.cs")]
    [InlineData("EscalationService.cs")]
    [InlineData("ScheduleService.cs")]
    [InlineData("OrganizationSettingsService.cs")]
    [InlineData("IncidentNoteService.cs")]
    [InlineData("ProfileService.cs")]
    [InlineData("TeamService.cs")]
    [InlineData("MaintenanceWindowService.cs")]
    [InlineData("WebhookConfigService.cs")]
    public void TheServiceTakesTheAuditWriter(string file)
    {
        var source = Read(Path.Combine("Callu.Infrastructure", "Services", file));

        Assert.Contains("IAuditLogService", source, StringComparison.Ordinal);
    }

    /// <summary>A sign-in must not be taken down by the audit table, so that one call is guarded.</summary>
    [Fact]
    public void TheSignInAuditWriteIsGuarded()
    {
        var source = Read(Path.Combine("Callu.Infrastructure", "Services", "AuthService.cs"));

        Assert.Contains("TryAuditAsync", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
    }

    /// <summary>Every action the enum offers should be reachable, or it is a promise nothing keeps.</summary>
    [Fact]
    public void TheAuthActionsAreNoLongerDeadEnumMembers()
    {
        var everything = string.Join('\n', AuditWritingSources.Select(Read));

        foreach (var action in new[] { AuditAction.Login, AuditAction.Logout, AuditAction.LoginFailed,
                                       AuditAction.RoleAssigned, AuditAction.PasswordChanged,
                                       AuditAction.OverrideCreated, AuditAction.OverrideCancelled,
                                       AuditAction.SettingsChanged })
            Assert.Contains($"AuditAction.{action}", everything, StringComparison.Ordinal);
    }
}
