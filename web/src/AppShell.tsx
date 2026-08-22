import { NavLink, Outlet } from 'react-router-dom'
import { AppStateBanner } from './AppStateBanner'
import './AppShell.css'

/** S-48, Scenario 1: the digest, a session view and the Rules Inventory are each reachable from
 * here, regardless of which one the operator lands on or what the app's data state is. FR-39 added
 * a fourth link, Monitor (`routes/MonitorPage.tsx`'s own doc comment states why it earns its own
 * nav entry rather than a section on an existing page). */
export function AppShell() {
  return (
    <div className="app-shell">
      <header className="app-shell__header">
        <span className="app-shell__brand">AecoPostMortem</span>
        <nav className="app-shell__nav" aria-label="Surfaces">
          <NavLink to="/" end>
            Digest
          </NavLink>
          <NavLink to="/rules">Rules Inventory</NavLink>
          <NavLink to="/monitor">Monitor</NavLink>
        </nav>
      </header>

      <AppStateBanner />

      <main className="app-shell__main">
        <Outlet />
      </main>
    </div>
  )
}
