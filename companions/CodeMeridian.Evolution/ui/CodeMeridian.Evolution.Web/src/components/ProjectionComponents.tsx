import type { LedgerAccountView, LedgerItem } from '../types'

export function PageTitle({
  eyebrow,
  title,
  description,
}: {
  eyebrow: string
  title: string
  description: string
}) {
  return (
    <header className="page-title">
      <span>{eyebrow}</span>
      <h1>{title}</h1>
      <p>{description}</p>
    </header>
  )
}

export function MetricCard({
  label,
  value,
  detail,
  tone = 'neutral',
}: {
  label: string
  value: string | number
  detail: string
  tone?: 'neutral' | 'good' | 'warning'
}) {
  return (
    <article className={`metric-card metric-card--${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </article>
  )
}

export function StatusPill({
  label,
  active,
}: {
  label: string
  active: boolean
}) {
  return (
    <span className={`status-pill ${active ? 'status-pill--active' : ''}`}>
      <i />
      {label}
    </span>
  )
}

export function LedgerList({
  items,
  empty = 'No ledger items in this projection.',
  onSelect,
}: {
  items: LedgerItem[]
  empty?: string
  onSelect?: (item: LedgerItem) => void
}) {
  if (items.length === 0) {
    return <div className="empty-state">{empty}</div>
  }

  return (
    <div className="ledger-list">
      {items.map((item) => (
        <button
          className="ledger-row"
          key={`${item.account}:${item.subjectId}`}
          onClick={() => onSelect?.(item)}
          type="button"
        >
          <span className="ledger-row__sequence">#{item.sequence}</span>
          <span className="ledger-row__body">
            <strong>{item.summary}</strong>
            <small>
              {item.account} · {item.eventKind} · {formatTime(item.occurredAt)}
            </small>
          </span>
          <span
            className={`reconciliation reconciliation--${item.reconciliation.toLowerCase()}`}
          >
            {item.reconciliation}
          </span>
        </button>
      ))}
    </div>
  )
}

export function AccountPanel({
  account,
  title,
}: {
  account?: LedgerAccountView
  title?: string
}) {
  return (
    <section className="panel">
      <div className="panel__heading">
        <div>
          <span>Ledger account</span>
          <h2>{title ?? account?.account ?? 'Account'}</h2>
        </div>
        <strong>{account?.items.length ?? 0}</strong>
      </div>
      <LedgerList items={account?.items ?? []} />
    </section>
  )
}

export function QueryError({ error }: { error: Error }) {
  return (
    <div className="error-state">
      <strong>Projection unavailable</strong>
      <p>{error.message}</p>
    </div>
  )
}

export function LoadingState() {
  return (
    <div className="loading-state">
      <i />
      Rebuilding projection from the ledger…
    </div>
  )
}

export function formatTime(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

export function shortHash(value: string) {
  return value ? `${value.slice(0, 8)}…${value.slice(-6)}` : 'empty'
}
