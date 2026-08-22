/**
 * Cross-cutting seam for "the store's contents just changed" — an ingest or rebuild completed from
 * the Settings page. `AppShell`'s `AppStateBanner` fetches `/api/app-state` once on mount
 * (`useAppState`) and stays mounted for the life of the SPA session — only the routed `<Outlet>`
 * content swaps between pages — so navigating away from Settings is not what refreshes it; without
 * this, a "Nothing has been ingested yet" banner would keep showing after a real, successful ingest
 * until a full page reload.
 *
 * The Digest, Rules Inventory and Monitor pages need no such signal: each fetches fresh in its own
 * `useEffect` on mount, and mounting is exactly what happens when the operator navigates to one of
 * them from Settings — leaving Settings after a write already lands on post-write data with no extra
 * wiring.
 *
 * A plain `window` `CustomEvent` rather than a shared store/cache library: there is exactly one
 * cross-cutting listener today (`useAppState`), and a bespoke event is the smallest seam that
 * decouples the writer (`SettingsPage`) from it without introducing global state this app has no
 * other need for.
 */
export const StoreChangedEventName = 'aecopostmortem:store-changed'

export function notifyStoreChanged(): void {
  window.dispatchEvent(new Event(StoreChangedEventName))
}

/** Returns the cleanup function an effect should return, mirroring `AbortController.abort`'s own
 * "call it to stop listening" shape every other hook in this app already returns from its effect. */
export function onStoreChanged(listener: () => void): () => void {
  window.addEventListener(StoreChangedEventName, listener)
  return () => window.removeEventListener(StoreChangedEventName, listener)
}
