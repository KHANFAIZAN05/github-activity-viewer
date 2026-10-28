using GitHubActivityDashboard.Api.Services;

namespace GitHubActivityDashboard.Tests;

public sealed class ActivityYearRangeTests
{
    [Fact]
    public void StartsAtAccountCreationAndIncludesCurrentYear()
    {
        var years = ActivityYearRange.GetYears(
            new DateTimeOffset(2017, 9, 8, 0, 0, 0, TimeSpan.Zero),
            2025);

        Assert.Equal(Enumerable.Range(2017, 9), years);
    }

    [Fact]
    public void DoesNotRequestContributionDataBeforeGitHubStarted()
    {
        var years = ActivityYearRange.GetYears(
            new DateTimeOffset(2004, 1, 1, 0, 0, 0, TimeSpan.Zero),
            2010);

        Assert.Equal(Enumerable.Range(2008, 3), years);
    }

    [Fact]
    public void ReturnsNoYearsWhenAccountCreationIsAfterCurrentYear()
    {
        var years = ActivityYearRange.GetYears(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            2025);

        Assert.Empty(years);
    }
}
