import { useState, type ReactNode } from 'react'
import {
  postIngest,
  postPurge,
  postRebuild,
  PurgeConfirmation,
  type IngestResultEnvelope,
  type PurgeResultEnvelope,
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
  const purge = useWriteOperation(postPurge)

  // The operator's own half of the purge confirmation (the machine half is the header `postPurge`
  // sends). Held here rather than inside `WriteOperationCard` so that clearing it after a run — the
  // card is re-armed deliberately, never left one click away from a second purge — is the same
  // post-write step `afterWrite` already is, not hidden state a parent cannot reset.
  const [purgeConfirmation, setPurgeConfirmation] = useState('')

  const anyRunning =
    ingest.state.status === 'running' ||
    rebuild.state.status === 'running' ||
    purge.state.status === 'running'

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

  async function runPurge() {
    const outcome = await purge.run()

    // Cleared whatever the outcome: a refused or failed purge leaving the button armed would sit one
    // click away from retrying a destructive action the operator has not re-confirmed.
    setPurgeConfirmation('')

    if (outcome === 'succeeded') {
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

        {/* The only destructive action in the app, and the only card that has to be armed before it
            can be run — see `web/CLAUDE.md`'s "A destructive button is armed by typing, not by
            clicking" for why a typed word rather than a second click or a native confirm(). */}
        <WriteOperationCard
          heading="Purge"
          description="Deletes the local store — every ingested session, every analysed table. Nothing in your Copilot session directory is touched, so a later ingest rebuilds the store from the same source."
          buttonLabel="Purge the store"
          runningLabel="Purging…"
          state={purge.state}
          disabled={anyRunning || purgeConfirmation.trim() !== PurgeConfirmation}
          onRun={runPurge}
          renderResult={(result) => <PurgeResultSummary result={result} />}
          destructive
          confirmationField={
            <ConfirmationField
              value={purgeConfirmation}
              onChange={setPurgeConfirmation}
              disabled={anyRunning}
            />
          }
        />
      </section>
    </section>
  )
}

function ConfirmationField({
  value,
  onChange,
  disabled,
}: {
  value: string
  onChange: (value: string) => void
  disabled: boolean
}) {
  return (
    <div className="settings-page__confirmation">
      {/* Explicit htmlFor/id rather than an implicit wrapping label — the association every
          accessibility API agrees on, the same gap a real-browser check caught on
          `digest/DateRangeFilter.tsx` (`web/CLAUDE.md`). */}
      <label htmlFor="purge-confirmation">Type {PurgeConfirmation} to confirm</label>
      <input
        id="purge-confirmation"
        type="text"
        autoComplete="off"
        spellCheck={false}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      />
    </div>
  )
}

function ConfigurationSummary({ settings }: { settings: SettingsEnvelope }) {
  return (
    <section className="settings-page__config" role="group" aria-label="Configuration">
      <dl className="settings-page__figures">
        <Figure label="Store path" value={settings.storePath} mono />
        <Figure
          label="Store location"
          value={
            settings.storeIsAtDefaultLocation
              ? 'The documented default location for this machine'
              : 'Not the default location — this store was chosen with --store'
          }
        />
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
  destructive,
  confirmationField,
}: {
  heading: string
  description: string
  buttonLabel: string
  runningLabel: string
  state: WriteOperationState<T>
  disabled: boolean
  onRun: () => void
  renderResult: (result: T) => ReactNode
  /** Marks the card as one that destroys data — a `data-` attribute rather than a second class, so
   * the distinction is readable from the DOM (and assertable in a test) rather than only visual. */
  destructive?: boolean
  /** Rendered between the description and the button, for a card the operator has to arm before the
   * button becomes usable. Absent for a card that needs no arming — the two non-destructive write
   * actions render byte-for-byte as they did before this prop existed. */
  confirmationField?: ReactNode
}) {
  const running = state.status === 'running'

  return (
    <div className="settings-page__card" data-destructive={destructive ? 'true' : undefined}>
      <h2>{heading}</h2>
      <p className="settings-page__description">{description}</p>

      {confirmationField}

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

function PurgeResultSummary({ result }: { result: PurgeResultEnvelope }) {
  return (
    <div className="settings-page__result" role="status">
      {result.deletedAnything ? (
        <>
          <p>
            Deleted {result.deletedFiles.length} file
            {result.deletedFiles.length === 1 ? '' : 's'}, reclaiming{' '}
            {formatBytes(result.bytesReclaimed)}. Run ingest to rebuild the store from your Copilot
            sessions.
          </p>
          <ul>
            {result.deletedFiles.map((file) => (
              <li key={file} className="settings-page__mono">
                {file}
              </li>
            ))}
          </ul>
        </>
      ) : (
        // Never "Deleted 0 files": nothing was deleted, and saying so plainly is a different fact
        // from a deletion of nothing.
        <p>There was no store to purge — nothing has been ingested at this path yet.</p>
      )}
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
