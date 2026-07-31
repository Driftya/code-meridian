export type ReconciliationState = 'Pending' | 'Reconciled' | 'Disputed'

export interface LedgerPosting {
  account: string
  subjectId: string
  summary: string
  provenance: string
  confidence: number
  reconciliation: ReconciliationState
  projectId: string
}

export interface EvidenceReference {
  id: string
  source: string
  description: string
  observedAt: string
  confidence: number
}

export interface CognitiveTransaction {
  id: string
  occurredAt: string
  actor: string
  kind: string
  summary: string
  evidence: EvidenceReference[]
  postings: LedgerPosting[]
  causalParentId?: string
  authorityReference?: string
  correctsEntryId?: string
  idempotencyKey?: string
  uncertainty: number
}

export interface JournalEntry {
  sequence: number
  appendedAt: string
  previousHash: string
  hash: string
  transaction: CognitiveTransaction
}

export interface LedgerItem {
  sequence: number
  transactionId: string
  eventKind: string
  occurredAt: string
  account: string
  subjectId: string
  summary: string
  provenance: string
  confidence: number
  reconciliation: ReconciliationState
  projectId: string
}

export interface AffectState {
  valence: number
  arousal: number
  dopamine: number
  curiosity: number
  fatigue: number
  frustration: number
  updatedAt: string
}

export interface DriveState {
  kind: string
  activation: number
  updatedAt: string
}

export interface LedgerAccountView {
  account: string
  items: LedgerItem[]
}

export interface CognitiveSnapshot {
  generatedAt: string
  isPaused: boolean
  autonomyLevel: string
  entryCount: number
  headHash: string
  isBalanced: boolean
  integrityViolations: Array<{ sequence?: number; code: string; message: string }>
  accounts: LedgerAccountView[]
  activeGoals: LedgerItem[]
  attention: LedgerItem[]
  unresolved: LedgerItem[]
  affect: AffectState
  drives: DriveState[]
}

export interface MindView {
  generatedAt: string
  isPaused: boolean
  affect: AffectState
  drives: DriveState[]
  simulations: LedgerItem[]
  candidates: LedgerItem[]
  classification: string
}

export interface CognitiveCycleResult {
  cycleId: string
  status: string
  projectId: string
}

export interface ProjectDescriptor {
  id: string
  displayName: string
  relationship: string
  mayPrepareChanges: boolean
  requiresHumanApproval: boolean
}

export interface SensorView {
  id: string
  displayName: string
  health: {
    isHealthy: boolean
    status: string
    checkedAt: string
  }
}

export interface ProviderCapabilities {
  providerId: string
  adapterVersion: string
  isAvailable: boolean
  supportsStructuredOutput: boolean
  supportsCancellation: boolean
  supportsContinuation: boolean
  isReadOnly: boolean
  roles: string[]
}

export interface ReasoningResult {
  invocationId: string
  providerId: string
  summary: string
  evidenceIds: string[]
  alternatives: string[]
  uncertainty: number
  abstained: boolean
}

export interface GovernanceView {
  isPaused: boolean
  autonomyLevel: string
  constitutionVersion: string
  principles: string[]
}

export interface SelfView {
  name: string
  purpose: string
  constitutionVersion: string
  principles: string[]
  identity: LedgerAccountView
}
