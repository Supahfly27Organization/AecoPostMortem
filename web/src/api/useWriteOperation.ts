import { useCallback, useRef, useState } from 'react'

export type WriteOperationState<T> =
  | { status: 'idle' }
  | { status: 'running' }
  | { status: 'succeeded'; result: T }
  | { status: 'failed'; message: string }

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
export function useWriteOperation<T>(action: (signal: AbortSignal) => Promise<T>): {
  state: WriteOperationState<T>
  run: () => Promise<void>
} {
  const [state, setState] = useState<WriteOperationState<T>>({ status: 'idle' })
  const runningRef = useRef(false)

  const run = useCallback(async () => {
    if (runningRef.current) {
      return
    }

    runningRef.current = true
    setState({ status: 'running' })

    const controller = new AbortController()
    try {
      const result = await action(controller.signal)
      setState({ status: 'succeeded', result })
    } catch (error) {
      setState({ status: 'failed', message: messageFor(error) })
    } finally {
      runningRef.current = false
    }
  }, [action])

  return { state, run }
}

function messageFor(error: unknown): string {
  return error instanceof Error ? error.message : 'The request failed for an unknown reason.'
}
