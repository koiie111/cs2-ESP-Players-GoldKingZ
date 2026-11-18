using System.Drawing;
using System.Reflection;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2MenuManager.API.Menu;
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

    private readonly Dictionary<ulong, GlowTeamChoice> _pendingColorInputs = new();

    [ConsoleCommand("css_color", "Open the personal ESP glow color menu")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnColorCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;

        EnsureMenuControlTranslations();

        if (_prefs == null || !_prefs.TryGetValue(player.Slot, out _))
        {
            PrintColorMessage(player, "Color.PrefsUnavailable");
            return;
        }

        _pendingColorInputs.Remove(player.SteamID);
        ShowColorTeamMenu(player);
    }

    private void ShowColorTeamMenu(CCSPlayerController player)
    {
        CreateColorTeamMenu(player).Display(player, 0);
    }

    private WasdMenu CreateColorTeamMenu(CCSPlayerController player)
    {
        var menu = new WasdMenu(Localizer.ForPlayer(player, "Color.Menu.Title"), this);

        string ctHex = GetEffectiveGlowHex(player.Slot, (byte)CsTeam.CounterTerrorist);
        menu.AddItem(
            $"{Localizer.ForPlayer(player, "Color.Menu.CT")} {ColorPreview(ctHex)}",
            (selectedPlayer, _) => ShowPresetColorMenu(selectedPlayer, GlowTeamChoice.CounterTerrorist));

        string tHex = GetEffectiveGlowHex(player.Slot, (byte)CsTeam.Terrorist);
        menu.AddItem(
            $"{Localizer.ForPlayer(player, "Color.Menu.T")} {ColorPreview(tHex)}",
            (selectedPlayer, _) => ShowPresetColorMenu(selectedPlayer, GlowTeamChoice.Terrorist));

        return menu;
    }

    private void ShowPresetColorMenu(CCSPlayerController player, GlowTeamChoice team)
    {
        byte teamNumber = TeamNumber(team);
        string currentHex = GetEffectiveGlowHex(player.Slot, teamNumber);
        string teamName = Localizer.ForPlayer(player, team == GlowTeamChoice.CounterTerrorist ? "Color.Menu.CT" : "Color.Menu.T");
        var menu = new WasdMenu(
            $"{teamName}: {Localizer.ForPlayer(player, "Color.Menu.Current")} {ColorPreview(currentHex)}",
            this)
        {
            PrevMenu = CreateColorTeamMenu(player)
        };

        foreach (string presetHex in PresetColors)
        {
            string selectedHex = presetHex;
            menu.AddItem(ColorPreview(selectedHex), (selectedPlayer, _) =>
            {
                ApplyGlowColor(selectedPlayer, team, selectedHex);
            });
        }

        menu.AddItem(Localizer.ForPlayer(player, "Color.Menu.Custom"), (selectedPlayer, _) =>
        {
            _pendingColorInputs[selectedPlayer.SteamID] = team;
            PrintColorMessage(selectedPlayer, "Color.Custom.Prompt");
            PrintColorMessage(selectedPlayer, "Color.Custom.Picker");
        });

        menu.Display(player, 0);
    }

    public HookResult OnColorInput(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || !_pendingColorInputs.TryGetValue(player.SteamID, out var team))
        {
            return HookResult.Continue;
        }

        string message = info.ArgString.Trim().Trim('"').Trim();
        if (message.Equals("cancel", StringComparison.OrdinalIgnoreCase)
            || message.Equals("!cancel", StringComparison.OrdinalIgnoreCase)
            || message.Equals("отмена", StringComparison.OrdinalIgnoreCase))
        {
            _pendingColorInputs.Remove(player.SteamID);
            PrintColorMessage(player, "Color.Custom.Cancelled");
            return HookResult.Handled;
        }

        if (!TryNormalizeHex(message, out string normalizedHex))
        {
            PrintColorMessage(player, "Color.Custom.Invalid");
            return HookResult.Handled;
        }

        _pendingColorInputs.Remove(player.SteamID);
        ApplyGlowColor(player, team, normalizedHex);
        return HookResult.Handled;
    }

    private bool ApplyGlowColor(CCSPlayerController player, GlowTeamChoice team, string hex)
    {
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

        Helper.RefreshViewerGlows(player);
        string teamName = Localizer.ForPlayer(player, team == GlowTeamChoice.CounterTerrorist ? "Color.Menu.CT" : "Color.Menu.T");
        Helper.AdvancedPlayerPrintToChat(player, null!, Localizer.ForPlayer(player, "Color.Applied"), teamName, hex);
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

    private static byte TeamNumber(GlowTeamChoice team)
        => team == GlowTeamChoice.CounterTerrorist
            ? (byte)CsTeam.CounterTerrorist
            : (byte)CsTeam.Terrorist;

    private static string ColorPreview(string hex)
        => $"<font color='{hex}'>■ {hex}</font>";

    private void PrintColorMessage(CCSPlayerController player, string localizationKey)
        => Helper.AdvancedPlayerPrintToChat(player, null!, Localizer.ForPlayer(player, localizationKey));

    private static void EnsureMenuControlTranslations()
    {
        try
        {
            Type? configManagerType = typeof(WasdMenu).Assembly.GetType("CS2MenuManager.API.Class.ConfigManager");
            object? config = configManagerType?
                .GetProperty("Config", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(null);
            var languages = config?.GetType().GetProperty("Lang")?.GetValue(config)
                as Dictionary<string, Dictionary<string, string>>;

            if (languages == null) return;

            MergeMenuLanguage(languages, "ru", new Dictionary<string, string>
            {
                ["ScrollKey"] = "[{0}/{1}] Перемещение",
                ["SelectKey"] = "[{0}] Выбрать",
                ["PrevKey"] = "[{0}] Назад",
                ["ExitKey"] = "[{0}] Закрыть"
            });
            MergeMenuLanguage(languages, "en", new Dictionary<string, string>
            {
                ["ScrollKey"] = "[{0}/{1}] Navigate",
                ["SelectKey"] = "[{0}] Select",
                ["PrevKey"] = "[{0}] Back",
                ["ExitKey"] = "[{0}] Close"
            });
        }
        catch (Exception exception)
        {
            Helper.DebugMessage($"Unable to register CS2MenuManager control translations: {exception.Message}", true);
        }
    }

    private static void MergeMenuLanguage(
        Dictionary<string, Dictionary<string, string>> languages,
        string language,
        Dictionary<string, string> translations)
    {
        if (!languages.TryGetValue(language, out var currentLanguage))
        {
            currentLanguage = new Dictionary<string, string>();
            languages[language] = currentLanguage;
        }

        foreach (var translation in translations)
        {
            currentLanguage[translation.Key] = translation.Value;
        }
    }
}
