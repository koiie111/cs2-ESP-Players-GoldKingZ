using System.Diagnostics;
using GameTimer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Core;
using ESP_Players_GoldKingZ;
using ESP_Players_GoldKingZ.Config;

static class Program
{
    static CDynamicProp Prop(int id) => new() { EntityHandle = ((uint)(id + 10000), id) };
    static void Baseline(MainPlugin plugin, CCheckTransmitInfoList infos)
    {
        foreach (var (info, viewer) in infos)
        {
            if (viewer == null || !viewer.IsValid) continue;
            foreach (var (slot, data) in plugin.g_Main.Player_Data)
            {
                bool relay = data.ModelRelay != null && data.ModelRelay.IsValid;
                if (!relay && !data.ModelGlows.Values.Any(g => g.IsValid)) continue;
                byte team = data.Player.Get_CCSPlayerController_ControlledBot().TeamNum;
                bool remove = (viewer.IsHLTV && Configs.Instance.DisableGlowOnGOTV) || slot == viewer.Slot || !data.Player.IsValid;
                remove |= Configs.Instance.ShowOnlyEnemyTeam && (viewer.TeamNum == 2 || viewer.TeamNum == 3) && team == viewer.TeamNum;
                int color = plugin.BaselineColor(viewer.Slot, team).ToArgb();
                foreach (var (key, glow) in data.ModelGlows)
                    if (glow.IsValid && (remove || key != color)) info.TransmitEntities.Remove((int)glow.Index);
                if (remove) { if (relay) info.TransmitEntities.Remove((int)data.ModelRelay!.Index); continue; }
                if (relay) info.TransmitEntities.Add((int)data.ModelRelay!.Index);
                if (data.ModelGlows.TryGetValue(color, out var selected) && selected.IsValid) info.TransmitEntities.Add((int)selected.Index);
            }
        }
    }
    static CCheckTransmitInfoList Copy(CCheckTransmitInfoList infos, bool full) => new(infos.Select(x =>
        (new CCheckTransmitInfo { TransmitEntities = new TransmitBits(full) }, x.Item2)));
    static void Equal(MainPlugin plugin, CCheckTransmitInfoList infos)
    {
        foreach (bool full in new[] { false, true })
        {
            var expected = Copy(infos, full);
            var actual = Copy(infos, full);
            Baseline(plugin, expected);
            plugin.Run(actual);
            for (int i = 0; i < expected.Count; i++)
                if (!expected[i].Item1.TransmitEntities.SetEquals(actual[i].Item1.TransmitEntities)) throw new Exception("Transmit mismatch");
        }
    }
    static void Measure(string label, Action action)
    {
        for (int i = 0; i < 200; i++) action();
        var samples = new double[1000];
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < samples.Length; i++)
        {
            long start = Stopwatch.GetTimestamp(); action();
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        double avg = samples.Average(); Array.Sort(samples);
        Console.WriteLine($"{label},{avg:F3},{samples[949]:F3},{samples[989]:F3},{bytes / samples.Length}");
    }
    // Models reconciliation's enumeration/color work. Native creation/removal is counted only.
    static (int Created, int Removed) Reconcile(MainPlugin plugin, List<CCSPlayerController> viewers,
        bool cached, Dictionary<int, HashSet<int>> entities)
    {
        var demand = cached ? new GlowDemand(plugin, viewers) : null;
        int created = 0, removed = 0;
        foreach (var (slot, target) in plugin.g_Main.Player_Data)
        {
            byte team = target.Player.Get_CCSPlayerController_ControlledBot().TeamNum;
            var desired = new HashSet<int>();
            if (demand != null)
            {
                foreach (var (color, owner) in demand.ForTeam(team))
                    if (owner != slot) desired.Add(color);
            }
            else
            {
                foreach (var viewer in viewers)
                    if (!viewer.IsBot && !viewer.IsHLTV) desired.Add(plugin.BaselineColor(viewer.Slot, team).ToArgb());
            }
            if (!entities.TryGetValue(slot, out var current)) entities[slot] = current = new HashSet<int>();
            created += desired.Count(c => !current.Contains(c));
            removed += current.Count(c => !desired.Contains(c));
            current.Clear(); current.UnionWith(desired);
        }
        return (created, removed);
    }

    static void CheckDemand(MainPlugin plugin, List<CCSPlayerController> viewers)
    {
        var entities = new Dictionary<int, HashSet<int>>();
        Reconcile(plugin, viewers, true, entities);
        foreach (var (slot, data) in plugin.g_Main.Player_Data)
        {
            byte team = data.Player.Get_CCSPlayerController_ControlledBot().TeamNum;
            var visible = new HashSet<int>();
            foreach (var viewer in viewers)
            {
                if (viewer.IsHLTV && Configs.Instance.DisableGlowOnGOTV) continue;
                if (viewer.IsBot && !viewer.IsHLTV || viewer.Slot == slot) continue;
                if (Configs.Instance.ShowOnlyEnemyTeam && viewer.TeamNum is 2 or 3 && viewer.TeamNum == team) continue;
                visible.Add(plugin.BaselineColor(viewer.Slot, team).ToArgb());
            }
            if (!visible.SetEquals(entities[slot])) throw new Exception("Glow demand differs from recipient visibility");
        }
    }

    static void CheckLifecycle()
    {
        var plugin = new MainPlugin();
        MainPlugin.Instance = plugin;
        plugin.StartEspRuntime();
        plugin.BeginInput(5);
        plugin.QueueReconcile();
        plugin.QueueReconcile();
        if (CounterStrikeSharp.API.Server.Frames.Count != 1) throw new Exception("Reconcile was not coalesced");
        var oldTimer = GameTimer.Repeating.Single();
        plugin.StopEspRuntime();
        if (plugin.PendingInputs != 0) throw new Exception("Pending chat input survived map end");
        CounterStrikeSharp.API.Server.Flush();
        oldTimer.Fire();
        if (Helper.Reconciles != 0) throw new Exception("Stopped runtime executed old callbacks");

        plugin.StartEspRuntime();
        plugin.QueueReconcile();
        var oldWorldProp = Prop(4321);
        plugin.TrackEspEntity(oldWorldProp);
        plugin.g_Main.Player_Data[9] = new Globals.PlayerDataClass { ModelRelay = oldWorldProp };
        plugin.OnMapStart("new_map");
        if (oldWorldProp.RemoveCalls != 0 || plugin.TrackedCount != 0 || plugin.g_Main.Player_Data.Count != 0)
            throw new Exception("Map start touched old-world native entities");
        CounterStrikeSharp.API.Server.Flush();
        if (Helper.Reconciles != 1) throw new Exception("Map change executed stale reconciliation");
        oldTimer.Fire();
        if (Helper.Reconciles != 1) throw new Exception("Old timer crossed map generation");
        GameTimer.Repeating.Single().Fire();
        if (Helper.Reconciles != 2) throw new Exception("New map timer did not run");

        var oldPlayer = new CCSPlayerController { Slot = 5, TeamNum = 2, EntityHandle = (100, 5) };
        plugin.OnEspSpawn(new EventPlayerSpawn { Userid = oldPlayer }, new GameEventInfo());
        plugin.BeginInput(5);
        plugin.OnEspClientDisconnect(5);
        if (plugin.PendingInputs != 0 || plugin.g_Main.Player_Data.ContainsKey(5)) throw new Exception("Disconnect retained slot state");
        // The old wrapper can remain valid until the native disconnect finishes.
        var replacement = new CCSPlayerController { Slot = 5, TeamNum = 3, EntityHandle = (200, 5) };
        plugin.g_Main.Player_Data[5] = new Globals.PlayerDataClass { Player = replacement };
        CounterStrikeSharp.API.Server.Flush();
        CounterStrikeSharp.API.Utilities.Connected.Add(replacement);
        // Stale spawn from an old wrapper, with the slot already bound to its replacement.
        plugin.g_Main.Player_Data[5] = new Globals.PlayerDataClass { Player = oldPlayer };
        plugin.OnEspSpawn(new EventPlayerSpawn { Userid = oldPlayer }, new GameEventInfo());
        plugin.g_Main.Player_Data[5] = new Globals.PlayerDataClass { Player = replacement };
        CounterStrikeSharp.API.Server.Flush();
        if (Helper.Spawns != 0) throw new Exception("Old connection spawned props after slot reuse");
        // A spectator creates real demand for the otherwise solitary target.
        CounterStrikeSharp.API.Utilities.Connected.Add(new CCSPlayerController { Slot = 40, TeamNum = 1 });
        plugin.OnEspSpawn(new EventPlayerSpawn { Userid = replacement }, new GameEventInfo());
        CounterStrikeSharp.API.Server.Flush();
        if (Helper.Spawns != 1) throw new Exception("Replacement connection could not spawn");

        int sweeps = Helper.Reconciles;
        for (int slot = 6; slot < 29; slot++)
        {
            var player = new CCSPlayerController { Slot = slot, TeamNum = 2 };
            CounterStrikeSharp.API.Utilities.Connected.Add(player);
            plugin.OnEspSpawn(new EventPlayerSpawn { Userid = player }, new GameEventInfo());
            plugin.OnEspSpawn(new EventPlayerSpawn { Userid = player }, new GameEventInfo());
        }
        CounterStrikeSharp.API.Server.Flush();
        if (Helper.Spawns != 24 || Helper.Reconciles != sweeps + 1) throw new Exception("Spawn burst was not batched");
        plugin.ScheduleConfigReload();
        plugin.StopEspRuntime();
        CounterStrikeSharp.API.Server.Flush();
        if (Helper.Reloads != 0) throw new Exception("Queued reload revived stopped plugin");
        plugin.StartEspRuntime();
        plugin.ScheduleConfigReload();
        CounterStrikeSharp.API.Server.Flush();
        if (Helper.Reloads != 1) throw new Exception("Active config reload was dropped");

        var propForPrefs = Prop(1440);
        plugin.g_Main.Player_Data[5].ModelRelay = propForPrefs;
        plugin.TrackEspEntity(propForPrefs);
        var infos = new CCheckTransmitInfoList { (new CCheckTransmitInfo { TransmitEntities = new() }, replacement) };
        plugin.Run(infos);
        plugin._prefs[5] = new Prefs { Glow_Color_CT = "#DDEEFF" };
        plugin.Run(infos);
        plugin.Run(infos);
        if (CounterStrikeSharp.API.Server.Frames.Count != 1) throw new Exception("Late preferences did not queue a single repair");
        CounterStrikeSharp.API.Server.Flush();
        plugin.RemoveEspEntity(propForPrefs);

        var deleted = Prop(1234);
        plugin.TrackEspEntity(deleted);
        plugin.OnEspEntityDeleted(deleted);
        if (plugin.IsEspEntityAlive(deleted)) throw new Exception("Deleted entity still owned before invalidation");
        plugin.RemoveEspEntity(deleted);
        if (deleted.RemoveCalls != 0) throw new Exception("Deleted entity removed twice");
        int frames = CounterStrikeSharp.API.Server.Frames.Count;
        plugin.OnEspEntityDeleted(deleted);
        if (CounterStrikeSharp.API.Server.Frames.Count != frames) throw new Exception("Duplicate deletion queued extra work");
        CounterStrikeSharp.API.Server.Flush();
        plugin.TrackEspEntity(deleted);
        plugin.RemoveEspEntity(deleted);
        plugin.OnEspEntityDeleted(deleted);
        if (deleted.RemoveCalls != 1 || CounterStrikeSharp.API.Server.Frames.Count != 0) throw new Exception("Owned cleanup retriggered repair");
        for (int i = 0; i < 10000; i++)
        {
            var prop = Prop(5000 + i);
            plugin.TrackEspEntity(prop);
            plugin.OnEspEntityDeleted(prop);
        }
        if (plugin.TrackedCount != 0) throw new Exception("Deleted handles accumulated");
        CounterStrikeSharp.API.Server.Flush();
        plugin.StopEspRuntime();
        Console.WriteLine("PASS actual lifecycle callbacks: map generations, old/new timers, coalescing, disconnect with valid old wrapper, slot reuse, pending chat, duplicate/native deletion, owned cleanup");
    }
    static void CheckRelayDemand()
    {
        var plugin = MainPlugin.Instance = new MainPlugin();
        var player = new CCSPlayerController { Slot = 3, TeamNum = 2 };
        var teammate = new CCSPlayerController { Slot = 4, TeamNum = 2 };
        var gotv = new CCSPlayerController { Slot = 5, TeamNum = 1, IsBot = true, IsHLTV = true };
        CounterStrikeSharp.API.Utilities.Connected = [player, teammate, gotv];
        Configs.Instance.ShowOnlyEnemyTeam = true;
        Configs.Instance.DisableGlowOnGOTV = true;
        var relay = Prop(77);
        var glow = Prop(78);
        plugin.TrackEspEntity(relay);
        plugin.TrackEspEntity(glow);
        var data = new Globals.PlayerDataClass { Player = player, ModelRelay = relay };
        data.ModelGlows[1] = glow;
        plugin.g_Main.Player_Data[player.Slot] = data;
        int setups = Helper.Spawns;
        Helper.ESP_Timer();
        if (plugin.TrackedCount != 0 || relay.RemoveCalls != 1 || glow.RemoveCalls != 1 || Helper.Spawns != setups)
            throw new Exception("Last eligible viewer leaving retained or recreated an invisible relay");
        Configs.Instance.DisableGlowOnGOTV = false;
        Helper.ESP_Timer();
        if (Helper.Spawns != setups + 2) throw new Exception("GOTV did not restore demand for both targets");

        Configs.Instance.ShowOnlyEnemyTeam = false;
        CounterStrikeSharp.API.Utilities.Connected = [player];
        Helper.ESP_Timer();
        if (Helper.Spawns != setups + 2) throw new Exception("Self-only color created a relay");
        CounterStrikeSharp.API.Utilities.Connected.Add(teammate);
        Helper.ESP_Timer();
        if (Helper.Spawns != setups + 4) throw new Exception("Second viewer sharing a color did not restore demand");
        Console.WriteLine("PASS relay demand: teammates, self-only color, last viewer leaving, shared color and GOTV");
    }

    static void Main()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        CheckLifecycle();
        HudTests.Run();
        CheckRelayDemand();
        Console.WriteLine("case,mean_us,p95_us,p99_us,bytes_per_call");
        foreach (int count in new[] { 1, 24, 64 })
        foreach (int palette in new[] { 1, 4, 64 })
        {
            var plugin = new MainPlugin();
            var infos = new CCheckTransmitInfoList();
            int id = 100;
            for (int i = 0; i < count; i++)
            {
                var viewer = new CCSPlayerController { Slot = i, TeamNum = (byte)(2 + i % 2) };
                plugin._prefs[i] = new Prefs { Glow_Color_CT = $"#{1 + i % palette:X6}", Glow_Color_T = $"#{100 + i % palette:X6}" };
                infos.Add((new CCheckTransmitInfo { TransmitEntities = new() }, viewer));
                plugin.g_Main.Player_Data[i] = new Globals.PlayerDataClass { Player = viewer, ModelRelay = Prop(id++) };
            }
            foreach (var data in plugin.g_Main.Player_Data.Values)
            foreach (var viewer in infos)
            {
                int color = plugin.GetViewerGlowColor(viewer.Item2!.Slot, data.Player.TeamNum).ToArgb();
                if (!data.ModelGlows.ContainsKey(color)) data.ModelGlows[color] = Prop(id++);
            }
            foreach (var data in plugin.g_Main.Player_Data.Values)
            {
                plugin.TrackEspEntity(data.ModelRelay!);
                foreach (var glow in data.ModelGlows.Values) plugin.TrackEspEntity(glow);
            }
            var viewers = infos.Select(info => info.Item2!).ToList();
            foreach (bool enemy in new[] { false, true })
            foreach (bool gotv in new[] { false, true })
            {
                Configs.Instance.ShowOnlyEnemyTeam = enemy; Configs.Instance.DisableGlowOnGOTV = gotv;
                Equal(plugin, infos); CheckDemand(plugin, viewers);
                viewers[0].IsBot = true; CheckDemand(plugin, viewers);
                viewers[0].IsHLTV = true; Equal(plugin, infos); CheckDemand(plugin, viewers);
                viewers[0].IsHLTV = viewers[0].IsBot = false;
                viewers[0].TeamNum = 1; Equal(plugin, infos); CheckDemand(plugin, viewers);
                viewers[0].TeamNum = 2;
            }
            Equal(plugin, infos);
            Measure($"{count}/{palette}/baseline", () => Baseline(plugin, infos));
            var oldEntities = new Dictionary<int, HashSet<int>>();
            var newEntities = new Dictionary<int, HashSet<int>>();
            var original = Reconcile(plugin, viewers, false, oldEntities);
            var optimized = Reconcile(plugin, viewers, true, newEntities);
            Console.WriteLine($"glows/{count}/{palette},before={original.Created},after={optimized.Created}");
            var expected = Copy(infos, false);
            Baseline(plugin, expected);
            foreach (var (slot, data) in plugin.g_Main.Player_Data)
            {
                foreach (var (color, prop) in data.ModelGlows)
                {
                    if (newEntities[slot].Contains(color)) continue;
                    data.ModelGlows.Remove(color);
                    plugin.RemoveEspEntity(prop);
                }
                if (newEntities[slot].Count == 0)
                {
                    plugin.RemoveEspEntity(data.ModelRelay);
                    data.ModelRelay = null;
                }
            }
            var actual = Copy(infos, false);
            plugin.Run(actual);
            for (int i = 0; i < actual.Count; i++)
                if (!actual[i].Item1.TransmitEntities.SetEquals(expected[i].Item1.TransmitEntities))
                    throw new Exception("Pruning changed visible entities");
            Measure($"{count}/{palette}/cached", () => plugin.Run(infos));
            Measure($"{count}/{palette}/world-rebuild", () =>
            {
                Configs.Instance.DisableGlowOnGOTV = !Configs.Instance.DisableGlowOnGOTV;
                plugin.Run(infos);
            });
            Measure($"{count}/{palette}/rebuild", () => { plugin.ForgetViewer(0); plugin.Run(infos); });
            Measure($"{count}/{palette}/timer-baseline-model", () => Reconcile(plugin, viewers, false, oldEntities));
            Measure($"{count}/{palette}/timer-cached-model", () => Reconcile(plugin, viewers, true, newEntities));
            plugin._prefs[0].Glow_Color_CT = "#12ABCD";
            var after = Reconcile(plugin, viewers, true, newEntities);
            CheckDemand(plugin, viewers);
            Console.WriteLine($"entities/{count}/{palette}/color-change,created={after.Created},removed={after.Removed}");
            oldEntities.Clear(); newEntities.Clear();
            var respawn = Reconcile(plugin, viewers, true, newEntities);
            if (respawn.Created != optimized.Created + after.Created - after.Removed) throw new Exception("Respawn entity mismatch");
            Measure($"{count}/{palette}/respawn-model", () => { newEntities.Clear(); Reconcile(plugin, viewers, true, newEntities); });
            var first = plugin.g_Main.Player_Data[0];
            first.Player.Controlled = new CCSPlayerController { TeamNum = 3 }; Equal(plugin, infos); CheckDemand(plugin, viewers);
            first.Player.Controlled = null;
            var deleted = first.ModelRelay ??= Prop(id++);
            plugin.TrackEspEntity(deleted); plugin.OnEspEntityDeleted(deleted);
            // The deletion notification arrives before the native wrapper becomes invalid.
            foreach (var info in infos) info.Item1.TransmitEntities.Clear();
            plugin.Run(infos);
            foreach (var info in infos) if (info.Item1.TransmitEntities.Contains((int)deleted.Index)) throw new Exception("Deleted entity leaked");
            deleted.IsValid = false; Equal(plugin, infos);
            first.ModelRelay = Prop((int)deleted.Index); first.ModelRelay.EntityHandle = (900000, (int)deleted.Index); plugin.TrackEspEntity(first.ModelRelay); Equal(plugin, infos);
            plugin._prefs[0].Glow_Color_CT = "#ABCDEF"; Equal(plugin, infos);
            plugin._prefs.Remove(0); Equal(plugin, infos);
            CheckDemand(plugin, viewers); // Missing preferences use configured defaults.
            viewers.RemoveAt(0);
            CheckDemand(plugin, viewers);
            plugin.ForgetViewer(0); Equal(plugin, infos);
            plugin.ClearTransmitCache();
            // A new world may reuse the exact same raw handle. Old wrappers remain unowned.
            foreach (var info in infos) info.Item1.TransmitEntities.Clear();
            plugin.Run(infos);
            if (infos.Any(info => !info.Item1.TransmitEntities.IsEmpty)) throw new Exception("Old world entities transmitted");
            plugin.RemoveEspEntity(first.ModelRelay);
            if (first.ModelRelay.RemoveCalls != 0) throw new Exception("Unowned reused entity removed");
            foreach (var data in plugin.g_Main.Player_Data.Values)
            {
                if (data.ModelRelay!.IsValid) plugin.TrackEspEntity(data.ModelRelay);
                foreach (var glow in data.ModelGlows.Values) plugin.TrackEspEntity(glow);
            }
            Equal(plugin, infos);
        }
        Console.WriteLine("PASS visibility, teams, HLTV, spectators, controlled bots, empty/full initial bits, colors, late prefs, deletion, serial reuse, cache reset");
    }
}
