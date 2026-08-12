using Callu.Application.Validators;
using Callu.Domain.Entities;
using Callu.Shared.Models.Services;

namespace Callu.Tests;

public class ServiceAckValidatorTests
{
    private static readonly UpdateServiceRequestValidator Update = new();
    private static readonly CreateServiceRequestValidator Create = new();

    [Fact]
    public void AckUrl_AtTheColumnLimit_IsAccepted()
    {
        var url = "https://x.example/" + new string('a', Service.MaxAckUrlLength - 18);
        Assert.Equal(Service.MaxAckUrlLength, url.Length);

        var result = Update.Validate(new UpdateServiceRequest { AckUrl = url });

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckUrl));
    }

    [Fact]
    public void AckUrl_OverTheColumnLimit_IsRejected_NotLeftToThe500()
    {
        var result = Update.Validate(new UpdateServiceRequest { AckUrl = new string('a', Service.MaxAckUrlLength + 1) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckUrl));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("post")]
    public void AckHttpMethod_FromTheAllowlist_IsAccepted(string method)
    {
        var result = Update.Validate(new UpdateServiceRequest { AckHttpMethod = method });

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckHttpMethod));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    [InlineData("")]
    [InlineData("TRACE")]
    public void AckHttpMethod_OutsideTheAllowlist_IsRejected(string method)
    {
        var result = Update.Validate(new UpdateServiceRequest { AckHttpMethod = method });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckHttpMethod));
    }

    [Fact]
    public void AckContentType_OverTheColumnLimit_IsRejected()
    {
        var result = Update.Validate(new UpdateServiceRequest { AckContentType = new string('a', Service.MaxAckContentTypeLength + 1) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckContentType));
    }

    [Fact]
    public void AckContentType_EmptyString_IsRejected_ADefaultedFieldCannotBeCleared()
    {
        var result = Update.Validate(new UpdateServiceRequest { AckContentType = "" });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckContentType));
    }

    [Fact]
    public void AckHeaders_AValidJsonObject_IsAccepted()
    {
        var result = Update.Validate(new UpdateServiceRequest { AckHeaders = """{"Authorization":"Bearer x"}""" });

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckHeaders));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"a":{"nested":true}}""")]
    public void AckHeaders_ThatDoNotParseToAStringObject_AreRejected_NotSilentlyDroppedAtSendTime(string headers)
    {
        var result = Update.Validate(new UpdateServiceRequest { AckHeaders = headers });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckHeaders));
    }

    [Fact]
    public void AckHeaders_OverTheValidatorCap_AreRejected()
    {
        var headers = $$"""{"a":"{{new string('x', Service.MaxAckHeadersLength)}}"}""";

        var result = Update.Validate(new UpdateServiceRequest { AckHeaders = headers });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckHeaders));
    }

    [Fact]
    public void AckHeaders_EmptyString_IsAccepted_BecauseItClears()
    {
        var result = Update.Validate(new UpdateServiceRequest { AckHeaders = "" });

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckHeaders));
    }

    [Fact]
    public void AckPayloadTemplate_OverTheValidatorCap_IsRejected()
    {
        var result = Update.Validate(new UpdateServiceRequest { AckPayloadTemplate = new string('a', Service.MaxAckPayloadTemplateLength + 1) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckPayloadTemplate));
    }

    [Fact]
    public void APartialPut_WithNoAckFields_IsValid()
    {
        var result = Update.Validate(new UpdateServiceRequest { Name = "renamed" });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NotAStatus")]
    public void AStatusOutsideTheEnum_IsRejected_NotWrittenAsEnumZero(string status)
    {
        var result = Update.Validate(new UpdateServiceRequest { Status = status });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.Status));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Web")]
    public void ATypeOutsideTheEnum_IsRejected(string type)
    {
        var result = Update.Validate(new UpdateServiceRequest { Type = type });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.Type));
    }

    [Fact]
    public void ValidStatusAndType_AreAccepted()
    {
        var result = Update.Validate(new UpdateServiceRequest { Status = "Operational", Type = "Api" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AContentTypeWithParameters_IsRejected()
    {
        var result = Update.Validate(new UpdateServiceRequest { AckContentType = "application/json; charset=utf-8" });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateServiceRequest.AckContentType));
    }

    [Fact]
    public void CreateWithAckConfig_IsAccepted()
    {
        var result = Create.Validate(new CreateServiceRequest
        {
            Name = "svc",
            Type = "Api",
            AckEnabled = true,
            AckUrl = "https://monitoring.example/ack",
            AckHttpMethod = "POST",
            AckHeaders = """{"X-Api-Key":"k"}""",
            AckPayloadTemplate = """{"id": "{{incident.id}}"}""",
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public void CreateWithAMethodOutsideTheAllowlist_IsRejected(string method)
    {
        var result = Create.Validate(new CreateServiceRequest { Name = "svc", Type = "Api", AckHttpMethod = method });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceRequest.AckHttpMethod));
    }

    [Fact]
    public void CreateWithAnOverlongAckUrl_IsRejected()
    {
        var result = Create.Validate(new CreateServiceRequest { Name = "svc", Type = "Api", AckUrl = new string('a', Service.MaxAckUrlLength + 1) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateServiceRequest.AckUrl));
    }
}
