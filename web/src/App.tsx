import { Route, Routes } from 'react-router-dom'
import { AppShell } from './AppShell'
import { DigestPage } from './routes/DigestPage'
import { RulesInventoryPage } from './routes/RulesInventoryPage'
import { SessionPage } from './routes/SessionPage'

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
      </Route>
    </Routes>
  )
}
