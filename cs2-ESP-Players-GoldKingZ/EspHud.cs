using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using ESP_Players_GoldKingZ.Config;
using PanoramaManager;

namespace ESP_Players_GoldKingZ;

public partial class MainPlugin
{
    private const string EspLayout = "panorama/layout/custom_game/gkz_esp_colors.vxml_c";
    private PanelHandle? _espHud;
    private OwnedGameTimer? _idleHudTimer;
    private readonly Dictionary<int, EspHudView> _espHudViews = new();
    private readonly Dictionary<int, HexPrompt> _pendingColorInputs = new();

    private sealed class HexPrompt(GlowTeamChoice team)
    {
        public GlowTeamChoice Team { get; } = team;
        public OwnedGameTimer? Timeout;
    }

    private sealed class EspHudView(GlowTeamChoice team)
    {
        public GlowTeamChoice Team = team;
        public GlowTeamChoice? DrawnTeam;
        public string? Language;
        public string? PreviewHex;
        public int SelectedPreset = -1;
        public readonly byte?[] Levels = new byte?[9];
        public bool NeedsFullDraw = true;
        public bool RetryQueued;
    }

    private static readonly string[] ChannelPanels =
    [
        "esp_ct_dot_0", "esp_ct_dot_1", "esp_ct_dot_2",
        "esp_t_dot_0", "esp_t_dot_1", "esp_t_dot_2",
        "esp_outline_0", "esp_outline_1", "esp_outline_2"
    ];
    private static readonly string[] LevelClasses =
        Enumerable.Range(0, 256).Select(level => $"esp-level-{level:X2}").ToArray();

    [ConsoleCommand("css_esp", "Open or close ESP color settings")]
    [ConsoleCommand("css_wh", "Open or close ESP color settings")]
    [ConsoleCommand("css_color", "Open or close ESP color settings")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnEspHudCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!_runtimeActive || player is not { IsValid: true, IsBot: false, IsHLTV: false }) return;

        if (_espHudViews.ContainsKey(player.Slot) || _pendingColorInputs.ContainsKey(player.Slot))
            CloseEspHud(player.Slot);
        else
            OpenEspHud(player, GlowTeamChoice.CounterTerrorist);
    }

    private void OpenEspHud(CCSPlayerController player, GlowTeamChoice team)
    {
        if (!Configs.Instance.PanoramaMenuEnabled)
        {
            PrintColorMessage(player, "Color.Hud.Unavailable");
            return;
        }
        if (_prefs == null || !_prefs.TryGetValue(player.Slot, out _))
        {
            PrintColorMessage(player, "Color.PrefsUnavailable");
            return;
        }
        if (!Panorama.IsInitialised) Panorama.Init(this);
        if (!Panorama.CanReceiveClicks)
        {
            PrintColorMessage(player, "Color.Hud.Unavailable");
            return;
        }

        _idleHudTimer?.Cancel();
        _idleHudTimer = null;
        if (_espHud == null)
        {
            _espHud = Panorama.Spawn(EspLayout, new LayoutContract
            {
                RootPanelId = "EspMenu",
                RevealClass = "esp-open",
                RowCount = 0
            });
            _espHud.OnEvent += OnEspHudEvent;
        }

        var view = new EspHudView(team);
        _espHudViews[player.Slot] = view;
        _espHud.Open(player);
        if (_espHud.IsOpenFor(player) && _espHudViews.ContainsKey(player.Slot))
        {
            DrawEspHud(player, view);
            return;
        }

        ForgetEspHud(player.Slot);
        PrintColorMessage(player, "Color.Hud.Unavailable");
    }

    private void ReleaseIdleHud()
    {
        if (_espHud == null || _espHudViews.Count != 0 || _idleHudTimer != null) return;

        // PanoramaManager transmits the final close and input release for one second.
        // An explicitly owned one-shot avoids retaining every expired timer in BasePlugin.Timers.
        _idleHudTimer = new OwnedGameTimer(1.1f, () =>
        {
            _idleHudTimer = null;
            DisposeEspHud();
        });
    }

    private void DisposeEspHud()
    {
        _idleHudTimer?.Cancel();
        _idleHudTimer = null;
        if (_espHud == null) return;
        _espHud.OnEvent -= OnEspHudEvent;
        _espHud.Dispose();
        _espHud = null;
    }

    private void CloseAllEspHuds()
    {
        foreach (int slot in _espHudViews.Keys.ToArray()) CloseEspHud(slot);
        foreach (var prompt in _pendingColorInputs.Values) prompt.Timeout?.Cancel();
        _pendingColorInputs.Clear();
    }

    private void ForgetEspHud(int slot)
    {
        if (_pendingColorInputs.Remove(slot, out var prompt)) prompt.Timeout?.Cancel();
        _espHudViews.Remove(slot);
        ReleaseIdleHud();
    }

    private void CloseEspHud(int slot)
    {
        if (_espHudViews.ContainsKey(slot)) _espHud?.Close(slot);
        ForgetEspHud(slot);
    }

    private void OnEspHudEvent(PanelEvent evt)
    {
        var player = evt.Player;
        if (evt.Action == PanelAction.Close)
        {
            _espHudViews.Remove(player.Slot);
            ReleaseIdleHud();
            return;
        }

        if (evt.Action is not (PanelAction.Button or PanelAction.Restored)
            || player is not { IsValid: true, IsBot: false, IsHLTV: false }) return;

        if (!_runtimeActive || !_espHudViews.TryGetValue(player.Slot, out var view))
        {
            // A world reset can restore a library session on the frame after our own cleanup.
            if (evt.Action == PanelAction.Restored) evt.Menu.Close(player.Slot);
            return;
        }
        if (!evt.Menu.IsOpenFor(player) || _prefs == null || !_prefs.TryGetValue(player.Slot, out _)) return;

        if (evt.Action == PanelAction.Restored)
        {
            view.NeedsFullDraw = true;
        }
        else
        {
            switch (evt.ElementId)
            {
                case "esp_ct":
                    view.Team = GlowTeamChoice.CounterTerrorist;
                    break;
                case "esp_t":
                    view.Team = GlowTeamChoice.Terrorist;
                    break;
                case "esp_hex":
                    BeginHexInput(player, view.Team);
                    return;
                default:
                    int index = Array.IndexOf(PresetButtons, evt.ElementId);
                    if (index < 0 || !ApplyGlowColor(player, view.Team, PresetColors[index], announce: false)) return;
                    break;
            }
        }
        DrawEspHud(player, view);
    }

    private void BeginHexInput(CCSPlayerController player, GlowTeamChoice team)
    {
        int slot = player.Slot;
        CloseEspHud(slot);
        var prompt = new HexPrompt(team);
        _pendingColorInputs[slot] = prompt;
        prompt.Timeout = new OwnedGameTimer(30, () =>
        {
            if (_pendingColorInputs.TryGetValue(slot, out var current) && ReferenceEquals(current, prompt))
                _pendingColorInputs.Remove(slot);
        });
        PrintColorMessage(player, "Color.Hud.Prompt");
    }

    private void ResumeEspHudAfterHex(CCSPlayerController player, HexPrompt prompt)
    {
        prompt.Timeout?.Cancel();
        prompt.Timeout = null;
        int slot = player.Slot;
        uint connection = player.EntityHandle.Raw;
        Server.NextFrame(() =>
        {
            if (!_pendingColorInputs.TryGetValue(slot, out var current) || !ReferenceEquals(current, prompt)) return;
            _pendingColorInputs.Remove(slot);
            if (_runtimeActive && Utilities.GetPlayerFromSlot(slot) is { IsValid: true } owner
                && owner.EntityHandle.Raw == connection) OpenEspHud(owner, prompt.Team);
        });
    }

    private void DrawEspHud(CCSPlayerController player, EspHudView view, bool allowRetry = true)
    {
        if (_espHud is not { } menu) return;
        var colors = GetViewerColors(player.Slot);
        bool ct = view.Team == GlowTeamChoice.CounterTerrorist;
        string hex = ct ? colors.CtHex : colors.THex;
        bool full = view.NeedsFullDraw;
        bool success = true;
        int preset = Array.IndexOf(PresetColors, hex);
        string language = player.GetLanguage().Name;
        bool translate = full || view.Language != language;
        if (translate)
        {
            menu.SetVariableFor(player, "esp_title", Localizer.ForPlayer(player, "Color.Hud.Title"))
                .SetVariableFor(player, "esp_ct_label", Localizer.ForPlayer(player, "Color.Hud.CT"))
                .SetVariableFor(player, "esp_t_label", Localizer.ForPlayer(player, "Color.Hud.T"))
                .SetVariableFor(player, "esp_hex_button", Localizer.ForPlayer(player, "Color.Hud.Custom"));
        }
        if (translate || view.SelectedPreset != preset)
        {
            string key = preset < 0 ? "Color.Hud.CustomName" : $"Color.Hud.{hex[1..]}";
            menu.SetVariableFor(player, "esp_color_name", Localizer.ForPlayer(player, key));
        }
        if (full || view.PreviewHex != hex) menu.SetVariableFor(player, "esp_hex_value", hex);
        if (full || view.DrawnTeam != view.Team)
        {
            success &= menu.TrySetClassFor(player, "esp_ct", "esp-selected", ct);
            success &= menu.TrySetClassFor(player, "esp_t", "esp-selected", !ct);
            success &= menu.TrySetClassFor(player, "esp_preview", "esp-team-t", !ct);
        }
        if (full || view.SelectedPreset != preset)
        {
            for (int i = -1; i < PresetButtons.Length; i++)
            {
                if (!full && i != view.SelectedPreset && i != preset) continue;
                string button = i < 0 ? "esp_hex" : PresetButtons[i];
                success &= menu.TrySetClassFor(player, button, "esp-selected", i == preset);
            }
        }

        success &= Colorize(menu, player, view, 0, colors.CTColor);
        success &= Colorize(menu, player, view, 3, colors.TColor);
        success &= Colorize(menu, player, view, 6, ct ? colors.CTColor : colors.TColor);
        view.DrawnTeam = view.Team;
        view.PreviewHex = hex;
        view.SelectedPreset = preset;
        view.Language = language;
        view.NeedsFullDraw = !success;
        if (!success && allowRetry && !view.RetryQueued) RetryEspHudDraw(menu, player, view);
    }

    // A round restore can precede a writable layout. Retry once without requiring a click.
    private void RetryEspHudDraw(PanelHandle menu, CCSPlayerController player, EspHudView view)
    {
        int slot = player.Slot;
        view.RetryQueued = true;
        Server.NextFrame(() =>
        {
            view.RetryQueued = false;
            if (view.NeedsFullDraw && _runtimeActive && _espHudViews.GetValueOrDefault(slot) == view
                && player.IsValid && menu.IsOpenFor(player))
                DrawEspHud(player, view, allowRetry: false);
        });
    }

    private static bool Colorize(PanelHandle menu, CCSPlayerController player, EspHudView view, int first, System.Drawing.Color color)
    {
        bool success = true;
        ReadOnlySpan<byte> rgb = [color.R, color.G, color.B];
        for (int channel = 0; channel < 3; channel++)
        {
            int i = first + channel;
            byte intensity = rgb[channel];
            byte? old = view.Levels[i];
            bool changed = old != intensity;
            if (!changed && !view.NeedsFullDraw) continue;
            if (changed && old.HasValue)
            {
                if (!menu.TrySetClassFor(player, ChannelPanels[i], LevelClasses[old.Value], false))
                {
                    success = false;
                    continue;
                }
                view.Levels[i] = null;
            }
            if (menu.TrySetClassFor(player, ChannelPanels[i], LevelClasses[intensity], true))
                view.Levels[i] = intensity;
            else success = false;
        }
        return success;
    }
}
