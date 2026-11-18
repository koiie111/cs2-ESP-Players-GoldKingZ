# ESP performance and visibility checks

Run with .NET 10 from the repository root. The recorded run used SDK 10.0.400 on Windows. Disabling tiered compilation keeps the JIT tier consistent across cases.

```powershell
dotnet build cs2-ESP-Players-GoldKingZ/ESP-Players-GoldKingZ.csproj -c Release
$env:DOTNET_TieredCompilation = '0'
dotnet run --project tests/TransmitHarness -c Release
```

## CheckTransmit

Each time below is in microseconds per complete list of recipients. Each scenario has equally sized teams where possible, one target per viewer, alternating team and palette assignment, enemy-only visibility, and 200 warm-up plus 1,000 measured callbacks. The baseline models the decisions at `72d92fb` with a simplified personal color parser. The middle column runs the incoming PR implementation at `ccf97ba` with the same packed bitset substitute as the final harness. Previous PR measurements using HashSet bit writes have different costs.

| Players | Colors per team before pruning | Baseline mean | Incoming PR mean | Final mean | Final p99 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 1 | 0.133 | 0.086 | 0.071 | 0.100 |
| 24 | 1 | 68.563 | 2.913 | 2.588 | 4.900 |
| 24 | 4 | 75.732 | 4.764 | 3.624 | 6.200 |
| 24 | 24 | 128.024 | 19.988 | 12.926 | 15.900 |
| 64 | 1 | 486.887 | 13.431 | 12.873 | 18.700 |
| 64 | 4 | 534.481 | 26.875 | 14.541 | 16.500 |
| 64 | 64 | 1558.852 | 298.586 | 171.494 | 222.700 |

The final steady-state callback allocated zero managed bytes in these fixtures. The baseline model allocated 23,040 bytes at 24 players and 163,840 at 64. The harness compiles the production transmit, demand and scheduling code. Controllers and prop handles are managed substitutes. The transmit bitmap has 16,384 bits with SDK-style word operations. The harness excludes native handle lookup, schema reads, prop spawning, scheduling and network costs. Run-to-run noise affects these timings.

[Raw final results](results.txt) include p95, p99, allocations, and two rebuild cases. `world-rebuild` invalidates all plans by toggling the GOTV setting on each call with the same viewers and entities. `rebuild` forgets one viewer and exercises allocating its color cache and plan again. With a stable viewer population, world changes reuse existing plan buffers. A larger entity population can still grow them.

## Entity and lifecycle checks

With unique colors, 12v12 went from 576 to 288 glow props and 32v32 from 4,096 to 2,048. Relays are additional, one per live target. Players sharing colors across teams, spectators and enabled GOTV change the demand. With one common color both populated teams still need one glow per target. Native creation, attachment and deletion can still spike during a round transition.

The suite compares transmit bits starting empty and full. It also compares the recipients' visible entities before and after pruning and independently derives exactly which colors any eligible viewer can see. Cases cover self and teammate filtering, spectators, bots, enabled and disabled GOTV, controlled bot teams, a late preferences load, color changes, disconnection, native deletion and index reuse.

A burst of 23 spawns delivered twice in the same frame makes one controller enumeration and visits each spawned connection once. Tests cover slot replacement, queued reload across shutdown, old map timers, pending color input and external prop deletion. Timer and respawn measurements model the desired color sets and entity counts. Engine attachments, command listener disposal and client rendering still require an in-game check.

## Server comparison

The earlier 45-second server capture quoted in the PR measured CheckTransmit at 23–24 players with a mean of 0.600 ms, p95 of 1.023 ms and p99 of 2.750 ms. There is no matching post-change engine capture. Compare the same map, player count, team mix and personal palette, and sample both steady play and round starts. Record callback mean/p95/p99, server frame time and peak live prop count. The managed timing ratio alone cannot predict server CPU or tick time.

Release compilation passed with the pinned CounterStrikeSharp.API 1.0.373 and with 1.0.380 selected by the release workflow at review time. NuGet reported NU1902 for transitive MySqlConnector 2.4.0.
