using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;

namespace GitHubActivityDashboard.Api.Services;

public sealed class GitHubDataService(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor)
{
    private const int ApiPageSize = 100;
    private const string GraphQlEndpoint = "https://api.github.com/graphql";

    private const int MaxRepositoryPages = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitHubProfile> GetProfileAsync(CancellationToken cancellationToken)
    {
        return await GetAsync<GitHubProfile>("user", cancellationToken);
    }

    public async Task<RepositoryPage> GetRepositoriesAsync(int page, CancellationToken cancellationToken)
    {
        var all = new List<GitHubRepository>();
        for (var current = 1; current <= MaxRepositoryPages; current++)
        {
            var batch = await GetAsync<List<GitHubRepository>>(
                $"user/repos?per_page={ApiPageSize}&page={current}&sort=updated&direction=desc&affiliation=owner,collaborator,organization_member",
                cancellationToken);
            all.AddRange(batch);
            if (batch.Count < ApiPageSize) return new RepositoryPage(all.Select(r => r.ToSummary()).ToArray(), 1, false);
        }

        return new RepositoryPage(all.Select(r => r.ToSummary()).ToArray(), 1, true);
    }

    public async Task<RepositoryDetail> GetRepositoryDetailAsync(
        string owner,
        string name,
        CancellationToken cancellationToken)
    {
        var path = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";
        var repoTask = GetAsync<RawRepositoryDetail>(path, cancellationToken);
        var languagesTask = TryGetAsync<Dictionary<string, long>>($"{path}/languages", cancellationToken);
        var commitsTask = TryGetAsync<List<RawCommit>>($"{path}/commits?per_page=8", cancellationToken);
        var pullsTask = TryGetAsync<List<RawPullRequest>>($"{path}/pulls?state=open&per_page=5", cancellationToken);
        var readmeTask = TryGetAsync<RawReadme>($"{path}/readme", cancellationToken);
        await Task.WhenAll(repoTask, languagesTask, commitsTask, pullsTask, readmeTask);

        var repo = await repoTask;
        var languages = await languagesTask ?? [];
        var totalBytes = languages.Values.Sum();
        var languageList = languages
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new RepositoryLanguage(pair.Key, pair.Value, totalBytes == 0 ? 0 : Math.Round(pair.Value * 100.0 / totalBytes, 1)))
            .ToArray();

        var commits = (await commitsTask ?? []).Select(commit => new RepositoryCommit(
            commit.Sha,
            commit.Commit.Message.Split('\n', 2)[0],
            commit.Author?.Login ?? commit.Commit.Author?.Name,
            commit.Commit.Author?.Date,
            commit.HtmlUrl)).ToArray();

        var pulls = (await pullsTask ?? []).Select(pull => new RepositoryPullRequest(
            pull.Number, pull.Title, pull.User?.Login, pull.Draft, pull.CreatedAt, pull.HtmlUrl)).ToArray();

        var readmeText = DecodeReadme(await readmeTask, out var truncated);

        return new RepositoryDetail(
            repo.Id, repo.Name, repo.FullName, repo.Description, repo.HtmlUrl,
            string.IsNullOrWhiteSpace(repo.Homepage) ? null : repo.Homepage,
            repo.IsPrivate, repo.IsFork, repo.IsArchived, repo.Language, repo.Stars, repo.Forks,
            repo.Watchers, repo.OpenIssues, repo.DefaultBranch, repo.License?.Name,
            repo.Topics ?? [], repo.Size, repo.CreatedAt, repo.UpdatedAt, repo.PushedAt,
            languageList, commits, pulls, readmeText, truncated);
    }

    private const int ReadmeCharLimit = 20_000;

    private static string? DecodeReadme(RawReadme? readme, out bool truncated)
    {
        truncated = false;
        if (readme?.Content is null || readme.Encoding != "base64") return null;
        var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(readme.Content));
        if (text.Length <= ReadmeCharLimit) return text;
        truncated = true;
        return text[..ReadmeCharLimit];
    }

    // Optional repo sections (empty repo, no README, restricted) should not fail the whole page.
    private async Task<T?> TryGetAsync<T>(string relativeUrl, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await GetAsync<T>(relativeUrl, cancellationToken);
        }
        catch (GitHubApiException ex) when (ex.StatusCode is 404 or 409 or 403)
        {
            return null;
        }
    }

    public async Task<CommentPage> GetCommentsAsync(int limit, CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(cancellationToken);
        var search = await GetAsync<IssueSearchResult>(
            $"search/issues?q=commenter:{Uri.EscapeDataString(profile.Login)}&per_page={limit}&page=1&sort=updated&order=desc",
            cancellationToken);

        var collected = new List<GitHubComment>();
        var partial = search.TotalCount > search.Items.Count;
        foreach (var issue in search.Items)
        {
            var repositoryPath = GetRepositoryPath(issue.RepositoryUrl);
            var issueComments = await GetAsync<List<IssueComment>>(
                $"repos/{repositoryPath}/issues/{issue.Number}/comments?per_page={ApiPageSize}&page=1",
                cancellationToken);
            AddIssueComments(collected, issueComments, issue, profile.Login);
            partial |= issueComments.Count == ApiPageSize;

            if (!issue.IsPullRequest)
            {
                continue;
            }

            var reviewComments = await GetAsync<List<ReviewComment>>(
                $"repos/{repositoryPath}/pulls/{issue.Number}/comments?per_page={ApiPageSize}&page=1",
                cancellationToken);
            AddReviewComments(collected, reviewComments, issue, profile.Login);
            partial |= reviewComments.Count == ApiPageSize;

            var reviews = await GetAsync<List<PullRequestReview>>(
                $"repos/{repositoryPath}/pulls/{issue.Number}/reviews?per_page={ApiPageSize}&page=1",
                cancellationToken);
            AddReviews(collected, reviews, issue, profile.Login);
            partial |= reviews.Count == ApiPageSize;
        }

        var comments = collected
            .OrderByDescending(comment => comment.CreatedAt)
            .Take(100)
            .ToArray();

        return new CommentPage(
            comments,
            partial,
            search.TotalCount,
            "GitHub search covers issue and pull-request discussions. Commit comments are not included because GitHub provides no global author search for them; each discussion returns its first 100 comments.");
    }

    public async Task<ActivityResponse> GetActivityAsync(CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(cancellationToken);
        var currentYear = DateTimeOffset.UtcNow.Year;
        var yearly = new List<YearActivity>();

        foreach (var year in ActivityYearRange.GetYears(profile.CreatedAt, currentYear))
        {
            var from = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var to = year == currentYear
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(-1);
            var contributions = await GetContributionYearAsync(
                profile.Login,
                from,
                to,
                cancellationToken);

            yearly.Add(contributions);
        }

        var events = await GetPublicEventsAsync(profile.Login, cancellationToken);
        var eventCounts = events
            .GroupBy(item => item.CreatedAt.Year)
            .ToDictionary(group => group.Key, group => group.Count());

        return new ActivityResponse(
            yearly.Select(item => item with
            {
                PublicEventCount = eventCounts.GetValueOrDefault(item.Year)
            }).ToArray(),
            "Public events show only the recent history GitHub exposes (up to 300 events). Contribution totals are queried separately by calendar year.");
    }

    private async Task<YearActivity> GetContributionYearAsync(
        string login,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        const string query = """
            query($login: String!, $from: DateTime!, $to: DateTime!) {
              user(login: $login) {
                contributionsCollection(from: $from, to: $to) {
                  totalCommitContributions
                  totalIssueContributions
                  totalPullRequestContributions
                  totalPullRequestReviewContributions
                }
              }
            }
            """;

        var result = await PostGraphQlAsync<ContributionResponse>(
            query,
            new
            {
                login,
                from = from.ToString("O"),
                to = to.ToString("O")
            },
            cancellationToken);
        var totals = result.User?.ContributionsCollection
            ?? throw new GitHubApiException(StatusCodes.Status502BadGateway, "GitHub did not return contribution data.");

        return new YearActivity(
            from.Year,
            totals.TotalCommitContributions,
            totals.TotalIssueContributions,
            totals.TotalPullRequestContributions,
            totals.TotalPullRequestReviewContributions,
            0);
    }

    private async Task<List<GitHubEvent>> GetPublicEventsAsync(
        string login,
        CancellationToken cancellationToken)
    {
        var events = new List<GitHubEvent>();
        for (var page = 1; page <= 3; page++)
        {
            var pageEvents = await GetAsync<List<GitHubEvent>>(
                $"users/{Uri.EscapeDataString(login)}/events/public?per_page={ApiPageSize}&page={page}",
                cancellationToken);
            events.AddRange(pageEvents);
            if (pageEvents.Count < ApiPageSize)
            {
                break;
            }
        }

        return events;
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("GitHub");
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, relativeUrl, cancellationToken);
        using var response = await client.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostGraphQlAsync<T>(
        string query,
        object variables,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("GitHub");
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, GraphQlEndpoint, cancellationToken);
        request.Content = JsonContent.Create(new { query, variables }, options: JsonOptions);
        using var response = await client.SendAsync(request, cancellationToken);
        var envelope = await ReadResponseAsync<GraphQlEnvelope<T>>(response, cancellationToken);
        if (envelope.Errors is { Count: > 0 })
        {
            throw new GitHubApiException(
                StatusCodes.Status502BadGateway,
                envelope.Errors[0].Message);
        }

        return envelope.Data
            ?? throw new GitHubApiException(StatusCodes.Status502BadGateway, "GitHub returned an empty GraphQL response.");
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken)
    {
        var accessToken = await httpContextAccessor.HttpContext!.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new GitHubApiException(
                StatusCodes.Status401Unauthorized,
                "The GitHub session has expired. Sign in again to continue.");
        }

        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (Uri.TryCreate(requestUri, UriKind.Absolute, out var absoluteUri) &&
            absoluteUri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            request.RequestUri = absoluteUri;
        }

        return request;
    }

    private static async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "GitHub rejected the access token. Sign out and authorize again.",
                HttpStatusCode.Forbidden => "GitHub denied this request. Check the granted OAuth permissions or rate limit.",
                HttpStatusCode.NotFound => "GitHub could not find the requested resource.",
                _ => $"GitHub returned HTTP {(int)response.StatusCode}."
            };
            throw new GitHubApiException((int)response.StatusCode, detail);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new GitHubApiException(StatusCodes.Status502BadGateway, "GitHub returned an empty response.");
    }

    private static string GetRepositoryPath(string repositoryUrl)
    {
        const string prefix = "https://api.github.com/repos/";
        if (!repositoryUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new GitHubApiException(StatusCodes.Status502BadGateway, "GitHub returned an unexpected repository URL.");
        }

        return repositoryUrl[prefix.Length..];
    }

    private static void AddIssueComments(
        ICollection<GitHubComment> results,
        IEnumerable<IssueComment> comments,
        SearchIssue issue,
        string login)
    {
        foreach (var comment in comments.Where(comment =>
                     string.Equals(comment.User?.Login, login, StringComparison.OrdinalIgnoreCase)))
        {
            results.Add(new GitHubComment(
                $"issue-{comment.Id}",
                "Issue comment",
                issue.Title,
                issue.RepositoryName,
                comment.Body ?? string.Empty,
                comment.CreatedAt,
                comment.HtmlUrl));
        }
    }

    private static void AddReviewComments(
        ICollection<GitHubComment> results,
        IEnumerable<ReviewComment> comments,
        SearchIssue issue,
        string login)
    {
        foreach (var comment in comments.Where(comment =>
                     string.Equals(comment.User?.Login, login, StringComparison.OrdinalIgnoreCase)))
        {
            results.Add(new GitHubComment(
                $"review-comment-{comment.Id}",
                "Pull request review comment",
                issue.Title,
                issue.RepositoryName,
                comment.Body ?? string.Empty,
                comment.CreatedAt,
                comment.HtmlUrl));
        }
    }

    private static void AddReviews(
        ICollection<GitHubComment> results,
        IEnumerable<PullRequestReview> reviews,
        SearchIssue issue,
        string login)
    {
        foreach (var review in reviews.Where(review =>
                     string.Equals(review.User?.Login, login, StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(review.Body)))
        {
            results.Add(new GitHubComment(
                $"review-{review.Id}",
                "Pull request review",
                issue.Title,
                issue.RepositoryName,
                review.Body!,
                review.SubmittedAt ?? review.CreatedAt,
                review.HtmlUrl));
        }
    }

    private sealed record IssueSearchResult(
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("items")] List<SearchIssue> Items);

    private sealed record SearchIssue(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("repository_url")] string RepositoryUrl,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("pull_request")] JsonElement PullRequest)
    {
        public string RepositoryName => RepositoryUrl["https://api.github.com/repos/".Length..];
        public bool IsPullRequest => PullRequest.ValueKind == JsonValueKind.Object;
    }

    private sealed record GitHubUserReference(
        [property: JsonPropertyName("login")] string? Login);

    private sealed record IssueComment(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("user")] GitHubUserReference? User);

    private sealed record ReviewComment(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("user")] GitHubUserReference? User);

    private sealed record PullRequestReview(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("submitted_at")] DateTimeOffset? SubmittedAt,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("user")] GitHubUserReference? User);

    private sealed record ContributionResponse(
        [property: JsonPropertyName("user")] ContributionUser? User);

    private sealed record ContributionUser(
        [property: JsonPropertyName("contributionsCollection")] ContributionTotals? ContributionsCollection);

    private sealed record ContributionTotals(
        [property: JsonPropertyName("totalCommitContributions")] int TotalCommitContributions,
        [property: JsonPropertyName("totalIssueContributions")] int TotalIssueContributions,
        [property: JsonPropertyName("totalPullRequestContributions")] int TotalPullRequestContributions,
        [property: JsonPropertyName("totalPullRequestReviewContributions")] int TotalPullRequestReviewContributions);

    private sealed record GraphQlEnvelope<T>(
        [property: JsonPropertyName("data")] T? Data,
        [property: JsonPropertyName("errors")] List<GraphQlError>? Errors);

    private sealed record GraphQlError(
        [property: JsonPropertyName("message")] string Message);

    private sealed record RawLicense([property: JsonPropertyName("name")] string? Name);

    private sealed record RawRepositoryDetail(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("homepage")] string? Homepage,
        [property: JsonPropertyName("private")] bool IsPrivate,
        [property: JsonPropertyName("fork")] bool IsFork,
        [property: JsonPropertyName("archived")] bool IsArchived,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("stargazers_count")] int Stars,
        [property: JsonPropertyName("forks_count")] int Forks,
        [property: JsonPropertyName("subscribers_count")] int Watchers,
        [property: JsonPropertyName("open_issues_count")] int OpenIssues,
        [property: JsonPropertyName("default_branch")] string? DefaultBranch,
        [property: JsonPropertyName("license")] RawLicense? License,
        [property: JsonPropertyName("topics")] List<string>? Topics,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt);

    private sealed record RawCommitAuthor(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("date")] DateTimeOffset? Date);

    private sealed record RawCommitBody(
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("author")] RawCommitAuthor? Author);

    private sealed record RawCommit(
        [property: JsonPropertyName("sha")] string Sha,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("commit")] RawCommitBody Commit,
        [property: JsonPropertyName("author")] GitHubUserReference? Author);

    private sealed record RawPullRequest(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("user")] GitHubUserReference? User);

    private sealed record RawReadme(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("encoding")] string? Encoding);

    private sealed record GitHubEvent(
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
}

public sealed record GitHubProfile(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("avatar_url")] string AvatarUrl,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("bio")] string? Bio,
    [property: JsonPropertyName("public_repos")] int PublicRepos,
    [property: JsonPropertyName("followers")] int Followers,
    [property: JsonPropertyName("following")] int Following,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record GitHubRepository(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("private")] bool IsPrivate,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("stargazers_count")] int Stars,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt)
{
    public RepositorySummary ToSummary() =>
        new(Id, Name, FullName, Description, HtmlUrl, IsPrivate, Language, Stars, UpdatedAt);
}

public sealed record RepositorySummary(
    long Id,
    string Name,
    string FullName,
    string? Description,
    string HtmlUrl,
    bool IsPrivate,
    string? Language,
    int Stars,
    DateTimeOffset UpdatedAt);

public sealed record RepositoryPage(
    IReadOnlyList<RepositorySummary> Items,
    int Page,
    bool HasMore);

public sealed record RepositoryLanguage(string Name, long Bytes, double Percent);

public sealed record RepositoryCommit(
    string Sha,
    string Message,
    string? Author,
    DateTimeOffset? Date,
    string HtmlUrl);

public sealed record RepositoryPullRequest(
    int Number,
    string Title,
    string? Author,
    bool IsDraft,
    DateTimeOffset CreatedAt,
    string HtmlUrl);

public sealed record RepositoryDetail(
    long Id,
    string Name,
    string FullName,
    string? Description,
    string HtmlUrl,
    string? Homepage,
    bool IsPrivate,
    bool IsFork,
    bool IsArchived,
    string? Language,
    int Stars,
    int Forks,
    int Watchers,
    int OpenIssues,
    string? DefaultBranch,
    string? License,
    IReadOnlyList<string> Topics,
    int SizeKb,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PushedAt,
    IReadOnlyList<RepositoryLanguage> Languages,
    IReadOnlyList<RepositoryCommit> RecentCommits,
    IReadOnlyList<RepositoryPullRequest> OpenPullRequests,
    string? Readme,
    bool ReadmeTruncated);

public sealed record GitHubComment(
    string Id,
    string Kind,
    string Subject,
    string Repository,
    string Body,
    DateTimeOffset CreatedAt,
    string HtmlUrl);

public sealed record CommentPage(
    IReadOnlyList<GitHubComment> Items,
    bool IsPartial,
    int MatchingDiscussions,
    string CoverageNote);

public sealed record YearActivity(
    int Year,
    int Commits,
    int Issues,
    int PullRequests,
    int Reviews,
    int PublicEventCount);

public sealed record ActivityResponse(
    IReadOnlyList<YearActivity> Years,
    string PublicEventNote);
