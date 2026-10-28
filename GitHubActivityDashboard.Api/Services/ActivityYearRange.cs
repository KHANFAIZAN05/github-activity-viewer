namespace GitHubActivityDashboard.Api.Services;

public static class ActivityYearRange
{
    public static IReadOnlyList<int> GetYears(DateTimeOffset accountCreatedAt, int currentYear)
    {
        var firstYear = Math.Max(2008, accountCreatedAt.Year);
        return firstYear > currentYear
            ? Array.Empty<int>()
            : Enumerable.Range(firstYear, currentYear - firstYear + 1).ToArray();
    }
}
