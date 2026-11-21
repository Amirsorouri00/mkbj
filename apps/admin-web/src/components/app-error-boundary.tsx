import { Button } from '@mkbj/ui';
import { AlertTriangle } from 'lucide-react';
import { Component, type ErrorInfo, type ReactNode } from 'react';

interface State { error: Error | null }

export class AppErrorBoundary extends Component<{ children: ReactNode; resetKey?: string }, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    if (import.meta.env.DEV) console.error('Unhandled admin UI error', error, info.componentStack);
  }

  componentDidUpdate(previous: Readonly<{ children: ReactNode; resetKey?: string }>) {
    if (this.state.error && previous.resetKey !== this.props.resetKey) this.setState({ error: null });
  }

  render() {
    if (!this.state.error) return this.props.children;
    return <main className="login-page"><section className="login-card" role="alert"><span className="login-icon"><AlertTriangle aria-hidden="true" /></span><p className="eyebrow">Backoffice</p><h1>Something went wrong</h1><p>The page encountered an unexpected interface error. Reload to restore a clean application state.</p><Button onClick={() => window.location.reload()}>Reload application</Button></section></main>;
  }
}
