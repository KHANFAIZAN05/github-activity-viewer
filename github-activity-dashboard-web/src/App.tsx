import { useEffect, useState, type ReactNode } from 'react'
import {
  Activity as ActivityIcon, ChevronRight, FolderGit2, LayoutDashboard, LogOut, MessageSquareText,
  Search, ShieldCheck, Star, UserCheck, Users, CalendarDays, Lock,
} from 'lucide-react'
import { apiGet, AuthenticationRequiredError } from './api'
import type {
  ActivityResponse,
  AuthUser,
  CommentPage,
  GitHubProfile,
  RepositoryPage,
} from './types'

import { ThemeSwitch, useTheme } from './theme'
import { RepositoryDetailView } from './RepositoryDetailView'

type Section = 'overview' | 'repositories' | 'comments' | 'activity'

interface Resource<T> {
  data: T | null
  error: string | null
  loading: boolean
}

const emptyResource = <T,>(): Resource<T> => ({ data: null, error: null, loading: true })

function App() {
  const [user, setUser] = useState<AuthUser | null>(null)
  const [authLoading, setAuthLoading] = useState(true)
  const [authError, setAuthError] = useState<string | null>(null)
  const [section, setSection] = useState<Section>('overview')
  const [profile, setProfile] = useState<Resource<GitHubProfile>>(emptyResource)
  const [repositories, setRepositories] = useState<Resource<RepositoryPage>>(emptyResource)
  const [comments, setComments] = useState<Resource<CommentPage>>(emptyResource)
  const [activity, setActivity] = useState<Resource<ActivityResponse>>(emptyResource)
  const [signingOut, setSigningOut] = useState(false)
  const [theme, setTheme] = useTheme()
  const [selectedRepo, setSelectedRepo] = useState<string | null>(null)

  const navigate = (next: Section) => {
    setSelectedRepo(null)
    setSection(next)
  }
  const openRepo = (fullName: string) => {
    setSelectedRepo(fullName)
    setSection('repositories')
  }

  useEffect(() => {
    const controller = new AbortController()
    apiGet<AuthUser>('/api/auth/me', controller.signal)
      .then(setUser)
      .catch((error: unknown) => {
        if (!controller.signal.aborted && !(error instanceof AuthenticationRequiredError)) {
          const message = error instanceof Error ? error.message : 'Could not verify the GitHub session.'
          setAuthError(message)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setAuthLoading(false)
      })

    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!user) return
    const controller = new AbortController()
    const load = <T,>(
      path: string,
      setResource: (resource: Resource<T>) => void,
    ) => {
      setResource({ data: null, error: null, loading: true })
      apiGet<T>(path, controller.signal)
        .then((data) => setResource({ data, error: null, loading: false }))
        .catch((error: unknown) => {
          if (!controller.signal.aborted) {
            setResource({
              data: null,
              error: error instanceof Error ? error.message : 'Something went wrong.',
              loading: false,
            })
          }
        })
    }

    load('/api/github/profile', setProfile)
    load('/api/github/repositories', setRepositories)
    load('/api/github/comments', setComments)
    load('/api/github/activity/years', setActivity)
    return () => controller.abort()
  }, [user])

  const signOut = async () => {
    setSigningOut(true)
    setAuthError(null)
    try {
      const response = await fetch('/api/auth/logout', {
        method: 'POST',
        credentials: 'include',
      })
      if (!response.ok) throw new Error('Could not sign out. Please try again.')
      setUser(null)
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : 'Could not sign out.')
    } finally {
      setSigningOut(false)
    }
  }

  if (authLoading) {
    return <main className="center-state"><span className="spinner" />Checking your GitHub session…</main>
  }

  if (!user) {
    return (
      <main className="sign-in-page">
        <ThemeSwitch theme={theme} onChange={setTheme} />
        <div className="sign-in-card">
          <div className="brand-mark" aria-hidden="true">GH</div>
          <p className="eyebrow">YOUR DEVELOPER STORY</p>
          <h1>GitHub, at a glance.</h1>
          <p className="muted">Explore your repositories, conversations, and contributions in one place.</p>
          {authError && <p className="notice error" role="alert">{authError}</p>}
          <a className="button primary sign-in-button" href="/api/auth/login">
            <GitHubIcon /> Sign in with GitHub
          </a>
          <p className="fine-print">Your GitHub data is loaded after you authorize this app.</p>
        </div>
        <p className="sign-in-footer">A personal project for learning the GitHub API</p>
      </main>
    )
  }

  const nav: { id: Section; title: string; icon: ReactNode }[] = [
    { id: 'overview', title: 'Overview', icon: <LayoutDashboard size={18} /> },
    { id: 'repositories', title: 'Repositories', icon: <FolderGit2 size={18} /> },
    { id: 'comments', title: 'Comments', icon: <MessageSquareText size={18} /> },
    { id: 'activity', title: 'Activity', icon: <ActivityIcon size={18} /> },
  ]

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a className="brand" href="#overview" onClick={() => navigate('overview')}>
          <span className="brand-mark">GH</span>
          <span>GitHub<span className="brand-light"> / story</span></span>
        </a>
        <div className="workspace-label">WORKSPACE</div>
        <nav className="side-nav" aria-label="Main navigation">
          {nav.map((item) => (
            <button
              className={`nav-item ${section === item.id ? 'active' : ''}`}
              key={item.id}
              onClick={() => navigate(item.id)}
              aria-current={section === item.id ? 'page' : undefined}
            >
              <span className="nav-icon" aria-hidden="true">{item.icon}</span>
              {item.title}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="privacy-note"><ShieldCheck size={14} /> Connected securely</div>
          <div className="profile-mini">
            {user.avatarUrl
              ? <img src={user.avatarUrl} alt="" />
              : <span className="avatar-fallback">{user.login.slice(0, 1).toUpperCase()}</span>}
            <div className="profile-mini-copy">
              <strong>{user.login}</strong>
              <a href={`https://github.com/${encodeURIComponent(user.login)}`} target="_blank" rel="noreferrer">View GitHub profile</a>
            </div>
          </div>
          <button className="sign-out" onClick={signOut} disabled={signingOut}>
            <LogOut size={14} /> {signingOut ? 'Signing out…' : 'Sign out'}
          </button>
        </div>
      </aside>

      <main className="main-content">
        <header className="topbar">
          <div className="breadcrumb">
            Workspace <span>/</span> {nav.find((item) => item.id === section)?.title}
            {selectedRepo && <><span>/</span> {selectedRepo.split('/')[1]}</>}
          </div>
          <div className="topbar-actions">
            <ThemeSwitch theme={theme} onChange={setTheme} />
            <div className="topbar-tag"><span className="status-dot" /> GitHub connected</div>
          </div>
        </header>
        <div className="page-content">
          {authError && <p className="notice error" role="alert">{authError}</p>}
          {section === 'overview' && (
            <Overview
              profile={profile}
              repositories={repositories}
              comments={comments}
              activity={activity}
              onNavigate={navigate}
              onOpenRepo={openRepo}
            />
          )}
          {section === 'repositories' && (selectedRepo
            ? <RepositoryDetailView fullName={selectedRepo} onBack={() => setSelectedRepo(null)} />
            : <Repositories resource={repositories} onOpenRepo={openRepo} />)}
          {section === 'comments' && <Comments resource={comments} />}
          {section === 'activity' && <Activity resource={activity} />}
        </div>
      </main>
    </div>
  )
}

function Overview({
  profile,
  repositories,
  comments,
  activity,
  onNavigate,
  onOpenRepo,
}: {
  profile: Resource<GitHubProfile>
  repositories: Resource<RepositoryPage>
  comments: Resource<CommentPage>
  activity: Resource<ActivityResponse>
  onNavigate: (section: Section) => void
  onOpenRepo: (fullName: string) => void
}) {
  const person = profile.data
  return (
    <>
      <section className="welcome-row">
        <div>
          <p className="eyebrow">YOUR GITHUB, IN CONTEXT</p>
          <h1>{person?.name ? `Welcome, ${person.name.split(' ')[0]}.` : 'Welcome back.'}</h1>
          <p className="muted">A snapshot of what you’ve been building and sharing.</p>
        </div>
        <div className="date-chip"><CalendarDays size={15} className="calendar-icon" /> Your developer journey</div>
      </section>

      {profile.loading && <LoadingCard label="Loading your profile…" />}
      {profile.error && <ErrorCard message={profile.error} />}
      {person && (
        <section className="profile-banner">
          <div className="profile-identity">
            <img className="profile-avatar" src={person.avatar_url} alt={`${person.login}'s avatar`} />
            <div>
              <div className="profile-name">{person.name || person.login}</div>
              <a className="profile-handle" href={person.html_url} target="_blank" rel="noreferrer">@{person.login}</a>
              {person.bio && <p className="profile-bio">{person.bio}</p>}
            </div>
          </div>
          <div className="profile-meta">
            <span>On GitHub since</span>
            <strong>{new Date(person.created_at).getFullYear()}</strong>
          </div>
        </section>
      )}

      <section className="stat-grid" aria-label="GitHub stats">
        <StatCard label="Public repositories" value={person?.public_repos}         icon={<FolderGit2 size={17} />} tint="purple" />
                <StatCard label="Followers" value={person?.followers} icon={<Users size={17} />} tint="blue" />
                <StatCard label="Following" value={person?.following} icon={<UserCheck size={17} />} tint="orange" />
                <StatCard label="Repositories shown" value={repositories.data?.items.length} icon={<Star size={17} />} tint="green" />
      </section>

      <div className="section-heading">
        <div><p className="eyebrow">AT A GLANCE</p><h2>Your activity</h2></div>
        <button className="text-button" onClick={() => onNavigate('activity')}>View activity <span aria-hidden="true">→</span></button>
      </div>
      {activity.loading && <LoadingCard label="Gathering yearly contributions…" />}
      {activity.error && <ErrorCard message={activity.error} />}
      {activity.data && <ActivityChart data={activity.data} compact />}

      <div className="content-grid">
        <section className="panel">
          <div className="panel-heading">
            <div><p className="eyebrow">RECENT WORK</p><h2>Repositories</h2></div>
            <button className="text-button" onClick={() => onNavigate('repositories')}>See all <span aria-hidden="true">→</span></button>
          </div>
          {repositories.loading && <LoadingCard label="Loading repositories…" />}
          {repositories.error && <ErrorCard message={repositories.error} />}
          {repositories.data && <RepositoryList resource={repositories} compact onOpenRepo={onOpenRepo} />}
        </section>
        <section className="panel">
          <div className="panel-heading">
            <div><p className="eyebrow">CONVERSATIONS</p><h2>Your comments</h2></div>
            <button className="text-button" onClick={() => onNavigate('comments')}>See all <span aria-hidden="true">→</span></button>
          </div>
          {comments.loading && <LoadingCard label="Finding your comments…" />}
          {comments.error && <ErrorCard message={comments.error} />}
          {comments.data && <CommentList resource={comments} compact />}
        </section>
      </div>
    </>
  )
}

function Repositories({ resource, onOpenRepo }: { resource: Resource<RepositoryPage>; onOpenRepo: (fullName: string) => void }) {
  return (
    <>
      <PageTitle eyebrow="YOUR CODE" title="Repositories" description="Projects you own or can access on GitHub." />
      {resource.loading && <LoadingCard label="Loading repositories…" />}
      {resource.error && <ErrorCard message={resource.error} />}
      {resource.data && (
        <>
          {resource.data.hasMore && (
            <p className="notice warning">Showing the {resource.data.items.length} most recently updated repositories (the app's upper limit).</p>
          )}
          <RepositoryList resource={resource} paged onOpenRepo={onOpenRepo} />
        </>
      )}
    </>
  )
}

const REPOS_PER_PAGE = 20

function RepositoryList({ resource, compact = false, paged = false, onOpenRepo }: {
  resource: Resource<RepositoryPage>
  compact?: boolean
  paged?: boolean
  onOpenRepo: (fullName: string) => void
}) {
  const [filter, setFilter] = useState('')
  const [pageIndex, setPageIndex] = useState(0)
  const repos = resource.data?.items ?? []
  const filtered = repos.filter((repo) =>
    `${repo.fullName} ${repo.description ?? ''} ${repo.language ?? ''}`.toLowerCase().includes(filter.toLowerCase()),
  )
  const pageCount = Math.max(1, Math.ceil(filtered.length / REPOS_PER_PAGE))
  const safePage = Math.min(pageIndex, pageCount - 1)
  const visible = compact
    ? filtered.slice(0, 4)
    : paged ? filtered.slice(safePage * REPOS_PER_PAGE, (safePage + 1) * REPOS_PER_PAGE) : filtered
  return (
    <div className="repository-list">
      {!compact && (
        <label className="search-box">
          <Search size={16} aria-hidden="true" />
          <input
            value={filter}
            onChange={(event) => { setFilter(event.target.value); setPageIndex(0) }}
            placeholder={`Filter ${repos.length} repositories`}
          />
        </label>
      )}
      {visible.length === 0
        ? <EmptyState title="No repositories found" text={filter ? 'Try a different search.' : 'No repositories are available for this account.'} />
        : visible.map((repo) => (
          <button type="button" className="repo-row" onClick={() => onOpenRepo(repo.fullName)} key={repo.id}>
            <span className="repo-icon">{repo.isPrivate ? <Lock size={15} /> : <FolderGit2 size={15} />}</span>
            <span className="repo-copy">
              <strong>{repo.fullName}</strong>
              <span>{repo.description || 'No description provided.'}</span>
              <span className="repo-meta">
                {repo.isPrivate && <span className="visibility-tag">Private</span>}
                {repo.language && <><i className={`language-dot ${languageClass(repo.language)}`} />{repo.language}</>}
                <span className="repo-stars"><Star size={12} /> {repo.stars}</span>
              </span>
            </span>
            <ChevronRight className="external-arrow" size={16} aria-hidden="true" />
          </button>
        ))}
      {paged && filtered.length > REPOS_PER_PAGE && (
        <div className="pagination">
          <span>Page {safePage + 1} of {pageCount} · {filtered.length} repositories</span>
          <div>
            <button onClick={() => setPageIndex(safePage - 1)} disabled={safePage <= 0}>Previous</button>
            <button onClick={() => setPageIndex(safePage + 1)} disabled={safePage >= pageCount - 1}>Next</button>
          </div>
        </div>
      )}
    </div>
  )
}

function Comments({ resource }: { resource: Resource<CommentPage> }) {
  return (
    <>
      <PageTitle eyebrow="YOUR CONVERSATIONS" title="Comments" description="Issue comments, pull request reviews, and review comments authored by you." />
      {resource.loading && <LoadingCard label="Finding your comments…" />}
      {resource.error && <ErrorCard message={resource.error} />}
      {resource.data && <CommentList resource={resource} />}
    </>
  )
}

function CommentList({ resource, compact = false }: { resource: Resource<CommentPage>; compact?: boolean }) {
  const result = resource.data
  const items = result?.items ?? []
  return (
    <div className="comment-list">
      <p className={`notice ${result?.isPartial ? 'warning' : 'info'}`}>
        {result?.coverageNote ?? 'Comment history is being loaded.'}
      </p>
      {items.length === 0
        ? <EmptyState title="No comments found" text="No matching authored comments were found in the discussions GitHub returned." />
        : (compact ? items.slice(0, 3) : items).map((comment) => (
          <article className="comment-card" key={comment.id}>
            <div className="comment-header">
              <span className="comment-kind">{comment.kind}</span>
              <time dateTime={comment.createdAt}>{formatDate(comment.createdAt)}</time>
            </div>
            <a className="comment-context" href={comment.htmlUrl} target="_blank" rel="noreferrer">
              {comment.repository} <span aria-hidden="true">·</span> {comment.subject}
            </a>
            <p className="comment-body">{comment.body}</p>
          </article>
        ))}
      {!compact && result && (
        <p className="fine-print">Matching discussions: {result.matchingDiscussions}. This view is bounded to the newest 12 discussions.</p>
      )}
    </div>
  )
}

function Activity({ resource }: { resource: Resource<ActivityResponse> }) {
  return (
    <>
      <PageTitle eyebrow="YOUR JOURNEY" title="Activity across the years" description="Contribution totals and the public events GitHub currently exposes." />
      {resource.loading && <LoadingCard label="Gathering yearly contributions…" />}
      {resource.error && <ErrorCard message={resource.error} />}
      {resource.data && <ActivityChart data={resource.data} />}
    </>
  )
}

function ActivityChart({ data, compact = false }: { data: ActivityResponse; compact?: boolean }) {
  const years = [...data.years].reverse()
  const shown = compact ? years.slice(-6) : years
  const max = Math.max(1, ...shown.map((year) => year.commits + year.issues + year.pullRequests + year.reviews))
  return (
    <section className={`activity-panel ${compact ? 'compact' : ''}`}>
      {!compact && <p className="notice info">{data.publicEventNote}</p>}
      <div className="chart-legend">
        <span><i className="legend-dot commits" /> Commits</span>
        <span><i className="legend-dot pull-requests" /> Pull requests</span>
        <span><i className="legend-dot issues" /> Issues</span>
        <span><i className="legend-dot events" /> Public events</span>
      </div>
      {shown.length === 0
        ? <EmptyState title="No activity data" text="GitHub did not return annual contribution data for this account." />
        : <div className="year-list">
          {shown.map((year) => {
            const contributions = year.commits + year.issues + year.pullRequests + year.reviews
            const width = `${Math.max(3, (contributions / max) * 100)}%`
            return (
              <div className="year-row" key={year.year}>
                <div className="year-label">{year.year}</div>
                <div className="year-bar-wrap">
                  <div className="year-bar" style={{ width }} aria-label={`${contributions} contributions`}>
                    {year.commits > 0 && <span className="bar-segment commits" style={{ flex: year.commits }} />}
                    {year.pullRequests > 0 && <span className="bar-segment pull-requests" style={{ flex: year.pullRequests }} />}
                    {year.issues > 0 && <span className="bar-segment issues" style={{ flex: year.issues }} />}
                    {year.reviews > 0 && <span className="bar-segment reviews" style={{ flex: year.reviews }} />}
                  </div>
                </div>
                <div className="year-total">{contributions.toLocaleString()} <span>contributions</span></div>
                <div className="event-count">{year.publicEventCount} <span>events</span></div>
              </div>
            )
          })}
        </div>}
      <div className="chart-footnote">Events are GitHub’s recent public feed; the feed is not a full historical archive.</div>
    </section>
  )
}

function StatCard({ label, value, icon, tint }: { label: string; value?: number; icon: ReactNode; tint: string }) {
  return (
    <div className="stat-card">
      <span className={`stat-icon ${tint}`} aria-hidden="true">{icon}</span>
      <span className="stat-label">{label}</span>
      <strong className="stat-value">{value === undefined ? '—' : value.toLocaleString()}</strong>
    </div>
  )
}

function PageTitle({ eyebrow, title, description }: { eyebrow: string; title: string; description: string }) {
  return (
    <section className="page-title">
      <p className="eyebrow">{eyebrow}</p>
      <h1>{title}</h1>
      <p className="muted">{description}</p>
    </section>
  )
}

function LoadingCard({ label }: { label: string }) {
  return <div className="loading-card"><span className="spinner" />{label}</div>
}

function ErrorCard({ message }: { message: string }) {
  return <p className="notice error" role="alert">{message}</p>
}

function EmptyState({ title, text }: { title: string; text: string }) {
  return <div className="empty-state"><strong>{title}</strong><span>{text}</span></div>
}

function GitHubIcon() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path fill="currentColor" d="M12 .8a11.2 11.2 0 0 0-3.54 21.83c.56.1.77-.24.77-.54v-2.1c-3.14.68-3.8-1.33-3.8-1.33-.5-1.3-1.25-1.65-1.25-1.65-1.02-.7.08-.69.08-.69 1.13.08 1.73 1.16 1.73 1.16 1 .1.75 2.44 3.32 1.74.1-.73.39-1.22.7-1.5-2.5-.29-5.13-1.25-5.13-5.56 0-1.23.44-2.24 1.16-3.03-.12-.29-.5-1.44.11-2.99 0 0 .95-.3 3.08 1.16a10.7 10.7 0 0 1 5.6 0c2.14-1.45 3.08-1.16 3.08-1.16.61 1.55.23 2.7.11 2.99.72.79 1.16 1.8 1.16 3.03 0 4.32-2.63 5.26-5.14 5.54.4.35.75 1.03.75 2.08v3.08c0 .3.2.65.78.54A11.2 11.2 0 0 0 12 .8Z" />
    </svg>
  )
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' }).format(new Date(value))
}

function languageClass(language: string) {
  const key = language.toLowerCase().replace(/[^a-z]/g, '')
  return `lang-${key}`
}

export default App
