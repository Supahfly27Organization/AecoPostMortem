# AecoPostMortem.Api

Endpoints for the three surfaces.

## References

`Findings` — the API is a thin host over the finding classes and their orchestration; it has no
reason to reach into `Data` or `Rules` directly, only through what `Findings` already exposes.

## Status

Empty. S-47 created it; S-48 populates it.
