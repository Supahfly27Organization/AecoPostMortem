import { useCallback, useRef, useState } from 'react'

export type WriteOperationState<T> =
  | { status: 'idle' }
  | { status: 'running' }
  | { status: 'succeeded'; result: T }
  | { status: 'failed'; message: string }

/** The two terminal outcomes a run can settle to — returned from `run()` itself (code review) so a
 * caller can tell a genuine success apart from a conflict or a failure without re-reading `state`
 * (which a subsequent render may not have committed yet by the time the caller's own `await`
 * resumes). `SettingsPage` uses this to gate its own post-write side effects (refetching this page's
 * settings, notifying the rest of the app the store changed) on `'succeeded'` alone — a `409`
 * conflict or a real failure must never be read as "the store just changed." */
export type WriteOperationOutcome = 'succeeded' | 'failed'

/**
 * A POST that must state that it is in progress, must never run twice concurrently from this UI,
 * and must never fail silently (the Settings brief's own Scenario 2). `run` is a no-op while a
 * previous call is still `running` — a `useRef` flag, checked synchronously before the state update,
 * so a rapid double-click cannot slip two calls through before the first render showing `'running'`
 * ever commits. This is belt-and-braces alongside the server's own shared write gate
 * (`ApiHost.RunGated`, `Api/CLAUDE.md`), which is what actually prevents two concurrent writes from
 * touching the store at once; this hook's own guard is what keeps a double-click from firing a
 * second HTTP request at all, and what a genuinely separate client (a second browser tab) still
 * relies on the server-side gate for.
 */
export function useWriteOperation<T>(action: () => Promise<T>): {
  state: WriteOperationState<T>
  run: () => Promise<WriteOperationOutcome>
} {
  const [state, setState] = useState<WriteOperationState<T>>({ status: 'idle' })
  const runningRef = useRef(false)

  const run = useCallback(async (): Promise<WriteOperationOutcome> => {
    if (runningRef.current) {
      // Never reached from this hook's own caller today (SettingsPage disables the triggering
      // button while running), but a stray extra call — e.g. a future caller wiring a second
      // trigger to the same instance — must not report a false success for a run it never started.
      return 'failed'
    }

    runningRef.current = true
    setState({ status: 'running' })

    // No AbortController here (code review, Minor): aborting the fetch would not stop the real
    // ingest/rebuild work already running server-side (`ApiHost.RunGated` holds the write gate for
    // the whole operation regardless of whether this client is still listening) — it would only
    // make the client lose visibility into a result that still lands in the store. Unlike
    // `useSettings`'s own GET, there is nothing correct to cancel here.
    try {
      const result = await action()
      setState({ status: 'succeeded', result })
      return 'succeeded'
    } catch (error) {
      setState({ status: 'failed', message: messageFor(error) })
      return 'failed'
    } finally {
      runningRef.current = false
    }
  }, [action])

  return { state, run }
}

function messageFor(error: unknown): string {
  return error instanceof Error ? error.message : 'The request failed for an unknown reason.'
}
