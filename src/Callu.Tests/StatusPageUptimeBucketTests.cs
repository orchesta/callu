using Callu.Infrastructure.Services;

namespace Callu.Tests;

/// <summary>
/// Uptime windows snap to the five cached buckets, so every request hits a key the invalidation loop
/// actually clears.
/// </summary>
public class StatusPageUptimeBucketTests
{
    [Theory]
    [InlineData(7, 7)]
    [InlineData(14, 14)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(90, 90)]
    [InlineData(15, 14)]
    [InlineData(22, 14)]  // 22 is 8 from 14, 8 from 30 → first-nearest wins (14)
    [InlineData(23, 30)]
    [InlineData(45, 30)]
    [InlineData(46, 60)]
    [InlineData(1, 7)]    // below range clamps up
    [InlineData(365, 90)] // above range clamps down
    public void Snaps_to_nearest_cached_bucket(int requested, int expected)
    {
        Assert.Equal(expected, StatusPageService.SnapToUptimeBucket(requested));
    }
}
