export interface AuthUser {
  login: string
  avatarUrl?: string
}

export interface GitHubProfile {
  login: string
  name: string | null
  avatar_url: string
  html_url: string
  bio: string | null
  public_repos: number
  followers: number
  following: number
  created_at: string
}

export interface GitHubRepository {
  id: number
  name: string
  fullName: string
  description: string | null
  htmlUrl: string
  isPrivate: boolean
  language: string | null
  stars: number
  updatedAt: string
}

export interface RepositoryPage {
  items: GitHubRepository[]
  page: number
  hasMore: boolean
}

export interface RepositoryLanguage {
  name: string
  bytes: number
  percent: number
}

export interface RepositoryCommit {
  sha: string
  message: string
  author: string | null
  date: string | null
  htmlUrl: string
}

export interface RepositoryPullRequest {
  number: number
  title: string
  author: string | null
  isDraft: boolean
  createdAt: string
  htmlUrl: string
}

export interface RepositoryDetail extends GitHubRepository {
  homepage: string | null
  isFork: boolean
  isArchived: boolean
  forks: number
  watchers: number
  openIssues: number
  defaultBranch: string | null
  license: string | null
  topics: string[]
  sizeKb: number
  createdAt: string
  pushedAt: string | null
  languages: RepositoryLanguage[]
  recentCommits: RepositoryCommit[]
  openPullRequests: RepositoryPullRequest[]
  readme: string | null
  readmeTruncated: boolean
}

export interface GitHubComment {
  id: string
  kind: string
  subject: string
  repository: string
  body: string
  createdAt: string
  htmlUrl: string
}

export interface CommentPage {
  items: GitHubComment[]
  isPartial: boolean
  matchingDiscussions: number
  coverageNote: string
}

export interface YearActivity {
  year: number
  commits: number
  issues: number
  pullRequests: number
  reviews: number
  publicEventCount: number
}

export interface ActivityResponse {
  years: YearActivity[]
  publicEventNote: string
}

export interface ProblemDetails {
  title?: string
  detail?: string
}
