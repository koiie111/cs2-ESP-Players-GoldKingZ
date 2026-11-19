using System.Drawing;
using System.Globalization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;

namespace ESP_Players_GoldKingZ;

public partial class MainPlugin
{
    private enum GlowTeamChoice
    {
        CounterTerrorist,
        Terrorist
    }

    private static readonly string[] PresetColors =
    [
        "#00BEFF",
        "#00FFFF",
        "#00FF66",
        "#FFFF00",
        "#FF8C00",
        "#FF3030",
        "#F3005D",
        "#B347FF",
        "#FFFFFF"
    ];
    private static readonly string[] PresetButtons = Array.ConvertAll(PresetColors, hex => $"esp_color_{hex[1..]}");

    // CounterStrikeSharp keys command listeners by delegate. Each registration needs its own method.
    public HookResult OnTeamColorInput(CCSPlayerController? player, CommandInfo info) => OnColorInput(player, info);

    public HookResult OnColorInput(CCSPlayerController? player, CommandInfo info)
    {
        if (_pendingColorInputs.Count == 0 || player is not { IsValid: true }
            || !_pendingColorInputs.TryGetValue(player.Slot, out var prompt) || prompt.Timeout == null)
        {
            return HookResult.Continue;
        }

        string message = info.ArgString.Trim().Trim('"').Trim();
        if (message.Equals("cancel", StringComparison.OrdinalIgnoreCase)
            || message.Equals("!cancel", StringComparison.OrdinalIgnoreCase)
            || message.Equals("отмена", StringComparison.OrdinalIgnoreCase))
        {
            PrintColorMessage(player, "Color.Custom.Cancelled");
            ResumeEspHudAfterHex(player, prompt);
            return HookResult.Handled;
        }

        if (message.Equals("!esp", StringComparison.OrdinalIgnoreCase)
            || message.Equals("!wh", StringComparison.OrdinalIgnoreCase)
            || message.Equals("!color", StringComparison.OrdinalIgnoreCase))
        {
            CloseEspHud(player.Slot);
            return HookResult.Handled;
        }

        if (!TryReadRgb(message, out int rgb))
        {
            PrintColorMessage(player, "Color.Custom.Invalid");
            return HookResult.Handled;
        }

        ApplyGlowColor(player, prompt.Team, $"#{rgb:X6}");
        ResumeEspHudAfterHex(player, prompt);
        return HookResult.Handled;
    }

    private bool ApplyGlowColor(CCSPlayerController player, GlowTeamChoice team, string hex, bool announce = true)
    {
        if (!_runtimeActive) return false;
        if (_prefs == null || !_prefs.TryGetValue(player.Slot, out var prefs))
        {
            PrintColorMessage(player, "Color.PrefsUnavailable");
            return false;
        }

        bool ct = team == GlowTeamChoice.CounterTerrorist;
        if ((ct ? prefs.Glow_Color_CT : prefs.Glow_Color_T) != hex)
        {
            var colors = GetViewerColors(player.Slot);
            bool changed = (ct ? colors.CtHex : colors.THex) != hex;
            if (ct) prefs.Glow_Color_CT = hex;
            else prefs.Glow_Color_T = hex;
            if (changed) ScheduleMenuColorChange(player.Slot, ct);
        }
        if (announce)
        {
            string teamName = Localizer.ForPlayer(player, team == GlowTeamChoice.CounterTerrorist ? "Color.Hud.CT" : "Color.Hud.T");
            Helper.AdvancedPlayerPrintToChat(player, null!, Localizer.ForPlayer(player, "Color.Applied"), teamName, hex);
        }
        return true;
    }

    private static bool TryReadRgb(string? value, out int rgb)
    {
        var hex = value.AsSpan().Trim();
        rgb = 0;
        return hex.Length == 7 && hex[0] == '#' && hex[1..].IndexOfAnyExcept("0123456789abcdefABCDEF") < 0
            && int.TryParse(hex[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
    }

    private static Color ResolveGlowColor(string? saved, string configured, out string hex)
    {
        if (!TryReadRgb(saved, out int rgb))
        {
            rgb = configured.ToColor().ToArgb() & 0xFFFFFF;
        }
        hex = $"#{rgb:X6}";
        return Color.FromArgb(unchecked((int)0xFF000000) | rgb);
    }

    private void PrintColorMessage(CCSPlayerController player, string localizationKey)
        => Helper.AdvancedPlayerPrintToChat(player, null!, Localizer.ForPlayer(player, localizationKey));
}
