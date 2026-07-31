import type {
  CognitiveCycleResult,
  CognitiveSnapshot,
  GovernanceView,
  JournalEntry,
  LedgerAccountView,
  MindView,
  ProjectDescriptor,
  ProviderCapabilities,
  ReasoningResult,
  SelfView,
  SensorView,
} from './types'

const apiBase = import.meta.env.VITE_API_BASE_URL ?? ''

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  const apiKey = globalThis.localStorage?.getItem('evolution-api-key')

  if (init?.body) {
    headers.set('Content-Type', 'application/json')
  }

  if (apiKey) {
    headers.set('X-Evolution-Key', apiKey)
  }

  const response = await fetch(`${apiBase}${path}`, { ...init, headers })

  if (!response.ok) {
    const detail = await response.text()
    throw new Error(detail || `Request failed with status ${response.status}.`)
  }

  return response.json() as Promise<T>
}

export const evolutionApi = {
  getSnapshot: () => request<CognitiveSnapshot>('/api/now'),
  getMind: (projectId?: string) =>
    request<MindView>(
      `/api/mind${projectId ? `?projectId=${encodeURIComponent(projectId)}` : ''}`,
    ),
  getProjects: () => request<ProjectDescriptor[]>('/api/projects'),
  getJournal: () => request<JournalEntry[]>('/api/ledger/journal'),
  getAccount: (account: string) =>
    request<LedgerAccountView>(`/api/ledger/accounts/${account}`),
  getSensors: () => request<SensorView[]>('/api/sensors'),
  getProviders: () => request<ProviderCapabilities[]>('/api/reasoning/providers'),
  getGovernance: () => request<GovernanceView>('/api/governance'),
  getSelf: () => request<SelfView>('/api/self'),
  runSensor: (sensorId: string) =>
    request(`/api/sensors/${sensorId}/run`, { method: 'POST' }),
  submitPrompt: (text: string, projectId: string) =>
    request('/api/perception/prompts', {
      method: 'POST',
      body: JSON.stringify({
        text,
        actor: 'human:operator',
        projectId,
        idempotencyKey: `prompt:${crypto.randomUUID()}`,
      }),
    }),
  runCognitiveCycle: (projectId: string, providerId = 'fake') =>
    request<CognitiveCycleResult>('/api/mind/cycles', {
      method: 'POST',
      body: JSON.stringify({
        providerId,
        role: 'researcher',
        projectId,
        goal: null,
        maximumAttentionItems: 8,
        force: false,
      }),
    }),
  approveCandidate: (candidateId: string, reason: string) =>
    request(`/api/candidates/${encodeURIComponent(candidateId)}/approve`, {
      method: 'POST',
      body: JSON.stringify({
        actor: 'human:operator',
        reason,
        idempotencyKey: `approval:${crypto.randomUUID()}`,
      }),
    }),
  createGoal: (input: {
    id: string
    title: string
    actor: string
    successCriteria: string
    deadline?: string
    budget: number
    idempotencyKey: string
  }) => request('/api/goals', { method: 'POST', body: JSON.stringify(input) }),
  setPaused: (isPaused: boolean, actor: string, reason: string) =>
    request(`/api/governance/${isPaused ? 'pause' : 'resume'}`, {
      method: 'POST',
      body: JSON.stringify({
        actor,
        reason,
        idempotencyKey: `governance:${crypto.randomUUID()}`,
      }),
    }),
  challengeEntry: (sequence: number, actor: string, summary: string) =>
    request(`/api/ledger/entries/${sequence}/challenge`, {
      method: 'POST',
      body: JSON.stringify({
        actor,
        summary,
        confidence: 1,
        idempotencyKey: `challenge:${sequence}:${crypto.randomUUID()}`,
      }),
    }),
  invokeReasoning: (goal: string, evidenceIds: string[]) =>
    request<ReasoningResult>('/api/reasoning/invocations', {
      method: 'POST',
      body: JSON.stringify({
        invocationId: crypto.randomUUID(),
        providerId: 'fake',
        role: 'critic',
        goal,
        evidenceIds,
        maximumOutputTokens: 800,
        timeout: '00:00:10',
        idempotencyKey: `reasoning:${crypto.randomUUID()}`,
      }),
    }),
}
