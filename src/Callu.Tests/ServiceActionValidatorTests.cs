using Callu.Application.Validators;
using Callu.Domain.Entities;
using Callu.Shared.Models.Services;

namespace Callu.Tests;

public class ServiceActionValidatorTests
{
    private static readonly CreateServiceActionRequestValidator Create = new();
    private static readonly UpdateServiceActionRequestValidator Update = new();

    private static CreateServiceActionRequest Valid() => new()
    {
        Name = "Restart Redis",
        Url = "https://ops.example/restart",
    };

    [Fact]
    public void AValidAction_Passes()
    {
        Assert.True(Create.Validate(Valid()).IsValid);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("get")]
    public void ManualActions_MayUseGet_UnlikeTheAckCallback(string method)
    {
        var result = Create.Validate(Valid() with { HttpMethod = method });

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateServiceActionRequest.HttpMethod));
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("TRACE")]
    [InlineData("")]
    public void MethodsOutsideTheAllowlist_AreRejected(string method)
    {
        var result = Create.Validate(Valid() with { HttpMethod = method });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceActionRequest.HttpMethod));
    }

    [Fact]
    public void AMissingName_IsRejected()
    {
        var result = Create.Validate(Valid() with { Name = "" });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceActionRequest.Name));
    }

    [Fact]
    public void AnOverlongName_IsRejected()
    {
        var result = Create.Validate(Valid() with { Name = new string('a', ServiceAction.MaxNameLength + 1) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceActionRequest.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://x.example/run")]
    public void ANonHttpUrl_IsRejected(string url)
    {
        var result = Create.Validate(Valid() with { Url = url });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceActionRequest.Url));
    }

    [Fact]
    public void AnOverlongUrl_IsRejected_NotLeftToThe500()
    {
        var url = "https://x.example/" + new string('a', ServiceAction.MaxUrlLength);

        var result = Create.Validate(Valid() with { Url = url });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceActionRequest.Url));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    public void HeadersThatDoNotParse_AreRejected(string headers)
    {
        var result = Create.Validate(Valid() with { HeadersJson = headers });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceActionRequest.HeadersJson));
    }

    [Fact]
    public void SecretAndSignatureHeader_AreBoundedAtTheColumn()
    {
        var result = Create.Validate(Valid() with
        {
            Secret = new string('s', ServiceAction.MaxSecretLength + 1),
            SignatureHeader = new string('h', ServiceAction.MaxSignatureHeaderLength + 1),
        });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceActionRequest.Secret));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceActionRequest.SignatureHeader));
    }

    [Fact]
    public void APartialUpdate_WithNoFields_IsValid()
    {
        Assert.True(Update.Validate(new UpdateServiceActionRequest()).IsValid);
    }

    [Fact]
    public void AnUpdateWithABadUrl_IsRejected()
    {
        var result = Update.Validate(new UpdateServiceActionRequest { Url = "not a url" });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceActionRequest.Url));
    }
}
