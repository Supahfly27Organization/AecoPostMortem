# AecoPostMortem.Ingestion

Path discovery, event-line reader, RAW store, session/turn/agent reconstruction, self-exclusion.

## References

`Data` — it writes through the RAW store `Data` owns, and nothing else: ingestion has no reason to
see rule checks or findings, only to land raw events and reconstruct sessions from them.

## Status

Empty. Path discovery and the event-line reader are the first things that land here.
