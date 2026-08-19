import { ComingSoon } from './ComingSoon'

/** The front door (PRD §3.1: "Getting started ... Open the Process Digest"). Its ranking and
 * masthead are FR-41, built by S-36 and S-54 — this route exists and is reachable now, ahead of
 * that content, per this story's own scope. */
export function DigestPage() {
  return <ComingSoon surface="Process Digest" story="S-36 / S-54" release="Release 1" />
}
