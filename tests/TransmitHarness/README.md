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
| 1 | 1 | 0.228 | 0.571 | 0.400 | 0.700 | 40 | 0 |
| 1 | 4 | 0.231 | 0.618 | 0.300 | 0.700 | 40 | 0 |
| 1 | 64 | 0.237 | 0.581 | 0.300 | 0.700 | 40 | 0 |
| 24 | 1 | 94.022 | 20.536 | 145.400 | 22.600 | 23040 | 0 |
| 24 | 4 | 66.979 | 7.373 | 75.100 | 11.600 | 23040 | 0 |
| 24 | 64 | 105.866 | 35.545 | 166.400 | 39.600 | 23040 | 0 |
| 64 | 1 | 312.585 | 19.045 | 369.900 | 22.900 | 163840 | 0 |
| 64 | 4 | 347.003 | 47.637 | 420.000 | 54.900 | 163840 | 0 |
| 64 | 64 | 1279.511 | 695.865 | 1453.900 | 837.700 | 163840 | 0 |

## Coverage and implementation decisions

Visibility assertions compare the original decisions with production cache code on empty and full initial bitsets. Cases cover self filtering, enemy-only settings, spectators, GOTV settings, controlled bot team, color changes, unavailable preferences, last color viewer departure, deletion before wrapper invalidation, serial-bearing handle reuse and cache reset. Reconciliation models assert matching creation/removal counts at initial setup, color change, last viewer departure and mass respawn.

The production handler samples target validity, controlled team and current prop indices once per callback. This intentionally retains O(targets + props) native validation so unsignalled changes and index reuse cannot leave stale per-recipient plans. Each viewer's two parsed colors persist until preferences or configured defaults change. The three-second sweep builds shared CT/T color demand once from one controller enumeration. It uses that demand for every target, preserving one glow per target/color without repeated per-target viewer scans. Spawn and coalesced team/takeover/connect events reconcile sooner. External prop deletion invalidates plans in O(1) using tracked serial-bearing handles. Deferred reconciliation carries a runtime generation guard through map/reload boundaries.

A single-player callback has higher overhead in this harness. Unique colors at maximum population still require many transmit bit operations and glow entities. Native CS2 measurements remain necessary to assess the complete gain and spikes.

Release build passed with the pinned CounterStrikeSharp.API 1.0.373. NuGet reports an existing transitive MySqlConnector 2.4.0 vulnerability warning NU1902. No dependency versions were changed.

## Follow-up lifecycle review

The harness now also compiles production Lifecycle.cs. Assertions exercise map-generation invalidation, old versus new timers, coalesced work, pending HEX chat cancellation, disconnect while the old controller wrapper is still valid, slot reuse before queued spawn runs, deletion before wrapper invalidation, duplicate deletion, owned cleanup and 10,000 deleted handles without retained tombstones. OnMapStart is tested with live-looking wrappers from the old world and must discard them without calling native removal.

Cleanup invalidates callbacks before disposing resources. Timer references are reset before Kill, prop references are detached before removal, and finally blocks ensure cache/hook cleanup after provider or timer exceptions. These exception paths and native creation rollback are covered by code inspection and Release compilation. The local harness still substitutes engine operations and does not exercise a real map transition or provider implementation.
