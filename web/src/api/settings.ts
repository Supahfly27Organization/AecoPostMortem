// Mirrors AecoPostMortem.Api.SettingsEnvelope / IngestResultEnvelope / RebuildResultEnvelope
// (src/AecoPostMortem.Api/SettingsEnvelope.cs, IngestResultEnvelope.cs, RebuildResultEnvelope.cs) —
// hand-kept in sync until a generated client exists, the same convention every other api/*.ts file
// in this app follows.

export const SettingsRoute = '/api/settings'
export const IngestRoute = '/api/ingest'
export const RebuildRoute = '/api/rebuild'
export const PurgeRoute = '/api/purge'

/** The header a destructive request must carry, mirroring `AecoPostMortem.Api.ApiHost.
 * ConfirmationHeader`/`PurgeConfirmation`. A custom header makes the request a CORS *non-simple*
 * one, so a hostile page cannot fire it without a preflight this host answers no policy for — see
 * `Api/CLAUDE.md`'s "A destructive route needs a gate that proves intent, not only provenance". This
 * is the machine half of the confirmation; the operator's own typed word is the other half
 * (`routes/SettingsPage.tsx`). */
export const ConfirmationHeader = 'X-AecoPostMortem-Confirm'
export const PurgeConfirmation = 'purge'

export interface SettingsEnvelope {
  storePath: string
  storeExists: boolean
  storeSizeBytes: number
  copilotSourceRoot: string
  copilotSourceFound: boolean
  excludedRoots: string[]
}

export interface ExcludedSessionEnvelope {
  sessionId: string
  reason: string
}

export interface IngestResultEnvelope {
  sessionsFound: number
  sessionsIngested: number
  sessionsExcluded: ExcludedSessionEnvelope[]
  linesParsed: number
  linesSkipped: number
  eventsByType: Record<string, number>
  durationSeconds: number
}

export interface RebuildResultEnvelope {
  rawEventCount: number
  sessionCount: number
  durationSeconds: number
}

/** Mirrors `AecoPostMortem.Api.PurgeResultEnvelope`. `deletedAnything` is served explicitly rather
 * than inferred from an empty `deletedFiles`: purging a store that was never created is a real,
 * honest outcome, and rendering "the store was deleted" for it would claim a deletion that never
 * happened. No `durationSeconds` — unlike ingest and rebuild, deleting a file is not work an
 * operator waits on. */
export interface PurgeResultEnvelope {
  deletedAnything: boolean
  deletedFiles: string[]
  bytesReclaimed: number
}

/** Thrown for a `409` response — the shared write gate (`ApiHost.RunGated`, `Api/CLAUDE.md`)
 * refusing a second ingest or rebuild while one is already running. Distinct from every other
 * failure so a caller can say "already running" rather than a generic error message.
 * `message` is the server's own operator-facing sentence (`Results.Conflict(new { message = "An
 * ingest or rebuild is already running." })`) read off the body (code review, Minor) — a bare route
 * path is not a sentence an operator should have to read. */
export class WriteConflictError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'WriteConflictError'
  }
}

export async function fetchSettings(signal?: AbortSignal): Promise<SettingsEnvelope> {
  const response = await fetch(SettingsRoute, { signal })

  if (!response.ok) {
    throw new Error(`GET ${SettingsRoute} failed with status ${response.status}`)
  }

  return (await response.json()) as SettingsEnvelope
}

export function postIngest(signal?: AbortSignal): Promise<IngestResultEnvelope> {
  return postForResult<IngestResultEnvelope>(IngestRoute, signal)
}

export function postRebuild(signal?: AbortSignal): Promise<RebuildResultEnvelope> {
  return postForResult<RebuildResultEnvelope>(RebuildRoute, signal)
}

/** The only call in this app that sends `ConfirmationHeader` — the server refuses this route with a
 * `403` without it (`ApiHost.IsConfirmed`). */
export function postPurge(signal?: AbortSignal): Promise<PurgeResultEnvelope> {
  return postForResult<PurgeResultEnvelope>(PurgeRoute, signal, {
    [ConfirmationHeader]: PurgeConfirmation,
  })
}

async function postForResult<T>(
  route: string,
  signal?: AbortSignal,
  headers?: Record<string, string>,
): Promise<T> {
  const response = await fetch(route, { method: 'POST', signal, headers })

  if (response.status === 409) {
    throw new WriteConflictError(
      (await readErrorMessage(response)) ?? `${route} is already running.`,
    )
  }

  if (!response.ok) {
    throw new Error((await readErrorMessage(response)) ?? `POST ${route} failed with status ${response.status}`)
  }

  return (await response.json()) as T
}

/** Reads whichever field the server's own error body carries — `Results.Problem(detail: ex.Message,
 * ...)` (`ApiHost.RunGated`'s 500 path) serialises as RFC 7807 `{ ..., "detail": "..." }`, and
 * `Results.Conflict(new { message = "..." })` (its 409 path) serialises as `{ "message": "..." }`.
 * Read defensively — a non-JSON or differently-shaped error body must not itself throw here — so a
 * genuine failure always renders the server's own real message rather than a swallowed one. */
async function readErrorMessage(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { detail?: unknown; message?: unknown }
    if (typeof body.detail === 'string') {
      return body.detail
    }
    return typeof body.message === 'string' ? body.message : null
  } catch {
    return null
  }
}
