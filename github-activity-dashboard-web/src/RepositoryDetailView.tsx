import { useEffect, useState, type ReactNode } from 'react'
import {
  ArrowLeft, CircleDot, ExternalLink, Eye, GitBranch, GitCommitHorizontal, GitFork,
  GitPullRequest, Globe, Lock, Scale, Star,
} from 'lucide-react'
import { apiGet } from './api'
import type { RepositoryDetail } from './types'

const LANGUAGE_COLORS = ['#7669df', '#3fae88', '#ee9a58', '#5b9de6', '#d16ba5', '#e0c24a', '#8c8fa3']

const dateFormat = new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
const formatDate = (value: string | null) => (value ? dateFormat.format(new Date(value)) : '—')

function formatSize(kb: number) {
  return kb >= 1024 ? `${(kb / 1024).toFixed(1)} MB` : `${kb} KB`
}

export function RepositoryDetailView({ fullName, onBack }: { fullName: string; onBack: () => void }) {
  const [detail, setDetail] = useState<RepositoryDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const [owner, name] = fullName.split('/')
    setDetail(null)
    setError(null)
    apiGet<RepositoryDetail>(
      `/api/github/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(name)}`,
      controller.signal,
    )
      .then(setDetail)
      .catch((err: unknown) => {
        if (!controller.signal.aborted) setError(err instanceof Error ? err.message : 'Could not load this repository.')
      })
    return () => controller.abort()
  }, [fullName])

  return (
    <>
      <button className="back-button" onClick={onBack}><ArrowLeft size={15} /> All repositories</button>
      {!detail && !error && <div className="loading-card"><span className="spinner" />Loading {fullName}…</div>}
      {error && <p className="notice error" role="alert">{error}</p>}
      {detail && <DetailBody detail={detail} />}
    </>
  )
}

function DetailBody({ detail }: { detail: RepositoryDetail }) {
  return (
    <>
      <section className="repo-hero">
        <div className="repo-hero-top">
          <div>
            <p className="eyebrow">{detail.isFork ? 'FORKED REPOSITORY' : 'REPOSITORY'}</p>
            <h1>{detail.fullName}</h1>
          </div>
          <a className="button primary" href={detail.htmlUrl} target="_blank" rel="noreferrer">
            <ExternalLink size={16} /> Open on GitHub
          </a>
        </div>
        <div className="badge-row">
          <span className="badge">{detail.isPrivate ? <><Lock size={12} /> Private</> : <><Globe size={12} /> Public</>}</span>
          {detail.isArchived && <span className="badge warn">Archived</span>}
          {detail.language && <span className="badge">{detail.language}</span>}
          {detail.license && <span className="badge"><Scale size={12} /> {detail.license}</span>}
        </div>
        <p className="muted">{detail.description || 'No description provided.'}</p>
        {detail.homepage && (
          <a className="repo-homepage" href={detail.homepage} target="_blank" rel="noreferrer noopener">{detail.homepage}</a>
        )}
        {detail.topics.length > 0 && (
          <div className="topic-row">{detail.topics.map((topic) => <span className="topic" key={topic}>{topic}</span>)}</div>
        )}
      </section>

      <section className="stat-grid" aria-label="Repository stats">
        <MiniStat icon={<Star size={17} />} tint="orange" label="Stars" value={detail.stars} />
        <MiniStat icon={<GitFork size={17} />} tint="purple" label="Forks" value={detail.forks} />
        <MiniStat icon={<Eye size={17} />} tint="blue" label="Watchers" value={detail.watchers} />
        <MiniStat icon={<CircleDot size={17} />} tint="green" label="Open issues & PRs" value={detail.openIssues} />
      </section>

      <div className="content-grid">
        <section className="panel">
          <div className="panel-heading"><h2>Languages</h2></div>
          {detail.languages.length === 0
            ? <p className="muted">No language data available.</p>
            : (
              <>
                <div className="lang-bar" role="img" aria-label="Language breakdown">
                  {detail.languages.map((lang, index) => (
                    <span key={lang.name} style={{ width: `${lang.percent}%`, background: LANGUAGE_COLORS[index % LANGUAGE_COLORS.length] }} />
                  ))}
                </div>
                <ul className="lang-legend">
                  {detail.languages.map((lang, index) => (
                    <li key={lang.name}>
                      <i style={{ background: LANGUAGE_COLORS[index % LANGUAGE_COLORS.length] }} />
                      {lang.name}<span>{lang.percent}%</span>
                    </li>
                  ))}
                </ul>
              </>
            )}
        </section>

        <section className="panel">
          <div className="panel-heading"><h2>About</h2></div>
          <dl className="facts">
            <dt><GitBranch size={14} /> Default branch</dt><dd>{detail.defaultBranch ?? '—'}</dd>
            <dt>Created</dt><dd>{formatDate(detail.createdAt)}</dd>
            <dt>Last push</dt><dd>{formatDate(detail.pushedAt)}</dd>
            <dt>Size</dt><dd>{formatSize(detail.sizeKb)}</dd>
          </dl>
        </section>

        <section className="panel">
          <div className="panel-heading"><h2>Recent commits</h2></div>
          {detail.recentCommits.length === 0
            ? <p className="muted">No commits found.</p>
            : (
              <ul className="item-list">
                {detail.recentCommits.map((commit) => (
                  <li key={commit.sha}>
                    <GitCommitHorizontal size={15} className="item-icon" />
                    <a href={commit.htmlUrl} target="_blank" rel="noreferrer">
                      <strong>{commit.message}</strong>
                      <span>{commit.author ?? 'Unknown'} · {formatDate(commit.date)} · {commit.sha.slice(0, 7)}</span>
                    </a>
                  </li>
                ))}
              </ul>
            )}
        </section>

        <section className="panel">
          <div className="panel-heading"><h2>Open pull requests</h2></div>
          {detail.openPullRequests.length === 0
            ? <p className="muted">No open pull requests.</p>
            : (
              <ul className="item-list">
                {detail.openPullRequests.map((pull) => (
                  <li key={pull.number}>
                    <GitPullRequest size={15} className="item-icon" />
                    <a href={pull.htmlUrl} target="_blank" rel="noreferrer">
                      <strong>{pull.title}{pull.isDraft ? ' (draft)' : ''}</strong>
                      <span>#{pull.number} · {pull.author ?? 'Unknown'} · {formatDate(pull.createdAt)}</span>
                    </a>
                  </li>
                ))}
              </ul>
            )}
        </section>
      </div>

      <section className="panel readme-panel">
        <div className="panel-heading"><h2>README</h2></div>
        {detail.readme
          ? (
            <>
              <pre className="readme">{detail.readme}</pre>
              {detail.readmeTruncated && <p className="fine-print">README shortened for display. Open on GitHub to read it all.</p>}
            </>
          )
          : <p className="muted">This repository has no README.</p>}
      </section>
    </>
  )
}

function MiniStat({ icon, tint, label, value }: { icon: ReactNode; tint: string; label: string; value: number }) {
  return (
    <div className="stat-card">
      <span className={`stat-icon ${tint}`} aria-hidden="true">{icon}</span>
      <span className="stat-label">{label}</span>
      <strong className="stat-value">{value.toLocaleString()}</strong>
    </div>
  )
}
