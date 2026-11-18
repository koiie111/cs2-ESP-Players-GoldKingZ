# Local performance and visibility validation

Run from the repository root with .NET 10.

```powershell
dotnet build cs2-ESP-Players-GoldKingZ/ESP-Players-GoldKingZ.csproj -c Release
dotnet run --project tests/TransmitHarness -c Release
```

The harness compiles the production TransmitCache.cs against managed substitutes for native entities and transmit bits. Its baseline reproduces the previous transmit decisions with a simplified color parser. Each case warms up for 200 iterations and measures 1,000 iterations. HashSet substitutes for native bit writes. Measurements exclude native schema access, engine allocation, networking and scheduling. They cannot predict server milliseconds or FPS. Timer and respawn rows are explicit models of color reconciliation, with creation/removal counts in place of native entities. Menu rendering, chat delivery, map transitions and native FollowEntity require subsequent in-game acceptance.

Source baseline is 72d92fba80a115da336275b7a48d4194ab91943d. The supplied server capture measured CheckTransmit mean 0.600 ms, p95 1.023 ms and p99 2.750 ms at 23–24 players over 45 seconds. Those numbers and the local measurements have different coverage.

## Measured transmit algorithm

All durations are microseconds per complete recipient list. Palette is the number of distinct requested colors, capped by viewer count. Results are one local run and include scheduling noise. Full results include per-viewer plan rebuilding and timer models in results.txt.

| Players | Palette | Before mean | Cached mean | Before p99 | Cached p99 | Before bytes | Cached bytes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 1 | 0.237 | 0.518 | 0.500 | 0.600 | 40 | 0 |
| 1 | 4 | 0.234 | 0.537 | 0.300 | 0.600 | 40 | 0 |
| 1 | 64 | 0.236 | 0.546 | 0.300 | 0.600 | 40 | 0 |
| 24 | 1 | 88.761 | 18.377 | 145.200 | 20.800 | 23040 | 0 |
| 24 | 4 | 49.777 | 7.176 | 68.900 | 11.800 | 23040 | 0 |
| 24 | 64 | 86.803 | 34.180 | 159.600 | 38.200 | 23040 | 0 |
| 64 | 1 | 312.037 | 18.866 | 380.300 | 29.900 | 163840 | 0 |
| 64 | 4 | 347.078 | 46.976 | 430.200 | 60.900 | 163840 | 0 |
| 64 | 64 | 1273.020 | 675.209 | 1482.300 | 718.200 | 163840 | 0 |

## Coverage and implementation decisions

Visibility assertions compare the original decisions with production cache code on empty and full initial bitsets. Cases cover self filtering, enemy-only settings, spectators, GOTV settings, controlled bot team, color changes, unavailable preferences, last color viewer departure, deletion before wrapper invalidation, serial-bearing handle reuse and cache reset. Reconciliation models assert matching creation/removal counts at initial setup, color change, last viewer departure and mass respawn.

The production handler samples target validity, controlled team and current prop indices once per callback. This intentionally retains O(targets + props) native validation so unsignalled changes and index reuse cannot leave stale per-recipient plans. Each viewer's two parsed colors persist until preferences or configured defaults change. The three-second sweep builds shared CT/T color demand once from one controller enumeration. It uses that demand for every target, preserving one glow per target/color without repeated per-target viewer scans. Spawn and coalesced team/takeover/connect events reconcile sooner. External prop deletion invalidates plans in O(1) using tracked serial-bearing handles. Deferred reconciliation carries a runtime generation guard through map/reload boundaries.

A single-player callback has higher overhead in this harness. Unique colors at maximum population still require many transmit bit operations and glow entities. Native CS2 measurements remain necessary to assess the complete gain and spikes.

Release build passed with the pinned CounterStrikeSharp.API 1.0.373. NuGet reports an existing transitive MySqlConnector 2.4.0 vulnerability warning NU1902. No dependency versions were changed.
