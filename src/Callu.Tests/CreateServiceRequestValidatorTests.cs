using Callu.Application.Validators;
using Callu.Domain.Enums;
using Callu.Shared.Models.Services;

namespace Callu.Tests;

public class CreateServiceRequestValidatorTests
{
    private static readonly CreateServiceRequestValidator Validator = new();

    private static CreateServiceRequest Request(string? name = "svc", string? type = "Api", string? description = null) => new()
    {
        Name = name ?? string.Empty,
        Type = type ?? string.Empty,
        Description = description,
    };

    [Fact]
    public void Name_AtTheColumnLimit_IsAccepted()
    {
        var result = Validator.Validate(Request(name: new string('a', 100)));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateServiceRequest.Name));
    }

    [Fact]
    public void Name_OverTheColumnLimit_IsRejected_NotLeftToThe500()
    {
        var result = Validator.Validate(Request(name: new string('a', 101)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceRequest.Name));
    }

    [Fact]
    public void Description_OverTheColumnLimit_IsRejected()
    {
        var result = Validator.Validate(Request(description: new string('a', 501)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceRequest.Description));
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Website")]
    [InlineData("Cdn")]
    [InlineData("Storage")]
    [InlineData("Email")]
    [InlineData("ThirdParty")]
    [InlineData("Server")]
    public void EveryServiceTypeEnumName_IsAccepted(string type)
    {
        var result = Validator.Validate(Request(type: type));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateServiceRequest.Type));
    }

    [Fact]
    public void AllEnumNames_AreAccepted()
    {
        foreach (var name in Enum.GetNames<ServiceType>())
        {
            var result = Validator.Validate(Request(type: name));
            Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateServiceRequest.Type));
        }
    }

    [Fact]
    public void TheOldPhantom_Web_IsRejected_BecauseTheEnumHasWebsite()
    {
        var result = Validator.Validate(Request(type: "Web"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceRequest.Type));
    }
}
