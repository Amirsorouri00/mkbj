import { Button } from '@mkbj/ui';
import { ArrowRight, Box, ShieldCheck } from 'lucide-react';
import { Link } from 'react-router-dom';

export function OverviewPage() {
  return (
    <section aria-labelledby="overview-title" className="page-stack">
      <div className="page-heading"><div><p className="eyebrow">Backoffice</p><h1 id="overview-title">Good to see you.</h1><p>Manage MKBJ commerce operations from one focused workspace.</p></div></div>
      <div className="launch-grid">
        <article className="panel launch-card"><span className="launch-icon"><Box /></span><div><h2>Catalog products</h2><p>Review product readiness, status, variants and identifiers.</p></div><Button asChild variant="secondary"><Link to="/catalog/products">Open products <ArrowRight size={16} /></Link></Button></article>
        <article className="panel launch-card launch-card--quiet"><span className="launch-icon"><ShieldCheck /></span><div><h2>Verified contract</h2><p>This slice only exposes actions supported by the current backend.</p></div></article>
      </div>
    </section>
  );
}

export function NotFoundPage() {
  return <section className="empty-state"><h1>Page not found</h1><p>The page may have moved or is not available yet.</p><Button asChild><Link to="/">Return home</Link></Button></section>;
}
