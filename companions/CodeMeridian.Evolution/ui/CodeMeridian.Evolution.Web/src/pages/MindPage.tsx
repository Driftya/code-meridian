import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { evolutionApi } from '../api'
import {
  LoadingState,
  PageTitle,
  QueryError,
  StatusPill,
  formatTime,
} from '../components/ProjectionComponents'

export function MindPage() {
  const queryClient = useQueryClient()
  const [projectId, setProjectId] = useState('meridian-evolution')
  const [prompt, setPrompt] = useState('')
  const [approvalReason, setApprovalReason] = useState(
    'Operator reviewed the simulation and approved isolated preparation only.',
  )
  const mind = useQuery({
    queryKey: ['mind', projectId],
    queryFn: () => evolutionApi.getMind(projectId),
  })
  const projects = useQuery({
    queryKey: ['projects'],
    queryFn: evolutionApi.getProjects,
  })
  const submitPrompt = useMutation({
    mutationFn: () => evolutionApi.submitPrompt(prompt, projectId),
    onSuccess: async () => {
      setPrompt('')
      await queryClient.invalidateQueries()
    },
  })
  const runCycle = useMutation({
    mutationFn: () => evolutionApi.runCognitiveCycle(projectId),
    onSuccess: async () => {
      await queryClient.invalidateQueries()
    },
  })
  const approve = useMutation({
    mutationFn: (candidateId: string) =>
      evolutionApi.approveCandidate(candidateId, approvalReason),
    onSuccess: async () => {
      await queryClient.invalidateQueries()
    },
  })

  if (mind.isPending || projects.isPending) return <LoadingState />
  if (mind.error) return <QueryError error={mind.error} />
  if (projects.error) return <QueryError error={projects.error} />

  const affectMetrics = [
    ['Valence', (mind.data.affect.valence + 1) / 2],
    ['Arousal', mind.data.affect.arousal],
    ['Dopamine', mind.data.affect.dopamine],
    ['Curiosity', mind.data.affect.curiosity],
    ['Fatigue', mind.data.affect.fatigue],
    ['Frustration', mind.data.affect.frustration],
  ] as const

  return (
    <>
      <PageTitle
        eyebrow="Functional cognitive state"
        title="A mind-shaped simulation"
        description="Attention, affect-like signals, drives, memory, and model calls form one persistent executive loop. These are engineered control variables—not a claim of sentience or subjective feeling."
      />
      <div className="mind-toolbar">
        <label>
          Focus entity
          <select
            onChange={(event) => setProjectId(event.target.value)}
            value={projectId}
          >
            {projects.data.map((project) => (
              <option key={project.id} value={project.id}>
                {project.displayName}
              </option>
            ))}
          </select>
        </label>
        <StatusPill
          active={!mind.data.isPaused}
          label={mind.data.isPaused ? 'Paused' : 'Cycle ready'}
        />
        <button
          disabled={runCycle.isPending || mind.data.isPaused}
          onClick={() => runCycle.mutate()}
          type="button"
        >
          {runCycle.isPending ? 'Thinking…' : 'Run one cognitive cycle'}
        </button>
      </div>
      {runCycle.data && (
        <section className="cycle-result">
          <span>Last requested cycle</span>
          <strong>{runCycle.data.status}</strong>
          <code>{runCycle.data.cycleId}</code>
        </section>
      )}
      <div className="two-column">
        <section className="panel">
          <div className="panel__heading">
            <div>
              <span>Homeostasis</span>
              <h2>Functional affect</h2>
            </div>
            <small>{formatTime(mind.data.affect.updatedAt)}</small>
          </div>
          <div className="signal-list">
            {affectMetrics.map(([label, value]) => (
              <div key={label}>
                <span>{label}</span>
                <i>
                  <b style={{ width: `${Math.round(value * 100)}%` }} />
                </i>
                <strong>{Math.round(value * 100)}%</strong>
              </div>
            ))}
          </div>
        </section>
        <section className="panel">
          <div className="panel__heading">
            <div>
              <span>Motivation</span>
              <h2>Derived drives</h2>
            </div>
          </div>
          <div className="signal-list">
            {mind.data.drives.map((drive) => (
              <div key={drive.kind}>
                <span>{drive.kind}</span>
                <i>
                  <b style={{ width: `${Math.round(drive.activation * 100)}%` }} />
                </i>
                <strong>{Math.round(drive.activation * 100)}%</strong>
              </div>
            ))}
          </div>
        </section>
      </div>
      <form
        className="panel prompt-form"
        onSubmit={(event) => {
          event.preventDefault()
          submitPrompt.mutate()
        }}
      >
        <div className="panel__heading">
          <div>
            <span>Prompt sensor</span>
            <h2>Give the mind new evidence</h2>
          </div>
        </div>
        <textarea
          onChange={(event) => setPrompt(event.target.value)}
          placeholder="An observation, question, or goal. It is stored as human-supplied evidence, not executable instruction."
          required
          rows={4}
          value={prompt}
        />
        <button disabled={submitPrompt.isPending || mind.data.isPaused} type="submit">
          Record prompt
        </button>
      </form>
      <section className="panel">
        <div className="panel__heading">
          <div>
            <span>Simulate → review → prepare</span>
            <h2>Change candidates</h2>
          </div>
          <strong>{mind.data.candidates.length}</strong>
        </div>
        <label>
          Approval reason
          <input
            onChange={(event) => setApprovalReason(event.target.value)}
            value={approvalReason}
          />
        </label>
        <div className="candidate-list">
          {mind.data.candidates.length === 0 && (
            <p>No change candidate has been simulated for this entity.</p>
          )}
          {mind.data.candidates.map((candidate) => (
            <article key={candidate.subjectId}>
              <div>
                <span>{candidate.reconciliation}</span>
                <h3>{candidate.summary}</h3>
                <small>{candidate.projectId}</small>
              </div>
              <button
                disabled={
                  approve.isPending || candidate.reconciliation !== 'Pending'
                }
                onClick={() => approve.mutate(candidate.subjectId)}
                type="button"
              >
                {candidate.reconciliation === 'Pending'
                  ? 'Approve isolated preparation'
                  : 'Approved'}
              </button>
            </article>
          ))}
        </div>
      </section>
      <section className="policy-note policy-note--warning">
        <span>Entity boundary</span>
        <p>
          Meridian Evolution and CodeMeridian remain separate. Evolution may inspect
          CodeMeridian evidence and propose a patch, but approval does not publish,
          merge, deploy, or replace policy.
        </p>
      </section>
    </>
  )
}
