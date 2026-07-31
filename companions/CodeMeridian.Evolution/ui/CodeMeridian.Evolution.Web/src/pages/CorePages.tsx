import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { evolutionApi } from '../api'
import {
  AccountPanel,
  LedgerList,
  LoadingState,
  MetricCard,
  PageTitle,
  QueryError,
  StatusPill,
  formatTime,
  shortHash,
} from '../components/ProjectionComponents'

export function NowPage() {
  const snapshot = useQuery({
    queryKey: ['snapshot'],
    queryFn: evolutionApi.getSnapshot,
  })

  if (snapshot.isPending) return <LoadingState />
  if (snapshot.error) return <QueryError error={snapshot.error} />

  return (
    <>
      <PageTitle
        eyebrow="Live projection"
        title="What is happening now?"
        description="A rebuildable view of attention, goals, uncertainty, and integrity. This is recorded functional state—not hidden thought."
      />
      <div className="metrics-grid">
        <MetricCard
          label="Journal"
          value={snapshot.data.entryCount}
          detail={`Head ${shortHash(snapshot.data.headHash)}`}
          tone={snapshot.data.isBalanced ? 'good' : 'warning'}
        />
        <MetricCard
          label="Active goals"
          value={snapshot.data.activeGoals.length}
          detail="Human-authorized only"
        />
        <MetricCard
          label="Unresolved"
          value={snapshot.data.unresolved.length}
          detail="Pending or disputed"
          tone={snapshot.data.unresolved.length > 0 ? 'warning' : 'good'}
        />
        <MetricCard
          label="Autonomy"
          value={snapshot.data.autonomyLevel}
          detail={snapshot.data.isPaused ? 'Globally paused' : 'Governed and active'}
        />
      </div>
      <div className="two-column">
        <section className="panel panel--focus">
          <div className="panel__heading">
            <div>
              <span>Global workspace</span>
              <h2>Attention</h2>
            </div>
            <StatusPill
              active={!snapshot.data.isPaused}
              label={snapshot.data.isPaused ? 'Paused' : 'Observing'}
            />
          </div>
          <LedgerList
            items={snapshot.data.attention}
            empty="Nothing currently requires elevated attention."
          />
        </section>
        <section className="panel">
          <div className="panel__heading">
            <div>
              <span>Commitment horizon</span>
              <h2>Active goals</h2>
            </div>
            <strong>{snapshot.data.activeGoals.length}</strong>
          </div>
          <LedgerList
            items={snapshot.data.activeGoals}
            empty="No active goal has been authorized."
          />
        </section>
      </div>
    </>
  )
}

export function IdentityPage() {
  const self = useQuery({ queryKey: ['self'], queryFn: evolutionApi.getSelf })

  if (self.isPending) return <LoadingState />
  if (self.error) return <QueryError error={self.error} />

  return (
    <>
      <PageTitle
        eyebrow="Identity & self-model"
        title={self.data.name}
        description={self.data.purpose}
      />
      <section className="identity-card">
        <div className="identity-mark">ME</div>
        <div>
          <span>Constitution {self.data.constitutionVersion}</span>
          <h2>Functional continuity without a consciousness claim</h2>
          <p>
            Identity is reconstructed from authorized declarations and ledger evidence.
            Provider sessions are temporary workers, never canonical selves.
          </p>
        </div>
      </section>
      <div className="two-column">
        <section className="panel">
          <div className="panel__heading">
            <div>
              <span>Protected kernel</span>
              <h2>Constitution</h2>
            </div>
          </div>
          <ol className="principle-list">
            {self.data.principles.map((principle) => (
              <li key={principle}>{principle}</li>
            ))}
          </ol>
        </section>
        <AccountPanel account={self.data.identity} title="Identity evidence" />
      </div>
    </>
  )
}

export function LedgerPage() {
  const queryClient = useQueryClient()
  const journal = useQuery({ queryKey: ['journal'], queryFn: evolutionApi.getJournal })
  const snapshot = useQuery({
    queryKey: ['snapshot'],
    queryFn: evolutionApi.getSnapshot,
  })
  const [selectedSequence, setSelectedSequence] = useState<number>()
  const [correction, setCorrection] = useState('')
  const challenge = useMutation({
    mutationFn: () =>
      evolutionApi.challengeEntry(selectedSequence!, 'human:operator', correction),
    onSuccess: async () => {
      setCorrection('')
      await queryClient.invalidateQueries()
    },
  })

  if (journal.isPending || snapshot.isPending) return <LoadingState />
  if (journal.error) return <QueryError error={journal.error} />
  if (snapshot.error) return <QueryError error={snapshot.error} />

  return (
    <>
      <PageTitle
        eyebrow="Cognitive ledger"
        title="Immutable history, adjustable meaning"
        description="Every current projection traces to a hash-chained transaction. Corrections create adjusting entries; history is never rewritten."
      />
      <div className="ledger-banner">
        <StatusPill
          active={snapshot.data.isBalanced}
          label={snapshot.data.isBalanced ? 'Trial balance clean' : 'Integrity warning'}
        />
        <code>{shortHash(snapshot.data.headHash)}</code>
        <span>{snapshot.data.entryCount} journal entries</span>
      </div>
      <section className="journal-timeline">
        {journal.data
          .slice()
          .reverse()
          .map((entry) => (
            <button
              className={`journal-entry ${
                selectedSequence === entry.sequence ? 'journal-entry--selected' : ''
              }`}
              key={entry.sequence}
              onClick={() => setSelectedSequence(entry.sequence)}
              type="button"
            >
              <span className="journal-entry__rail">
                <strong>{entry.sequence}</strong>
                <i />
              </span>
              <span className="journal-entry__content">
                <small>
                  {entry.transaction.kind} · {formatTime(entry.appendedAt)}
                </small>
                <strong>{entry.transaction.summary}</strong>
                <em>{entry.transaction.actor}</em>
              </span>
              <code>{shortHash(entry.hash)}</code>
            </button>
          ))}
      </section>
      {selectedSequence && (
        <form
          className="inline-form"
          onSubmit={(event) => {
            event.preventDefault()
            challenge.mutate()
          }}
        >
          <label>
            Challenge entry #{selectedSequence}
            <input
              onChange={(event) => setCorrection(event.target.value)}
              placeholder="Explain the correction or disagreement"
              required
              value={correction}
            />
          </label>
          <button disabled={challenge.isPending} type="submit">
            Record adjusting entry
          </button>
        </form>
      )}
    </>
  )
}
