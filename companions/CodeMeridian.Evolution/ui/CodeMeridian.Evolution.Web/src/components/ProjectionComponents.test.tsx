import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { LedgerList, MetricCard } from './ProjectionComponents'
import type { LedgerItem } from '../types'

describe('ProjectionComponents', () => {
  it('renders an operational metric', () => {
    render(<MetricCard label="Journal entries" value={7} detail="append-only" tone="good" />)

    expect(screen.getByText('Journal entries')).toBeInTheDocument()
    expect(screen.getByText('7')).toBeInTheDocument()
  })

  it('renders the latest ledger evidence', () => {
    const items: LedgerItem[] = [{
      sequence: 2,
      transactionId: '21b88f65-ae49-42b5-93ac-f7353f3fdcc5',
      eventKind: 'Observation',
      occurredAt: '2026-07-28T12:00:00Z',
      account: 'Memory',
      subjectId: 'observation:1',
      summary: 'A bounded observation.',
      provenance: 'sensor:test',
      confidence: 0.9,
      reconciliation: 'Pending',
      projectId: 'meridian-evolution',
    }]

    render(<LedgerList items={items} />)

    expect(screen.getByText('A bounded observation.')).toBeInTheDocument()
    expect(screen.getByText('Pending')).toBeInTheDocument()
  })
})
