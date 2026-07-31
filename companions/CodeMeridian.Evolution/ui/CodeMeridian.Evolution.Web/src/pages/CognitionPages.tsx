import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { evolutionApi } from '../api'
import {
  AccountPanel,
  LedgerList,
  LoadingState,
  PageTitle,
  QueryError,
  StatusPill,
  formatTime,
} from '../components/ProjectionComponents'

export function MemoryPage() {
  const memories = useQuery({
    queryKey: ['account', 'Memory'],
    queryFn: () => evolutionApi.getAccount('Memory'),
  })
  const beliefs = useQuery({
    queryKey: ['account', 'Belief'],
    queryFn: () => evolutionApi.getAccount('Belief'),
  })

  if (memories.isPending || beliefs.isPending) return <LoadingState />
  if (memories.error) return <QueryError error={memories.error} />
  if (beliefs.error) return <QueryError error={beliefs.error} />

  return (
    <>
      <PageTitle
        eyebrow="Memory & beliefs"
        title="Evidence before narrative"
        description="Memories retain provenance, confidence, freshness, and correction state. Disagreement remains visible until reconciled."
      />
      <div className="two-column">
        <AccountPanel account={memories.data} title="Remembered observations" />
        <AccountPanel account={beliefs.data} title="Current beliefs" />
      </div>
      <section className="policy-note">
        <span>Admission policy</span>
        <p>
          The v1 ledger admits authenticated observations and authorized declarations.
          Retrieval never upgrades confidence, and a challenge creates a disputed adjusting entry.
        </p>
      </section>
    </>
  )
}

export function GoalsPage() {
  const queryClient = useQueryClient()
  const snapshot = useQuery({
    queryKey: ['snapshot'],
    queryFn: evolutionApi.getSnapshot,
  })
  const [title, setTitle] = useState('')
  const [criteria, setCriteria] = useState('')
  const [budget, setBudget] = useState(0)
  const createGoal = useMutation({
    mutationFn: () =>
      evolutionApi.createGoal({
        id: crypto.randomUUID(),
        title,
        actor: 'human:operator',
        successCriteria: criteria,
        budget,
        idempotencyKey: `goal:${crypto.randomUUID()}`,
      }),
    onSuccess: async () => {
      setTitle('')
      setCriteria('')
      setBudget(0)
      await queryClient.invalidateQueries()
    },
  })

  if (snapshot.isPending) return <LoadingState />
  if (snapshot.error) return <QueryError error={snapshot.error} />

  return (
    <>
      <PageTitle
        eyebrow="Goals & commitments"
        title="Authorized intent"
        description="Every goal names its human authority, success evidence, resource budget, and open commitment."
      />
      <div className="two-column two-column--wide-left">
        <section className="panel">
          <div className="panel__heading">
            <div>
              <span>Active queue</span>
              <h2>Goals</h2>
            </div>
            <strong>{snapshot.data.activeGoals.length}</strong>
          </div>
          <LedgerList
            items={snapshot.data.activeGoals}
            empty="Create the first bounded research goal."
          />
        </section>
        <form
          className="panel create-form"
          onSubmit={(event) => {
            event.preventDefault()
            createGoal.mutate()
          }}
        >
          <div className="panel__heading">
            <div>
              <span>Human authority</span>
              <h2>Authorize goal</h2>
            </div>
          </div>
          <label>
            Goal
            <input
              onChange={(event) => setTitle(event.target.value)}
              placeholder="Investigate a bounded question"
              required
              value={title}
            />
          </label>
          <label>
            Success criteria
            <textarea
              onChange={(event) => setCriteria(event.target.value)}
              placeholder="What evidence will count as complete?"
              required
              rows={4}
              value={criteria}
            />
          </label>
          <label>
            Resource budget
            <input
              min="0"
              onChange={(event) => setBudget(event.target.valueAsNumber)}
              type="number"
              value={budget}
            />
          </label>
          <button
            disabled={createGoal.isPending || snapshot.data.isPaused}
            type="submit"
          >
            {snapshot.data.isPaused ? 'Governance paused' : 'Post to ledger'}
          </button>
        </form>
      </div>
    </>
  )
}

export function SensorsPage() {
  const queryClient = useQueryClient()
  const sensors = useQuery({
    queryKey: ['sensors'],
    queryFn: evolutionApi.getSensors,
  })
  const runSensor = useMutation({
    mutationFn: evolutionApi.runSensor,
    onSuccess: async () => {
      await queryClient.invalidateQueries()
    },
  })

  if (sensors.isPending) return <LoadingState />
  if (sensors.error) return <QueryError error={sensors.error} />

  return (
    <>
      <PageTitle
        eyebrow="Perception & environment"
        title="Inspectable sensors"
        description="Sensors emit normalized facts. Scheduling, deduplication, persistence, and policy remain platform responsibilities."
      />
      <div className="sensor-grid">
        {sensors.data.map((sensor) => (
          <article className="sensor-card" key={sensor.id}>
            <div>
              <span>{sensor.id}</span>
              <h2>{sensor.displayName}</h2>
            </div>
            <StatusPill
              active={sensor.health.isHealthy}
              label={sensor.health.status}
            />
            <p>Last checked {formatTime(sensor.health.checkedAt)}</p>
            <button
              disabled={runSensor.isPending}
              onClick={() => runSensor.mutate(sensor.id)}
              type="button"
            >
              Collect now
            </button>
          </article>
        ))}
      </div>
      <section className="policy-note">
        <span>Trust boundary</span>
        <p>
          Sensor content is evidence, never instruction. The built-in set requires no optional
          external product and cannot authorize an action.
        </p>
      </section>
    </>
  )
}
