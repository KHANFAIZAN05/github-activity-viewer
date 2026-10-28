using System.Net;
using System.Security.Claims;
using System.Text.Json;
using GitHubActivityDashboard.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var frontendUrl = builder.Configuration["GitHub:FrontendUrl"] ?? "http://localhost:5173";
var githubClientId = builder.Configuration["GitHub:ClientId"];
var githubClientSecret = builder.Configuration["GitHub:ClientSecret"];
var scopes = (builder.Configuration["GitHub:Scopes"] ?? "read:user repo read:org")
    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<InMemoryTicketStore>();
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<InMemoryTicketStore>((options, ticketStore) => options.SessionStore = ticketStore);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(frontendUrl)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "gh-dashboard";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddOAuth("GitHub", options =>
    {
        options.ClientId = string.IsNullOrWhiteSpace(githubClientId) ? "not-configured" : githubClientId;
        options.ClientSecret = string.IsNullOrWhiteSpace(githubClientSecret) ? "not-configured" : githubClientSecret;
        options.CallbackPath = "/signin-github";
        options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
        options.TokenEndpoint = "https://github.com/login/oauth/access_token";
        options.UserInformationEndpoint = "https://api.github.com/user";
        options.SaveTokens = true;
        options.Scope.Clear();
        foreach (var scope in scopes)
        {
            options.Scope.Add(scope);
        }

        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, options.UserInformationEndpoint);
                request.Headers.UserAgent.ParseAdd("GitHubActivityDashboard/1.0");
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var response = await context.Backchannel.SendAsync(
                    request,
                    context.HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();

                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                var user = document.RootElement;
                var identity = context.Identity ?? throw new InvalidOperationException("GitHub did not return an identity.");
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.GetProperty("id").GetInt64().ToString()));
                identity.AddClaim(new Claim(ClaimTypes.Name, user.GetProperty("login").GetString() ?? string.Empty));

                if (user.TryGetProperty("avatar_url", out var avatarUrl))
                {
                    identity.AddClaim(new Claim("github:avatar_url", avatarUrl.GetString() ?? string.Empty));
                }
            }
        };
    });

builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GitHubActivityDashboard/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});
builder.Services.AddScoped<GitHubDataService>();

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("GitHubActivityDashboard");
    logger.LogError(exception, "Request failed for {Path}", context.Request.Path);

    var status = exception is GitHubApiException githubException
        ? githubException.StatusCode
        : StatusCodes.Status500InternalServerError;
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = status,
        Title = exception is GitHubApiException
            ? "GitHub API request failed"
            : "The request could not be completed",
        Detail = exception is GitHubApiException
            ? exception.Message
            : "Check the server logs for details."
    });
}));

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/auth/login", (HttpContext context, IConfiguration configuration) =>
{
    if (string.IsNullOrWhiteSpace(configuration["GitHub:ClientId"]) ||
        string.IsNullOrWhiteSpace(configuration["GitHub:ClientSecret"]))
    {
        return Results.Problem(
            title: "GitHub OAuth is not configured",
            detail: "Set GitHub:ClientId and GitHub:ClientSecret using user secrets or environment variables.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var redirectUri = configuration["GitHub:FrontendUrl"] ?? "http://localhost:5173";
    return Results.Challenge(
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = redirectUri },
        ["GitHub"]);
}).AllowAnonymous();

app.MapGet("/api/auth/me", (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        login = context.User.Identity.Name,
        avatarUrl = context.User.FindFirstValue("github:avatar_url")
    });
}).AllowAnonymous();

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

app.MapControllers();
app.Run();

public partial class Program
{
}
