using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Callu.Shared.Models.Auth;

namespace Callu.Tests.Conventions;

/// <summary>MVC refuses a record whose validation attributes sit on properties instead of constructor parameters.</summary>
public class RecordValidationAttributeGuardTests
{
    // ASP.NET throws InvalidOperationException while validating such a type, so every request that
    // binds it answers 500 — unit tests that call the controller directly never see it.
    [Fact]
    public void NoSharedRecord_CarriesValidationAttributesOnItsPositionalProperties()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(RefreshRequest).Assembly.GetTypes()
                     .Where(t => t.IsClass && !t.IsAbstract && IsRecord(t)))
        {
            foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
            {
                var property = type.GetProperty(
                    parameter.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property is null) continue;
                if (property.GetCustomAttributes<ValidationAttribute>(inherit: true).Any())
                    offenders.Add($"{type.Name}.{property.Name}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Validation attributes on these record properties crash MVC model validation with a 500 on "
            + "every request that binds the type. Move the attribute onto the constructor parameter — "
            + "[StringLength(…)] string? X — not [property: …]:\n  "
            + string.Join("\n  ", offenders));
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;
}
