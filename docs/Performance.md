# Performance

Benchmarks for the ImageSharp pixel-indexer rewrite (#15).

## Method

Harness: a throwaway console project that calls `ttf2flf.Program.Main` in-process on the
calibrated corpus font `Corpus/TTF/Gecko 16.ttf`, one warmup iteration then 8 timed
iterations, with `GC.GetTotalAllocatedBytes` around the loop. Wall time and allocated
bytes are per iteration.

Environment: Apple M1, macOS 27.0, .NET SDK 10.0.401, Release build, server GC off,
tiered PGO on.

Reproduce:

```bash
# build the pre-#15 baseline and current main
git worktree add --detach /tmp/pre15 c52d1b6
dotnet build /tmp/pre15/src/ttf2flf -c Release -o /tmp/pre15-bin
dotnet build src/ttf2flf -c Release -o /tmp/cur-bin
# run the harness twice, once with each project as Ttf2flfProj
dotnet run -c Release -p:Ttf2flfProj=<path-to-csproj>
```

The harness runs two cases: pixel mode at `--render-size 512`, and anti-aliased mode at
`--height 64` (renders at 512 px).

## Results (per iteration)

| Case | Before (`c52d1b6`) | After (`main`) | Change |
|---|---|---|---|
| pixel, `--render-size 512` | 670 ms, 102.3 MiB | 440 ms, 111.5 MiB | **-34% time**; allocation dominated by `DrawText`, unchanged in kind |
| anti-aliased, `--height 64` | 209 ms, 27.8 MiB | 166 ms, 27.8 MiB | **-21% time**; allocation unchanged |

## Output equality

- Anti-aliased output is byte-identical before and after (glyph bodies).
- Pixel mode glyph bodies are identical except that fonts whose ascenders were previously
  clamped by the span-sized canvas gain rows. That is the separate, intentional #36 fix
  (baseline-extent canvas), which landed between the two benchmarked commits. With #36
  excluded, the #15 rewrite alone produces byte-identical output.
