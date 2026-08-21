import { NavLink, Outlet } from 'react-router-dom'
import { AppStateBanner } from './AppStateBanner'
import './AppShell.css'

/** S-48, Scenario 1: the digest, a session view and the Rules Inventory are each reachable from
 * here, regardless of which one the operator lands on or what the app's data state is. */
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
        </nav>
      </header>

      <AppStateBanner />

      <main className="app-shell__main">
        <Outlet />
      </main>
    </div>
  )
}
