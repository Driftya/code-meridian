import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { evolutionApi } from '../api'
import {
  AccountPanel,
  LoadingState,
  PageTitle,
  QueryError,
  StatusPill,
  formatTime,
} from '../components/ProjectionComponents'

export function ReasoningPage() {
  const providers = useQuery({
    queryKey: ['providers'],
    queryFn: evolutionApi.getProviders,
  })
  const [goal, setGoal] = useState('')
  const invoke = useMutation({
    mutationFn: () => evolutionApi.invokeReasoning(goal, []),
  })

  if (providers.isPending) return <LoadingState />
  if (providers.error) return <QueryError error={providers.error} />

  return (
    <>
      <PageTitle
        eyebrow="Reasoning providers"
        title="Temporary workers, durable identity"
        description="Capability probes govern routing. Provider output is bounded evidence and never owns memory, authority, or identity."
      />
      <div className="provider-grid">
        {providers.data.map((provider) => (
          <article className="provider-card" key={provider.providerId}>
            <div className="provider-card__top">
              <div>
                <span>{provider.adapterVersion}</span>
                <h2>{provider.providerId}</h2>
              </div>
              <StatusPill active={provider.isAvailable} label="Available" />
            </div>
            <div className="tag-list">
              {provider.roles.map((role) => (
                <span key={role}>{role}</span>
              ))}
            </div>
            <dl>
              <div>
                <dt>Permissions</dt>
                <dd>{provider.isReadOnly ? 'Read-only' : 'Workspace write'}</dd>
              </div>
              <div>
                <dt>Structured output</dt>
                <dd>{provider.supportsStructuredOutput ? 'Yes' : 'No'}</dd>
              </div>
            </dl>
          </article>
        ))}
      </div>
      <form
        className="panel reasoning-form"
        onSubmit={(event) => {
          event.preventDefault()
          invoke.mutate()
        }}
      >
        <label>
          Run a deterministic critic fixture
          <textarea
            onChange={(event) => setGoal(event.target.value)}
            placeholder="Ask a bounded question. No action will be executed."
            required
            rows={3}
            value={goal}
          />
        </label>
        <button disabled={invoke.isPending} type="submit">
          Invoke read-only provider
        </button>
        {invoke.data && (
          <blockquote>
            <span>
              {invoke.data.abstained ? 'Abstained' : 'Completed'} · uncertainty{' '}
              {Math.round(invoke.data.uncertainty * 100)}%
            </span>
            {invoke.data.summary}
          </blockquote>
        )}
      </form>
    </>
  )
}

export function EvolutionPage() {
  const research = useQuery({
    queryKey: ['account', 'Research'],
    queryFn: () => evolutionApi.getAccount('Research'),
  })
  const skills = useQuery({
    queryKey: ['account', 'Skill'],
    queryFn: () => evolutionApi.getAccount('Skill'),
  })
  const actions = useQuery({
    queryKey: ['account', 'Action'],
    queryFn: () => evolutionApi.getAccount('Action'),
  })

  if (research.isPending || skills.isPending || actions.isPending) {
    return <LoadingState />
  }
  if (research.error) return <QueryError error={research.error} />
  if (skills.error) return <QueryError error={skills.error} />
  if (actions.error) return <QueryError error={actions.error} />

  return (
    <>
      <PageTitle
        eyebrow="Skills & evolution"
        title="Learning with receipts"
        description="Signals, candidate actions, skills, and outcomes remain versioned ledger projections. No raw observation silently changes policy."
      />
      <div className="three-column">
        <AccountPanel account={research.data} title="Signals & evidence" />
        <AccountPanel account={actions.data} title="Candidate actions" />
        <AccountPanel account={skills.data} title="Versioned skills" />
      </div>
      <section className="policy-note policy-note--warning">
        <span>Approval gate</span>
        <p>
          This build can observe and recommend. Repository writes, publication, deployment,
          policy replacement, and model-weight updates are intentionally unavailable.
        </p>
      </section>
    </>
  )
}

export function DialoguePage() {
  const journal = useQuery({
    queryKey: ['journal'],
    queryFn: evolutionApi.getJournal,
  })
  const [question, setQuestion] = useState('')
  const ask = useMutation({
    mutationFn: () =>
      evolutionApi.invokeReasoning(
        question,
        (journal.data ?? []).slice(-5).map((entry) => `journal:${entry.sequence}`),
      ),
  })

  if (journal.isPending) return <LoadingState />
  if (journal.error) return <QueryError error={journal.error} />

  return (
    <>
      <PageTitle
        eyebrow="Dialogue & interpretation"
        title="Question the projection"
        description="Ask what changed, what remains uncertain, or what evidence could change a belief. Responses cite bounded ledger context."
      />
      <div className="dialogue-shell">
        <div className="dialogue-context">
          <span>Recent evidence window</span>
          {journal.data.slice(-5).map((entry) => (
            <article key={entry.sequence}>
              <strong>#{entry.sequence}</strong>
              <p>{entry.transaction.summary}</p>
              <small>{formatTime(entry.appendedAt)}</small>
            </article>
          ))}
        </div>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            ask.mutate()
          }}
        >
          <label>
            Your interpretation or question
            <textarea
              onChange={(event) => setQuestion(event.target.value)}
              placeholder="What are you uncertain about?"
              required
              rows={5}
              value={question}
            />
          </label>
          <button disabled={ask.isPending} type="submit">
            Ask bounded critic
          </button>
          {ask.data && (
            <div className="dialogue-response">
              <span>{ask.data.providerId} · durable summary</span>
              <p>{ask.data.summary}</p>
              <ul>
                {ask.data.alternatives.map((alternative) => (
                  <li key={alternative}>{alternative}</li>
                ))}
              </ul>
            </div>
          )}
        </form>
      </div>
    </>
  )
}

export function GovernancePage() {
  const queryClient = useQueryClient()
  const [apiKey, setApiKey] = useState(
    () => globalThis.localStorage?.getItem('evolution-api-key') ?? '',
  )
  const [keySaved, setKeySaved] = useState(false)
  const governance = useQuery({
    queryKey: ['governance'],
    queryFn: evolutionApi.getGovernance,
  })
  const setPaused = useMutation({
    mutationFn: (isPaused: boolean) =>
      evolutionApi.setPaused(
        isPaused,
        'human:operator',
        isPaused ? 'Operator requested global pause.' : 'Operator authorized recovery.',
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries()
    },
  })

  if (governance.isPending) return <LoadingState />
  if (governance.error) return <QueryError error={governance.error} />

  const saveApiKey = () => {
    if (apiKey) {
      globalThis.localStorage?.setItem('evolution-api-key', apiKey)
    } else {
      globalThis.localStorage?.removeItem('evolution-api-key')
    }

    setKeySaved(true)
  }

  return (
    <>
      <PageTitle
        eyebrow="Governance & audit"
        title="Human authority stays outside the model"
        description="Pause, correction, approvals, claims, and reversal paths are explicit and journaled."
      />
      <section className="governance-hero">
        <div>
          <StatusPill
            active={!governance.data.isPaused}
            label={governance.data.isPaused ? 'Globally paused' : 'Operational'}
          />
          <h2>Autonomy level: {governance.data.autonomyLevel}</h2>
          <p>Constitution {governance.data.constitutionVersion}</p>
        </div>
        <button
          className={governance.data.isPaused ? '' : 'danger-button'}
          disabled={setPaused.isPending}
          onClick={() => setPaused.mutate(!governance.data.isPaused)}
          type="button"
        >
          {governance.data.isPaused ? 'Authorize resume' : 'Pause immediately'}
        </button>
      </section>
      <form
        className="inline-form"
        onSubmit={(event) => {
          event.preventDefault()
          saveApiKey()
        }}
      >
        <label>
          Operator mutation key
          <input
            autoComplete="current-password"
            onChange={(event) => {
              setApiKey(event.target.value)
              setKeySaved(false)
            }}
            placeholder="Required by the Compose API"
            type="password"
            value={apiKey}
          />
        </label>
        <button type="submit">{keySaved ? 'Key saved' : 'Save in this browser'}</button>
      </form>
      {setPaused.error && <QueryError error={setPaused.error} />}
      <section className="panel">
        <div className="panel__heading">
          <div>
            <span>Immutable product boundary</span>
            <h2>Constitutional principles</h2>
          </div>
        </div>
        <ol className="principle-list">
          {governance.data.principles.map((principle) => (
            <li key={principle}>{principle}</li>
          ))}
        </ol>
      </section>
    </>
  )
}
