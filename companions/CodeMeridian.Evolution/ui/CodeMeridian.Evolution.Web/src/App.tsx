import {
  Link,
  Outlet,
  RouterProvider,
  createRootRoute,
  createRoute,
  createRouter,
} from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { evolutionApi } from './api'
import { StatusPill, shortHash } from './components/ProjectionComponents'
import { GoalsPage, MemoryPage, SensorsPage } from './pages/CognitionPages'
import { IdentityPage, LedgerPage, NowPage } from './pages/CorePages'
import { MindPage } from './pages/MindPage'
import {
  DialoguePage,
  EvolutionPage,
  GovernancePage,
  ReasoningPage,
} from './pages/EvolutionPages'

const navigation = [
  { to: '/', label: 'Now', glyph: 'N' },
  { to: '/identity', label: 'Identity', glyph: 'I' },
  { to: '/mind', label: 'Mind', glyph: 'C' },
  { to: '/ledger', label: 'Ledger', glyph: 'L' },
  { to: '/memory', label: 'Memory', glyph: 'M' },
  { to: '/goals', label: 'Goals', glyph: 'G' },
  { to: '/sensors', label: 'Sensors', glyph: 'P' },
  { to: '/reasoning', label: 'Reasoning', glyph: 'R' },
  { to: '/evolution', label: 'Evolution', glyph: 'E' },
  { to: '/dialogue', label: 'Dialogue', glyph: 'D' },
  { to: '/governance', label: 'Governance', glyph: 'A' },
] as const

function Shell() {
  const snapshot = useQuery({
    queryKey: ['snapshot'],
    queryFn: evolutionApi.getSnapshot,
  })

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand__mark">
            <span />
            <i />
          </div>
          <div>
            <strong>Meridian</strong>
            <span>Evolution</span>
          </div>
        </div>
        <nav aria-label="Primary">
          {navigation.map((item) => (
            <Link
              activeOptions={{ exact: item.to === '/' }}
              activeProps={{ className: 'nav-link nav-link--active' }}
              className="nav-link"
              key={item.to}
              to={item.to}
            >
              <i>{item.glyph}</i>
              <span>{item.label}</span>
            </Link>
          ))}
        </nav>
        <div className="sidebar__footer">
          <span>Functional projection</span>
          <p>No consciousness claim</p>
          <small>Constitution 1.1.0</small>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <div className="topbar__system">
            <StatusPill
              active={!snapshot.data?.isPaused}
              label={snapshot.data?.isPaused ? 'Paused' : 'Observing'}
            />
            <span>
              {snapshot.data?.entryCount ?? 0} entries ·{' '}
              {shortHash(snapshot.data?.headHash ?? '')}
            </span>
          </div>
          <div className="topbar__meta">
            <span>Autonomy</span>
            <strong>{snapshot.data?.autonomyLevel ?? 'Recommend'}</strong>
          </div>
        </header>
        <main>
          <Outlet />
        </main>
      </div>
    </div>
  )
}

const rootRoute = createRootRoute({ component: Shell })
const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: NowPage,
})
const identityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/identity',
  component: IdentityPage,
})
const mindRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/mind',
  component: MindPage,
})
const ledgerRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/ledger',
  component: LedgerPage,
})
const memoryRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/memory',
  component: MemoryPage,
})
const goalsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/goals',
  component: GoalsPage,
})
const sensorsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/sensors',
  component: SensorsPage,
})
const reasoningRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/reasoning',
  component: ReasoningPage,
})
const evolutionRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/evolution',
  component: EvolutionPage,
})
const dialogueRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/dialogue',
  component: DialoguePage,
})
const governanceRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/governance',
  component: GovernancePage,
})

const routeTree = rootRoute.addChildren([
  indexRoute,
  identityRoute,
  mindRoute,
  ledgerRoute,
  memoryRoute,
  goalsRoute,
  sensorsRoute,
  reasoningRoute,
  evolutionRoute,
  dialogueRoute,
  governanceRoute,
])
const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
  scrollRestoration: true,
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

export function EvolutionApp() {
  return <RouterProvider router={router} />
}
