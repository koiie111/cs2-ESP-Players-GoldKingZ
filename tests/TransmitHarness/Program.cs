using System.Diagnostics;
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
        (new CCheckTransmitInfo { TransmitEntities = full ? new HashSet<int>(Enumerable.Range(0, 12000)) : new HashSet<int>() }, x.Item2)));
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
    static (int Created, int Removed) Reconcile(MainPlugin plugin, bool cached, Dictionary<int, HashSet<int>> entities)
    {
        var shared = new[] { new HashSet<int>(), new HashSet<int>() };
        if (cached)
            foreach (int viewer in plugin._prefs.Keys)
                for (int team = 2; team <= 3; team++) shared[team - 2].Add(plugin.GetViewerGlowColor(viewer, (byte)team).ToArgb());
        int created = 0, removed = 0;
        foreach (var (slot, target) in plugin.g_Main.Player_Data)
        {
            HashSet<int> desired;
            if (cached) desired = shared[target.Player.TeamNum - 2];
            else
            {
                desired = new HashSet<int>();
                foreach (int viewer in plugin._prefs.Keys.ToArray()) desired.Add(plugin.BaselineColor(viewer, target.Player.TeamNum).ToArgb());
            }
            if (!entities.TryGetValue(slot, out var current)) entities[slot] = current = new HashSet<int>();
            created += desired.Count(c => !current.Contains(c));
            removed += current.Count(c => !desired.Contains(c));
            current.Clear(); current.UnionWith(desired);
        }
        return (created, removed);
    }
    static void Main()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
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
                plugin.g_Main.Player_Data[i] = new PlayerData { Player = viewer, ModelRelay = Prop(id++) };
            }
            foreach (var data in plugin.g_Main.Player_Data.Values)
            foreach (var viewer in infos)
            {
                int color = plugin.GetViewerGlowColor(viewer.Item2!.Slot, data.Player.TeamNum).ToArgb();
                if (!data.ModelGlows.ContainsKey(color)) data.ModelGlows[color] = Prop(id++);
            }
            foreach (bool enemy in new[] { false, true })
            foreach (bool gotv in new[] { false, true })
            {
                Configs.Instance.ShowOnlyEnemyTeam = enemy; Configs.Instance.DisableGlowOnGOTV = gotv;
                Equal(plugin, infos);
                infos[0].Item2!.IsHLTV = true; Equal(plugin, infos); infos[0].Item2!.IsHLTV = false;
                infos[0].Item2!.TeamNum = 1; Equal(plugin, infos); infos[0].Item2!.TeamNum = 2;
            }
            Equal(plugin, infos);
            Measure($"{count}/{palette}/baseline", () => Baseline(plugin, infos));
            Measure($"{count}/{palette}/cached", () => plugin.Run(infos));
            Measure($"{count}/{palette}/rebuild", () => { plugin.ForgetViewer(0); plugin.Run(infos); });
            var oldEntities = new Dictionary<int, HashSet<int>>();
            var newEntities = new Dictionary<int, HashSet<int>>();
            if (Reconcile(plugin, false, oldEntities) != Reconcile(plugin, true, newEntities)) throw new Exception("Creation count mismatch");
            Measure($"{count}/{palette}/timer-baseline-model", () => Reconcile(plugin, false, oldEntities));
            Measure($"{count}/{palette}/timer-cached-model", () => Reconcile(plugin, true, newEntities));
            plugin._prefs[0].Glow_Color_T = "#12ABCD";
            var before = Reconcile(plugin, false, oldEntities);
            var after = Reconcile(plugin, true, newEntities);
            if (before != after) throw new Exception("Color transition entity mismatch");
            Console.WriteLine($"entities/{count}/{palette}/color-change,created={after.Created},removed={after.Removed}");
            oldEntities.Clear(); newEntities.Clear();
            if (Reconcile(plugin, false, oldEntities) != Reconcile(plugin, true, newEntities)) throw new Exception("Respawn entity mismatch");
            Measure($"{count}/{palette}/respawn-model", () => { newEntities.Clear(); Reconcile(plugin, true, newEntities); });
            var first = plugin.g_Main.Player_Data[0];
            first.Player.Controlled = new CCSPlayerController { TeamNum = 3 }; Equal(plugin, infos);
            first.Player.Controlled = null;
            var deleted = first.ModelRelay!;
            plugin.TrackEspEntity(deleted); plugin.OnEspEntityDeleted(deleted);
            // The deletion notification arrives before the native wrapper becomes invalid.
            foreach (var info in infos) info.Item1.TransmitEntities.Clear();
            plugin.Run(infos);
            foreach (var info in infos) if (info.Item1.TransmitEntities.Contains((int)deleted.Index)) throw new Exception("Deleted entity leaked");
            deleted.IsValid = false; Equal(plugin, infos);
            first.ModelRelay = Prop((int)deleted.Index); first.ModelRelay.EntityHandle = (900000, (int)deleted.Index); Equal(plugin, infos);
            plugin._prefs[0].Glow_Color_CT = "#ABCDEF"; Equal(plugin, infos);
            plugin._prefs.Remove(0); Equal(plugin, infos);
            if (Reconcile(plugin, false, oldEntities) != Reconcile(plugin, true, newEntities)) throw new Exception("Last color viewer mismatch");
            plugin.ForgetViewer(0); Equal(plugin, infos);
            plugin.ClearTransmitCache(); Equal(plugin, infos);
        }
        Console.WriteLine("PASS visibility, teams, HLTV, spectators, controlled bots, empty/full initial bits, colors, late prefs, deletion, serial reuse, cache reset");
    }
}
