using CS2MenuManager.API.Class;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using ESP_Players_GoldKingZ.Config;
using PanoramaManager;

namespace ESP_Players_GoldKingZ;

public partial class MainPlugin
{
    private const string EspLayout = "panorama/layout/custom_game/gkz_esp_colors.vxml_c";
    private PanelHandle? _espHud;
    private readonly Dictionary<int, EspHudView> _espHudViews = new();

    private sealed class EspHudView(GlowTeamChoice team)
    {
        public GlowTeamChoice Team { get; set; } = team;
        public bool AwaitingHex { get; init; }
        public string CtHex { get; set; } = "";
        public string THex { get; set; } = "";
        public string PreviewHex { get; set; } = "";
    }

    private string HudText(CCSPlayerController player, string key) => Localizer.ForPlayer(player, key);

    [ConsoleCommand("css_esp", "Open or close ESP color settings")]
    [ConsoleCommand("css_wh", "Open or close ESP color settings")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnEspHudCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (!_runtimeActive || player is not { IsValid: true, IsBot: false, IsHLTV: false }) return;

        if (_espHudViews.ContainsKey(player.Slot))
        {
            CloseEspHud(player.Slot);
            return;
        }

        if (!Configs.Instance.PanoramaMenuEnabled || !Panorama.CanReceiveClicks)
        {
            OnColorCommand(player, info);
            return;
        }

        _pendingColorInputs.Remove(player.Slot);
        MenuManager.CloseActiveMenu(player);
        OpenEspHud(player, GlowTeamChoice.CounterTerrorist);
    }

    private void OpenEspHud(CCSPlayerController player, GlowTeamChoice team)
    {
        if (!_runtimeActive || _prefs == null || !_prefs.TryGetValue(player.Slot, out _))
        {
            PrintColorMessage(player, "Color.PrefsUnavailable");
            return;
        }

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

        _espHudViews.Remove(player.Slot);
        PrintColorMessage(player, "Color.Hud.Fallback");
        EnsureMenuControlTranslations();
        ShowColorTeamMenu(player);
    }

    private void CloseAllEspHuds()
    {
        foreach (int slot in _espHudViews.Keys.ToArray()) CloseEspHud(slot);
    }

    private void ForgetEspHud(int slot) => _espHudViews.Remove(slot);

    private void CloseEspHud(int slot)
    {
        if (!_espHudViews.Remove(slot, out var view)) return;
        if (view.AwaitingHex) _pendingColorInputs.Remove(slot);
        else _espHud?.Close(slot);
    }

    private void OnEspHudEvent(PanelEvent evt)
    {
        var player = evt.Player;
        if (evt.Action == PanelAction.Close)
        {
            _espHudViews.Remove(player.Slot);
            return;
        }

        if (!_runtimeActive || player is not { IsValid: true, IsBot: false, IsHLTV: false }
            || !_espHudViews.TryGetValue(player.Slot, out var view) || view.AwaitingHex
            || !evt.Menu.IsOpenFor(player) || _prefs == null || !_prefs.TryGetValue(player.Slot, out _)) return;

        if (evt.Action == PanelAction.Restored)
        {
            DrawEspHud(player, view);
            return;
        }

        if (evt.Action != PanelAction.Button) return;
        switch (evt.ElementId)
        {
            case "esp_ct":
                view.Team = GlowTeamChoice.CounterTerrorist;
                break;
            case "esp_t":
                view.Team = GlowTeamChoice.Terrorist;
                break;
            case "esp_hex":
                CloseEspHud(player.Slot);
                var prompt = new EspHudView(view.Team) { AwaitingHex = true };
                int slot = player.Slot;
                _espHudViews[slot] = prompt;
                _pendingColorInputs[slot] = view.Team;
                PrintColorMessage(player, "Color.Hud.Prompt");
                AddTimer(30, () =>
                {
                    if (_espHudViews.TryGetValue(slot, out var current) && ReferenceEquals(current, prompt))
                        CloseEspHud(slot);
                }, TimerFlags.STOP_ON_MAPCHANGE);
                return;
            default:
                int index = Array.FindIndex(PresetColors, hex => evt.ElementId == $"esp_color_{hex[1..]}");
                if (index < 0 || !ApplyGlowColor(player, view.Team, PresetColors[index], announce: false)) return;
                break;
        }

        DrawEspHud(player, view);
    }

    private void ResumeEspHudAfterHex(CCSPlayerController player)
    {
        if (!_espHudViews.TryGetValue(player.Slot, out var prompt) || !prompt.AwaitingHex) return;

        // Wait until the chat command has finished before giving mouse input back to the HUD.
        Server.NextFrame(() =>
        {
            if (player is not { IsValid: true } || !_espHudViews.TryGetValue(player.Slot, out var current)
                || !ReferenceEquals(current, prompt)) return;

            _espHudViews.Remove(player.Slot);
            if (_runtimeActive) OpenEspHud(player, prompt.Team);
        });
    }

    private void DrawEspHud(CCSPlayerController player, EspHudView view)
    {
        if (_espHud is not { } menu) return;

        bool ctSelected = view.Team == GlowTeamChoice.CounterTerrorist;
        string ctHex = GetEffectiveGlowHex(player.Slot, (byte)CsTeam.CounterTerrorist);
        string tHex = GetEffectiveGlowHex(player.Slot, (byte)CsTeam.Terrorist);
        string previewHex = ctSelected ? ctHex : tHex;
        bool isPreset = Array.IndexOf(PresetColors, previewHex) >= 0;

        menu.SetVariableFor(player, "esp_title", HudText(player, "Color.Hud.Title"))
            .SetVariableFor(player, "esp_ct_label", HudText(player, "Color.Menu.CT"))
            .SetVariableFor(player, "esp_t_label", HudText(player, "Color.Menu.T"))
            .SetVariableFor(player, "esp_hex_button", HudText(player, "Color.Hud.Custom"))
            .SetVariableFor(player, "esp_color_name", HudText(player, isPreset ? $"Color.Hud.{previewHex[1..]}" : "Color.Hud.CustomName"))
            .SetVariableFor(player, "esp_hex_value", previewHex)
            .SetClassFor(player, "esp_ct", "esp-selected", ctSelected)
            .SetClassFor(player, "esp_t", "esp-selected", !ctSelected)
            .SetClassFor(player, "esp_preview", "esp-team-t", !ctSelected)
            .SetClassFor(player, "esp_hex", "esp-selected", !isPreset);

        foreach (string hex in PresetColors)
            menu.SetClassFor(player, $"esp_color_{hex[1..]}", "esp-selected", hex == previewHex);

        Colorize(menu, player, "esp_ct_dot", view.CtHex, ctHex);
        Colorize(menu, player, "esp_t_dot", view.THex, tHex);
        Colorize(menu, player, "esp_outline", view.PreviewHex, previewHex);
        view.CtHex = ctHex;
        view.THex = tHex;
        view.PreviewHex = previewHex;
    }

    // Source 2 can toggle classes. The three additive masks compose the RGB channels in the layout.
    private static void Colorize(PanelHandle menu, CCSPlayerController player, string panel, string previous, string hex)
    {
        for (int channel = 0; channel < 3; channel++)
        {
            int offset = 1 + 2 * channel;
            string intensity = hex.Substring(offset, 2);
            string id = $"{panel}_{channel}";
            if (previous.Length != 0)
            {
                string oldIntensity = previous.Substring(offset, 2);
                if (oldIntensity != intensity)
                    menu.SetClassFor(player, id, $"esp-level-{oldIntensity}", false);
            }
            menu.SetClassFor(player, id, $"esp-level-{intensity}", true);
        }
    }
}
