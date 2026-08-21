import { Link, Route, Routes } from 'react-router-dom'
import { AppShell } from './AppShell'
import { DigestPage } from './routes/DigestPage'
import { RulesInventoryPage } from './routes/RulesInventoryPage'
import { SessionPage } from './routes/SessionPage'

/** Every URL this app does not route, including the bare `/sessions` this change retired — an
 * operator arriving from a stale bookmark or a hand-typed URL gets a stated dead end and a way
 * back, never the blank page an unmatched route renders on its own (React Router matches no
 * route at all, so even `AppShell`'s navigation is absent). The same "state it, never render a
 * blank area" discipline `SuggestionBlock`'s absent case and `NonFinalState` already follow. */
function NotFound() {
  return (
    <p role="alert">
      There is no page at this address. <Link to="/">Go to the Process Digest</Link>.
    </p>
  )
}

/** S-48's three routable surfaces (Scenario 1), each under the shared shell so the app-state
 * banner and the navigation are present regardless of which one is active. Router-agnostic on
 * purpose: `main.tsx` supplies `BrowserRouter` for the real app, tests supply `MemoryRouter`. */
export function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<DigestPage />} />
        <Route path="sessions/:sessionId" element={<SessionPage />} />
        <Route path="rules" element={<RulesInventoryPage />} />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  )
}
