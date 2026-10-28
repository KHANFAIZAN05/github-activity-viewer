import type { ProblemDetails } from './types'

export class AuthenticationRequiredError extends Error {}

export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    credentials: 'include',
    headers: { Accept: 'application/json' },
    signal,
  })

  if (response.status === 401) {
    throw new AuthenticationRequiredError('Your GitHub session has expired. Sign in again.')
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as ProblemDetails
    throw new Error(problem.detail ?? problem.title ?? `Request failed (${response.status})`)
  }

  return (await response.json()) as T
}
