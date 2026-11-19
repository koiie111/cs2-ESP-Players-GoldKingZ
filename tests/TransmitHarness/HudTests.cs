using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using ESP_Players_GoldKingZ;
using ESP_Players_GoldKingZ.Config;
using PanoramaManager;
using GameTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

static class HudTests
{
    private static void Check(bool condition, string failure)
    {
        if (!condition) throw new Exception(failure);
    }

    public static void Run()
    {
        var plugin = MainPlugin.Instance = new MainPlugin();
        plugin.StartEspRuntime();
        var first = new CCSPlayerController { Slot = 1, EntityHandle = (101, 1) };
        var second = new CCSPlayerController { Slot = 2, EntityHandle = (102, 2) };
        Utilities.Connected = [first, second];
        var config = Configs.Instance;
        plugin.OnEspHudCommand(first, new());
        Check(!Panorama.IsInitialised, "Disabled HUD installed hooks");
        config.PanoramaMenuEnabled = true;
        plugin.OnEspHudCommand(first, new());
        Check(!Panorama.IsInitialised, "Missing preferences installed hooks");
        plugin._prefs[first.Slot] = new();
        plugin._prefs[second.Slot] = new();
        plugin.OnEspHudCommand(first, new());
        var menu = Panorama.Live!;
        plugin.OnEspHudCommand(second, new());
        Check(menu.IsOpenFor(first) && menu.IsOpenFor(second), "Two viewers must share a handle");

        second.TeamNum = 3;
        var relay = new CDynamicProp { EntityHandle = (2000, 20) };
        var glow = new CDynamicProp { EntityHandle = (2001, 21) };
        plugin.TrackEspEntity(relay);
        plugin.TrackEspEntity(glow);
        plugin.g_Main.Player_Data[2] = new Globals.PlayerDataClass { Player = second, ModelRelay = relay };
        plugin.g_Main.Player_Data[2].ModelGlows[unchecked((int)0xFF00FF66)] = glow;
        var bits = new TransmitBits();
        var recipients = new CCheckTransmitInfoList { (new CCheckTransmitInfo { TransmitEntities = bits }, first) };
        plugin.Run(recipients);
        Check(!bits.Contains(21), "Initial transmit plan showed an unselected glow");

        int writes = menu.Writes;
        menu.Click(first, "esp_ct");
        menu.Click(first, "esp_color_00BEFF");
        menu.Click(first, "esp_color_00BEFF");
        Check(plugin._prefs[1].Glow_Color_CT == "#00BEFF", "Default choice must be stored explicitly");
        Check(menu.Writes == writes && Server.Frames.Count == 0, "Repeated selections redrew or scheduled glow work");
        string initialCt = config.Glow_Color_CT;
        config.Glow_Color_CT = "1, 2, 3, 120";
        Check(plugin.GetViewerGlowColor(1, 3).ToArgb() != plugin.GetViewerGlowColor(2, 3).ToArgb(),
            "Explicit choice followed a later server default");
        config.Glow_Color_CT = initialCt;

        menu.Click(first, "esp_color_00FFFF");
        Check(menu.Writes - writes == 8, "One-channel preset should write two labels, two buttons and four channel changes");
        Check(menu.Text[(2, "esp_hex_value")] == "#00BEFF" && menu.Classes.Contains((2, "esp_color_00BEFF", "esp-selected")),
            "A click changed the other viewer's HUD");
        menu.RefuseNextClassOn = "esp_ct_dot_2";
        menu.Click(first, "esp_color_00FF66");
        menu.Click(first, "esp_color_00FF66");
        Check(!menu.Classes.Contains((1, "esp_ct_dot_2", "esp-level-FF")) &&
            menu.Classes.Contains((1, "esp_ct_dot_2", "esp-level-66")), "Refused RGB write was never repaired");
        int beforeSweeps = Helper.Reconciles;
        Check(Server.Frames.Count == 1, "Burst scheduled more than one frame");
        Server.Flush();
        Check(Helper.Reconciles == beforeSweeps + 1, "Burst did not produce exactly one sweep");
        plugin.Run(recipients);
        Check(bits.Contains(21) && Server.Frames.Count == 0,
            "CheckTransmit missed the new glow or scheduled a duplicate sweep");

        menu.Click(first, "esp_t");
        writes = menu.Writes;
        var expected = menu.Classes.Where(c => c.Slot == first.Slot).ToHashSet();
        menu.Restore(first);
        Check(menu.Writes - writes == 28 && expected.SetEquals(menu.Classes.Where(c => c.Slot == first.Slot)),
            "Restore did not reapply all viewer state");
        menu.Click(first, "esp_hex");
        Check(GameTimer.OneShots.Count == 1 && menu.IsOpenFor(second), "Opening HEX released a shared handle");
        Check(plugin.OnTeamColorInput(first, new("bad")) == HookResult.Handled && GameTimer.OneShots.Count == 1,
            "Invalid team-chat input ended the active prompt");
        Check(plugin.OnColorInput(second, new("normal chat")) == HookResult.Continue,
            "A prompt captured another viewer's chat");
        Check(plugin.OnColorInput(first, new("#12ab34")) == HookResult.Handled && GameTimer.OneShots.Count == 0,
            "Accepted HEX left its timeout running");
        Check(plugin.OnColorInput(first, new("normal chat")) == HookResult.Continue, "Consumed a second chat line");
        Server.Flush();
        Check(menu.IsOpenFor(first) && menu.Text[(1, "esp_hex_value")] == "#12AB34" &&
            menu.Classes.Contains((1, "esp_t", "esp-selected")) && plugin._prefs[1].Glow_Color_T == "#12AB34",
            "HEX lost the selected team, normalization or stored value");
        menu.Click(first, "esp_hex");
        Check(plugin.OnColorInput(first, new("cancel")) == HookResult.Handled && GameTimer.OneShots.Count == 0,
            "Cancel leaked a prompt timer");
        var replacement = new CCSPlayerController { Slot = first.Slot, EntityHandle = (201, 1) };
        Utilities.Connected[0] = replacement;
        Server.Flush();
        Check(!menu.IsOpenFor(replacement), "Queued return took over a recycled slot");
        plugin.OnEspClientDisconnect(first.Slot);
        Server.Flush();

        menu.Close(second.Slot);
        Check(GameTimer.OneShots.Single().Interval > 1 && Panorama.Live == menu, "Last close skipped transmit grace");
        plugin.OnEspHudCommand(second, new());
        Check(GameTimer.OneShots.Count == 0 && Panorama.Live == menu, "Reopen during grace did not cancel disposal");
        menu.Click(second, "esp_hex");
        GameTimer.OneShots.Single(t => t.Interval < 2).Fire();
        Check(Panorama.Live == null && plugin.PendingInputs == 1, "Chat-only prompt kept an idle handle registered");
        GameTimer.OneShots.Single().Fire();
        Check(plugin.PendingInputs == 0 && plugin.OnColorInput(second, new("hello")) == HookResult.Continue,
            "Expired HEX still swallowed chat");

        plugin.OnEspHudCommand(second, new());
        Panorama.Live!.Click(second, "esp_hex");
        plugin.OnMapStart("next");
        Server.Flush();
        Check(plugin.PendingInputs == 0 && GameTimer.OneShots.All(t => t.Interval < 2), "Map change retained HEX timeout");
        GameTimer.OneShots.Single().Fire();
        Check(Panorama.Live == null && GameTimer.OneShots.Count == 0 && FunctionReference.Count == 0,
            "Idle HUD handle, timeout or registered callback survived cleanup");

        config.PanoramaMenuEnabled = false;
        plugin.StopEspRuntime();
        Console.WriteLine("PASS HUD: two viewers, 0 writes on repeat, 8 on one-channel preset, 28 on restore, coalesced reconciliation, private HEX, slot reuse and idle disposal");
    }
}
