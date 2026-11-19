using System.Drawing;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ESP_Players_GoldKingZ.Config;

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

    private static readonly Regex HexColorPattern = new(
        "^#(?<hex>[0-9a-fA-F]{6})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<int, GlowTeamChoice> _pendingColorInputs = new();

    // CounterStrikeSharp keys command listeners by delegate. Each registration needs its own method.
    public HookResult OnTeamColorInput(CCSPlayerController? player, CommandInfo info) => OnColorInput(player, info);

    public HookResult OnColorInput(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || !_pendingColorInputs.TryGetValue(player.Slot, out var team))
        {
            return HookResult.Continue;
        }

        string message = info.ArgString.Trim().Trim('"').Trim();
        if (message.Equals("cancel", StringComparison.OrdinalIgnoreCase)
            || message.Equals("!cancel", StringComparison.OrdinalIgnoreCase)
            || message.Equals("отмена", StringComparison.OrdinalIgnoreCase))
        {
            _pendingColorInputs.Remove(player.Slot);
            PrintColorMessage(player, "Color.Custom.Cancelled");
            ResumeEspHudAfterHex(player);
            return HookResult.Handled;
        }

        if (message.Equals("!esp", StringComparison.OrdinalIgnoreCase)
            || message.Equals("!wh", StringComparison.OrdinalIgnoreCase)
            || message.Equals("!color", StringComparison.OrdinalIgnoreCase))
        {
            CloseEspHud(player.Slot);
            return HookResult.Handled;
        }

        if (!TryNormalizeHex(message, out string normalizedHex))
        {
            PrintColorMessage(player, "Color.Custom.Invalid");
            return HookResult.Handled;
        }

        _pendingColorInputs.Remove(player.Slot);
        ApplyGlowColor(player, team, normalizedHex);
        ResumeEspHudAfterHex(player);
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

        if (team == GlowTeamChoice.CounterTerrorist)
        {
            prefs.Glow_Color_CT = hex;
        }
        else
        {
            prefs.Glow_Color_T = hex;
        }

        ScheduleEspReconcile();
        if (announce)
        {
            string teamName = Localizer.ForPlayer(player, team == GlowTeamChoice.CounterTerrorist ? "Color.Hud.CT" : "Color.Hud.T");
            Helper.AdvancedPlayerPrintToChat(player, null!, Localizer.ForPlayer(player, "Color.Applied"), teamName, hex);
        }
        return true;
    }

    private Color ParseViewerGlowColor(int viewerSlot, byte targetTeam)
    {
        string hex = GetEffectiveGlowHex(viewerSlot, targetTeam);
        return TryNormalizeHex(hex, out string normalizedHex)
            ? Color.FromArgb(
                255,
                Convert.ToInt32(normalizedHex.Substring(1, 2), 16),
                Convert.ToInt32(normalizedHex.Substring(3, 2), 16),
                Convert.ToInt32(normalizedHex.Substring(5, 2), 16))
            : Color.White;
    }

    private string GetEffectiveGlowHex(int viewerSlot, byte targetTeam)
    {
        if (_prefs != null && _prefs.TryGetValue(viewerSlot, out var prefs))
        {
            string savedHex = targetTeam == (byte)CsTeam.CounterTerrorist
                ? prefs.Glow_Color_CT
                : prefs.Glow_Color_T;

            if (TryNormalizeHex(savedHex, out string normalizedSavedHex))
            {
                return normalizedSavedHex;
            }
        }

        Color configuredColor = targetTeam == (byte)CsTeam.CounterTerrorist
            ? Configs.Instance.Glow_Color_CT.ToColor()
            : Configs.Instance.Glow_Color_T.ToColor();
        return $"#{configuredColor.R:X2}{configuredColor.G:X2}{configuredColor.B:X2}";
    }

    private static bool TryNormalizeHex(string? value, out string normalizedHex)
    {
        Match match = HexColorPattern.Match(value?.Trim() ?? "");
        normalizedHex = match.Success ? $"#{match.Groups["hex"].Value.ToUpperInvariant()}" : "";
        return match.Success;
    }

    private void PrintColorMessage(CCSPlayerController player, string localizationKey)
        => Helper.AdvancedPlayerPrintToChat(player, null!, Localizer.ForPlayer(player, localizationKey));
}
