# GitHub / story

A learning project with an ASP.NET Core API and React dashboard. Each visitor signs in with GitHub to view their profile, repositories, comments, and annual contribution history.

## Requirements

- .NET 10 SDK
- Node.js 20.19+ and npm
- A GitHub OAuth App for local sign-in

## Configure GitHub OAuth

Create a GitHub OAuth App with:

- **Homepage URL:** `http://localhost:5173`
- **Authorization callback URL:** `http://localhost:5080/signin-github`

In PowerShell, store the app credentials with .NET user secrets (do not put them in source files):

```powershell
dotnet user-secrets init --project .\GitHubActivityDashboard.Api
dotnet user-secrets set "GitHub:ClientId" "YOUR_CLIENT_ID" --project .\GitHubActivityDashboard.Api
dotnet user-secrets set "GitHub:ClientSecret" "YOUR_CLIENT_SECRET" --project .\GitHubActivityDashboard.Api
```

The default `GitHub:Scopes` value is `read:user repo read:org`, so private repositories and organisation repositories are included. To restrict the app to public data, override it locally:

```powershell
dotnet user-secrets set "GitHub:Scopes" "read:user public_repo" --project .\GitHubActivityDashboard.Api
```

After changing scopes, sign out and sign in again so GitHub asks you to authorize the new permissions.

**Permission note:** GitHub's classic `repo` OAuth scope is broader than read-only access. Only enable it if you need private repositories and are comfortable granting the permissions GitHub associates with that scope. For a deployed application, prefer a GitHub App configured with the narrowest read-only repository permissions possible.

OAuth tickets and access tokens are stored server-side in an in-memory ticket store; the browser receives only an HTTP-only session cookie. The in-memory store is for local learning only: sessions end when the API restarts, and a multi-instance deployment needs a shared, protected ticket store. Use HTTPS in deployments.

## Run locally

Open two PowerShell terminals in `exercise1`.

Terminal 1 — API:

```powershell
dotnet run --project .\GitHubActivityDashboard.Api --launch-profile http
```

Terminal 2 — React UI:

```powershell
Push-Location .\github-activity-dashboard-web
npm install
npm run dev
Pop-Location
```

Open the Vite URL shown in the second terminal (normally `http://localhost:5173`) and sign in. The Vite development server proxies `/api` requests to the API at `http://localhost:5080`.

## API endpoints

All data endpoints require an authenticated GitHub session.

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/api/auth/login` | Start GitHub OAuth |
| `GET` | `/api/auth/me` | Return the signed-in GitHub handle |
| `POST` | `/api/auth/logout` | End the session |
| `GET` | `/api/github/profile` | Profile and account statistics |
| `GET` | `/api/github/repositories?page=1` | Repositories the user can access, 30 per page |
| `GET` | `/api/github/comments?limit=12` | Recent issue/PR conversations, reviews, and review comments authored by the user |
| `GET` | `/api/github/activity/years` | Contributions grouped by calendar year plus available public-event counts |

## Data coverage and limits

- **Repositories:** Private repositories appear only if the OAuth authorization permits them. The API returns 30 repositories per page.
- **Comments:** GitHub does not provide a global author search for every comment type. This dashboard searches the newest 12 matching issue/PR discussions, and checks up to the first 100 issue comments, review comments, and reviews in each discussion. It does not include commit comments. The API response and UI label this result as potentially partial.
- **Annual contributions:** Commit, issue, pull-request, and review contribution totals are requested separately for each year since the account was created.
- **Public events:** GitHub's public events endpoint is a limited recent feed (up to 300 events), not a full historical archive. Its event counts are shown separately from contribution totals.
- **Rate limits:** Requests use the signed-in user's GitHub authorization and are subject to GitHub API rate limits. Errors are returned as problem responses rather than being represented as empty results.

## Test and build

From `exercise1`:

```powershell
dotnet test .\GitHubActivityDashboard.slnx
dotnet build .\GitHubActivityDashboard.slnx
Push-Location .\github-activity-dashboard-web
npm run build
Pop-Location
```
