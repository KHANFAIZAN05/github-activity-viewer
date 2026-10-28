using System.Text.RegularExpressions;
using GitHubActivityDashboard.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitHubActivityDashboard.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/github")]
public sealed partial class GitHubController(GitHubDataService githubDataService) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<GitHubProfile>> GetProfile(CancellationToken cancellationToken)
    {
        return Ok(await githubDataService.GetProfileAsync(cancellationToken));
    }

    [HttpGet("repositories")]
    public async Task<ActionResult<RepositoryPage>> GetRepositories(
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > 100)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid page",
                Detail = "Page must be between 1 and 100."
            });
        }

        return Ok(await githubDataService.GetRepositoriesAsync(page, cancellationToken));
    }

    [HttpGet("repositories/{owner}/{name}")]
    public async Task<ActionResult<RepositoryDetail>> GetRepository(
        string owner,
        string name,
        CancellationToken cancellationToken)
    {
        if (!RepositoryNamePattern().IsMatch(owner) || !RepositoryNamePattern().IsMatch(name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid repository",
                Detail = "Owner and repository names may only contain letters, numbers, '.', '-' and '_'."
            });
        }

        return Ok(await githubDataService.GetRepositoryDetailAsync(owner, name, cancellationToken));
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,100}$")]
    private static partial Regex RepositoryNamePattern();

    [HttpGet("comments")]
    public async Task<ActionResult<CommentPage>> GetComments(
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1 || limit > 20)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid limit",
                Detail = "Limit must be between 1 and 20."
            });
        }

        return Ok(await githubDataService.GetCommentsAsync(limit, cancellationToken));
    }

    [HttpGet("activity/years")]
    public async Task<ActionResult<ActivityResponse>> GetActivity(CancellationToken cancellationToken)
    {
        return Ok(await githubDataService.GetActivityAsync(cancellationToken));
    }
}
