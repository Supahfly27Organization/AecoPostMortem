# AecoPostMortem.Api

Endpoints for the three surfaces.

## References

`Findings` — the API is a thin host over the finding classes and their orchestration; it has no
reason to reach into `Data` or `Rules` directly, only through what `Findings` already exposes.

## Status

Empty. Endpoints for the three surfaces are the first thing that lands here.
