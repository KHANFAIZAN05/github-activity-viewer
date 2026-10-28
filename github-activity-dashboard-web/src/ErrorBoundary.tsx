import { Component, type ErrorInfo, type ReactNode } from 'react'

interface State {
  error: Error | null
}

export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('UI crashed:', error, info.componentStack)
  }

  render() {
    if (!this.state.error) return this.props.children
    return (
      <main className="center-state" role="alert" style={{ flexDirection: 'column', textAlign: 'center', padding: 24 }}>
        <h1>Something went wrong</h1>
        <p className="muted">The page hit an unexpected error. Reload to try again.</p>
        <pre className="readme" style={{ maxWidth: 640 }}>{this.state.error.message}</pre>
        <button className="button primary" onClick={() => window.location.reload()}>Reload</button>
      </main>
    )
  }
}
