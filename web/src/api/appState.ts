// Mirrors AecoPostMortem.Api.AppStateReport (src/AecoPostMortem.Api/AppStateReport.cs). The route
// and the field shape are the contract between the .NET host and this client; keep both in sync by
// hand until a generated client exists.

export const AppStateRoute = '/api/app-state'

export type AppStateKind = 'noSourceFound' | 'emptyStore' | 'ready'

export interface AppStateReport {
  kind: AppStateKind
  message: string
  fixCommand: string | null
}

/** Throws on a non-2xx response or a network failure; callers (see `useAppState`) turn that into
 * a state a component can render rather than an unhandled rejection. */
export async function fetchAppState(signal?: AbortSignal): Promise<AppStateReport> {
  const response = await fetch(AppStateRoute, { signal })

  if (!response.ok) {
    throw new Error(`GET ${AppStateRoute} failed with status ${response.status}`)
  }

  return (await response.json()) as AppStateReport
}
