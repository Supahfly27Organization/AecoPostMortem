import { useAppState } from './api/useAppState'
import './AppStateBanner.css'

/**
 * S-48, Scenarios 2 and 3: before the first ingest, or with no Copilot directory at all, the app
 * says so — two distinct diagnoses that must not collapse into one message. Shown above every
 * routed surface (`AppShell`) because "opens the app" names no particular route: whichever surface
 * the operator lands on, the same diagnosis applies until it is resolved.
 */
export function AppStateBanner() {
  const query = useAppState()

  if (query.status === 'loading') {
    return null
  }

  if (query.status === 'error') {
    return (
      <div className="app-state-banner app-state-banner--error" role="alert">
        <p>
          Could not reach the local API. Is <code>aecopostmortem serve</code> running?
        </p>
      </div>
    )
  }

  const { report } = query

  if (report.kind === 'ready') {
    return null
  }

  return (
    <div className="app-state-banner" role="status" data-state={report.kind}>
      <p>{report.message}</p>
      {report.fixCommand !== null && (
        <p className="app-state-banner__fix">
          Run <code>{report.fixCommand}</code>.
        </p>
      )}
    </div>
  )
}
