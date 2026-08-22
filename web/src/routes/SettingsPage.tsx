import { useState, type ReactNode } from 'react'
import {
  postIngest,
  postRebuild,
  type IngestResultEnvelope,
  type RebuildResultEnvelope,
  type SettingsEnvelope,
} from '../api/settings'
import { notifyStoreChanged } from '../api/storeChangeEvents'
import { useSettings } from '../api/useSettings'
import { useWriteOperation, type WriteOperationState } from '../api/useWriteOperation'
import './SettingsPage.css'

/**
 * Part A + Part B of the Settings surface: the operator's currently-resolved configuration, real
 * facts only (no field here is guessed, and a store that does not exist yet says so rather than
 * rendering a bare `0 bytes` — `web/CLAUDE.md`'s "an honest empty state, never a guessed number"
 * discipline), plus this codebase's first two write actions.
 *
 * A completed write bumps `refetchToken` (re-fetches this page's own settings — the store's own size
 * just changed) and calls `notifyStoreChanged()` (refreshes the app-state banner mounted above every
 * route — see `storeChangeEvents.ts`'s own remarks for why that needs a separate signal). Digest,
 * Rules Inventory and Monitor need neither: each fetches fresh on its own mount, and navigating to
 * one of them from here already triggers that mount.
 */
export function SettingsPage() {
  const [refetchToken, setRefetchToken] = useState(0)
  const query = useSettings(refetchToken)

  const ingest = useWriteOperation(postIngest)
  const rebuild = useWriteOperation(postRebuild)

  const anyRunning = ingest.state.status === 'running' || rebuild.state.status === 'running'

  async function runIngest() {
    // Gated on a genuine success (code review, Important): a 409 conflict or a real failure must
    // never dispatch `notifyStoreChanged()` — the store did not actually change, and a stray
    // refetch fired while a *concurrent* rebuild is mid-drop could hit the real window
    // `NormalizedLayerWriter.RebuildAll`'s own transaction wrap otherwise closes.
    if ((await ingest.run()) === 'succeeded') {
      afterWrite()
    }
  }

  async function runRebuild() {
    if ((await rebuild.run()) === 'succeeded') {
      afterWrite()
    }
  }

  function afterWrite() {
    setRefetchToken((token) => token + 1)
    notifyStoreChanged()
  }

  return (
    <section className="settings-page">
      <h1>Settings</h1>

      {query.status === 'error' && (
        <p role="alert">
          Could not reach the local API. Is <code>aecopostmortem serve</code> running?
        </p>
      )}

      {query.status === 'loaded' && (
        <>
          <ConfigurationSummary settings={query.settings} />
          {/* A post-write refresh keeps the previous configuration on screen (useSettings' own
              isRefetching) rather than blanking it — this is the one visible sign it is updating.
              role="status" is an implicit aria-live="polite" region, so it announces without
              stealing focus, the same choice DigestPage's own "Updating…" note makes. */}
          {query.isRefetching && (
            <p className="settings-page__updating" role="status">
              Updating…
            </p>
          )}
        </>
      )}

      <section className="settings-page__actions" aria-label="Commands">
        <WriteOperationCard
          heading="Ingest"
          description="Reads new Copilot sessions from the source directory above and adds them to the store. Already-ingested sessions are skipped, not duplicated."
          buttonLabel="Run ingest"
          runningLabel="Running ingest…"
          state={ingest.state}
          disabled={anyRunning}
          onRun={runIngest}
          renderResult={(result) => <IngestResultSummary result={result} />}
        />

        <WriteOperationCard
          heading="Rebuild"
          description="Re-derives every analysed table from the sessions already in the store. No source directory is read."
          buttonLabel="Run rebuild"
          runningLabel="Running rebuild…"
          state={rebuild.state}
          disabled={anyRunning}
          onRun={runRebuild}
          renderResult={(result) => <RebuildResultSummary result={result} />}
        />
      </section>
    </section>
  )
}

function ConfigurationSummary({ settings }: { settings: SettingsEnvelope }) {
  return (
    <section className="settings-page__config" role="group" aria-label="Configuration">
      <dl className="settings-page__figures">
        <Figure label="Store path" value={settings.storePath} mono />
        <Figure
          label="Store"
          value={
            settings.storeExists
              ? `Exists — ${formatBytes(settings.storeSizeBytes)}`
              : 'Does not exist yet — nothing has been ingested'
          }
        />
        <Figure label="Copilot source root" value={settings.copilotSourceRoot} mono />
        <Figure
          label="Copilot source"
          value={settings.copilotSourceFound ? 'Found' : 'Not found on this machine'}
        />
      </dl>

      <div className="settings-page__exclusions">
        <h2>Excluded roots</h2>
        {settings.excludedRoots.length === 0 ? (
          <p className="settings-page__empty">No roots are configured for exclusion.</p>
        ) : (
          <ul>
            {settings.excludedRoots.map((root) => (
              <li key={root} className="settings-page__mono">
                {root}
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  )
}

function Figure({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="settings-page__figure">
      <dt>{label}</dt>
      <dd className={mono ? 'settings-page__mono' : undefined}>{value}</dd>
    </div>
  )
}

function WriteOperationCard<T>({
  heading,
  description,
  buttonLabel,
  runningLabel,
  state,
  disabled,
  onRun,
  renderResult,
}: {
  heading: string
  description: string
  buttonLabel: string
  runningLabel: string
  state: WriteOperationState<T>
  disabled: boolean
  onRun: () => void
  renderResult: (result: T) => ReactNode
}) {
  const running = state.status === 'running'

  return (
    <div className="settings-page__card">
      <h2>{heading}</h2>
      <p className="settings-page__description">{description}</p>

      <button type="button" onClick={onRun} disabled={disabled}>
        {buttonLabel}
      </button>

      {running && (
        <p className="settings-page__running" role="status">
          {runningLabel}
        </p>
      )}

      {state.status === 'failed' && (
        <p className="settings-page__error" role="alert">
          {state.message}
        </p>
      )}

      {state.status === 'succeeded' && renderResult(state.result)}
    </div>
  )
}

function IngestResultSummary({ result }: { result: IngestResultEnvelope }) {
  return (
    <div className="settings-page__result" role="status">
      <p>
        Found {result.sessionsFound} session{result.sessionsFound === 1 ? '' : 's'}, ingested{' '}
        {result.sessionsIngested}, excluded {result.sessionsExcluded.length} — in{' '}
        {result.durationSeconds.toFixed(1)}s.
      </p>
      <p>
        Lines parsed: {result.linesParsed}. Lines skipped: {result.linesSkipped}.
      </p>
      {result.sessionsExcluded.length > 0 && (
        <ul>
          {result.sessionsExcluded.map((excluded) => (
            <li key={excluded.sessionId}>
              {excluded.sessionId}: {excluded.reason}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function RebuildResultSummary({ result }: { result: RebuildResultEnvelope }) {
  return (
    <div className="settings-page__result" role="status">
      <p>
        Rebuilt from {result.rawEventCount} RAW event{result.rawEventCount === 1 ? '' : 's'} across{' '}
        {result.sessionCount} session{result.sessionCount === 1 ? '' : 's'} — in{' '}
        {result.durationSeconds.toFixed(1)}s.
      </p>
    </div>
  )
}

const units = ['bytes', 'KB', 'MB', 'GB']

function formatBytes(bytes: number): string {
  if (bytes <= 0) {
    return '0 bytes'
  }

  let value = bytes
  let unitIndex = 0
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }

  return `${unitIndex === 0 ? value : value.toFixed(1)} ${units[unitIndex]}`
}
