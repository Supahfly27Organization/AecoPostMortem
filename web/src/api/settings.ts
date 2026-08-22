// Mirrors AecoPostMortem.Api.SettingsEnvelope / IngestResultEnvelope / RebuildResultEnvelope
// (src/AecoPostMortem.Api/SettingsEnvelope.cs, IngestResultEnvelope.cs, RebuildResultEnvelope.cs) —
// hand-kept in sync until a generated client exists, the same convention every other api/*.ts file
// in this app follows.

export const SettingsRoute = '/api/settings'
export const IngestRoute = '/api/ingest'
export const RebuildRoute = '/api/rebuild'

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

/** Thrown for a `409` response — the shared write gate (`ApiHost.RunGated`, `Api/CLAUDE.md`)
 * refusing a second ingest or rebuild while one is already running. Distinct from every other
 * failure so a caller can say "already running" rather than a generic error message. */
export class WriteConflictError extends Error {
  constructor(route: string) {
    super(`${route} is already running.`)
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

async function postForResult<T>(route: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(route, { method: 'POST', signal })

  if (response.status === 409) {
    throw new WriteConflictError(route)
  }

  if (!response.ok) {
    throw new Error((await readProblemDetail(response)) ?? `POST ${route} failed with status ${response.status}`)
  }

  return (await response.json()) as T
}

/** `Results.Problem(detail: ex.Message, ...)` on the server (`ApiHost.RunGated`) serialises as
 * RFC 7807 `{ ..., "detail": "..." }` — read defensively (a non-JSON or differently-shaped error
 * body must not itself throw here) so a genuine failure always renders the real message rather than
 * a swallowed one. */
async function readProblemDetail(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { detail?: unknown }
    return typeof body.detail === 'string' ? body.detail : null
  } catch {
    return null
  }
}
