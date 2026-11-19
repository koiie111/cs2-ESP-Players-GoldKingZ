using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using ESP_Players_GoldKingZ.Config;

namespace ESP_Players_GoldKingZ;

// Each value is the only viewer slot requesting this color, or -1 for multiple viewers.
// A target never needs a glow requested exclusively by its own slot.
internal sealed class GlowDemand
{
    private readonly Dictionary<int, int> _ct = new();
    private readonly Dictionary<int, int> _t = new();
    private readonly Dictionary<int, int> _other = new();

    public GlowDemand(MainPlugin plugin, List<CCSPlayerController> controllers)
    {
        var config = Configs.Instance;
        foreach (var viewer in controllers)
        {
            if (viewer.IsHLTV ? config.DisableGlowOnGOTV : viewer.IsBot) continue;
            int slot = viewer.Slot;
            byte team = viewer.TeamNum;
            var (ctColor, tColor) = plugin.GetGlowDemandColors(slot);
            if (!config.ShowOnlyEnemyTeam || team != 3)
                Request(_ct, ctColor, slot);
            if (!config.ShowOnlyEnemyTeam || team != 2)
                Request(_t, tColor, slot);
            Request(_other, tColor, slot);
        }
    }

    private static void Request(Dictionary<int, int> colors, int color, int slot)
    {
        if (!colors.TryAdd(color, slot)) colors[color] = -1;
    }

    public Dictionary<int, int> ForTeam(byte team) => team switch
    {
        3 => _ct,
        2 => _t,
        _ => _other
    };
}

public partial class Helper
{
    public static void ESP_Timer(IReadOnlyDictionary<int, Globals.PlayerDataClass>? spawns = null)
    {
        var plugin = MainPlugin.Instance;
        var controllers = Utilities.GetPlayers();
        var colors = new GlowDemand(plugin, controllers);
        foreach (var player in controllers)
        {
            if (!player.IsValid || player.IsHLTV) continue;
            if (spawns != null)
            {
                // An old spawn must not target the next connection in the same slot.
                if (!spawns.TryGetValue(player.Slot, out var queued)
                    || !plugin.g_Main.Player_Data.TryGetValue(player.Slot, out var current)
                    || !ReferenceEquals(queued, current)
                    || current.Player.EntityHandle.Raw != player.EntityHandle.Raw) continue;
            }
            CheckPlayerInGlobals(player);
            int slot = player.Slot;
            var data = plugin.g_Main.Player_Data[slot];
            var target = player.Get_CCSPlayerController_ControlledBot();
            // The relay is invisible and serves only as a parent for a requested glow.
            if (target is { IsValid: true })
            {
                var desired = colors.ForTeam(target.TeamNum);
                if (desired.Count > 1 || (desired.Count == 1 && !desired.ContainsValue(slot)))
                {
                    SetGlowPlayer(data, slot, target, desired);
                    continue;
                }
            }
            if (data.ModelRelay != null || data.ModelGlows.Count != 0) RemoveGlowData(data);
        }
    }
}
