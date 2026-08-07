using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Tests;

/// <summary>
/// Postmortem one-way lifecycle: Draft → InReview → Published → Locked. Transitions are
/// domain-enforced (controller maps the throw to 409). Locked is terminal/immutable.
/// </summary>
public class PostmortemTests
{
    private static Postmortem InStatus(PostmortemStatus status) => new() { Status = status };

    [Fact]
    public void New_DefaultsToDraft() =>
        Assert.Equal(PostmortemStatus.Draft, new Postmortem().Status);

    [Fact]
    public void Submit_DraftToInReview()
    {
        var p = InStatus(PostmortemStatus.Draft);
        p.Submit();
        Assert.Equal(PostmortemStatus.InReview, p.Status);
    }

    [Theory]
    [InlineData(PostmortemStatus.InReview)]
    [InlineData(PostmortemStatus.Published)]
    [InlineData(PostmortemStatus.Locked)]
    public void Submit_FromNonDraft_Throws(PostmortemStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).Submit());

    [Fact]
    public void Reject_InReviewToDraft()
    {
        var p = InStatus(PostmortemStatus.InReview);
        p.Reject();
        Assert.Equal(PostmortemStatus.Draft, p.Status);
    }

    [Theory]
    [InlineData(PostmortemStatus.Draft)]
    [InlineData(PostmortemStatus.Published)]
    [InlineData(PostmortemStatus.Locked)]
    public void Reject_FromNonInReview_Throws(PostmortemStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).Reject());

    [Theory]
    [InlineData(PostmortemStatus.Draft)]
    [InlineData(PostmortemStatus.InReview)]
    public void Publish_FromDraftOrInReview_StampsPublishedAt(PostmortemStatus status)
    {
        var p = InStatus(status);
        p.Publish();
        Assert.Equal(PostmortemStatus.Published, p.Status);
        Assert.NotNull(p.PublishedAt);
    }

    [Theory]
    [InlineData(PostmortemStatus.Published)]
    [InlineData(PostmortemStatus.Locked)]
    public void Publish_FromPublishedOrLocked_Throws(PostmortemStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).Publish());

    [Fact]
    public void Lock_PublishedToLocked_FreezesEditing()
    {
        var p = InStatus(PostmortemStatus.Published);
        p.Lock();
        Assert.Equal(PostmortemStatus.Locked, p.Status);
        Assert.False(p.CanEditAllFields);
    }

    [Theory]
    [InlineData(PostmortemStatus.Draft)]
    [InlineData(PostmortemStatus.InReview)]
    [InlineData(PostmortemStatus.Locked)]
    public void Lock_FromNonPublished_Throws(PostmortemStatus status) =>
        Assert.Throws<InvalidOperationException>(() => InStatus(status).Lock());

    [Theory]
    [InlineData(PostmortemStatus.Draft, true)]
    [InlineData(PostmortemStatus.InReview, true)]
    [InlineData(PostmortemStatus.Published, false)]
    [InlineData(PostmortemStatus.Locked, false)]
    public void CanEditAllFields_OnlyDraftAndInReview(PostmortemStatus status, bool expected) =>
        Assert.Equal(expected, InStatus(status).CanEditAllFields);

    [Fact]
    public void FullHappyPath_DraftToLocked()
    {
        var p = new Postmortem();
        p.Submit();
        p.Publish();
        p.Lock();
        Assert.Equal(PostmortemStatus.Locked, p.Status);
        Assert.NotNull(p.PublishedAt);
    }
}
