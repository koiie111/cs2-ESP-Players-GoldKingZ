using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.RegularExpressions;
using ESP_Players_GoldKingZ.Config;
using System.Drawing;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using System.Security.Cryptography;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using Microsoft.VisualBasic;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Entities.Constants;

namespace ESP_Players_GoldKingZ;

public class Helper
{
    public static void RegisterCssCommands(string[]? commands, string description, CommandInfo.CommandCallback callback)
    {
        if (commands == null || commands.Length == 0) return;

        foreach (var cmd in commands)
        {
            if (string.IsNullOrEmpty(cmd)) continue;
            MainPlugin.Instance.AddCommand(cmd, description, callback);
        }
    }


    public static void RemoveCssCommands(string[]? commands, CommandInfo.CommandCallback callback)
    {
        if (commands == null || commands.Length == 0) return;

        foreach (var cmd in commands)
        {
            if (string.IsNullOrEmpty(cmd)) continue;
            MainPlugin.Instance.RemoveCommand(cmd, callback);
        }
    }

    public static void RegisterCssListener(string[]? commands, CommandInfo.CommandListenerCallback callback)
    {
        if (commands == null || commands.Length == 0) return;

        foreach (var cmd in commands)
        {
            if (string.IsNullOrEmpty(cmd)) continue;
            MainPlugin.Instance.AddCommandListener(cmd, callback, HookMode.Pre);
        }
    }

    public static void RemoveCssListener(string[]? commands, CommandInfo.CommandListenerCallback callback)
    {
        if (commands == null || commands.Length == 0) return;

        foreach (var cmd in commands)
        {
            if (string.IsNullOrEmpty(cmd)) continue;
            MainPlugin.Instance.RemoveCommandListener(cmd, callback, HookMode.Pre);
        }
    }

    public static void AdvancedPlayerPrintToChat(CCSPlayerController player, CounterStrikeSharp.API.Modules.Commands.CommandInfo commandInfo, string message, params object[] args)
    {
        if (string.IsNullOrEmpty(message)) return;

        for (int i = 0; i < args.Length; i++)
        {
            message = message.Replace($"{{{i}}}", args[i]?.ToString() ?? "");
        }

        if (Regex.IsMatch(message, "{nextline}", RegexOptions.IgnoreCase))
        {
            string[] parts = Regex.Split(message, "{nextline}", RegexOptions.IgnoreCase);
            foreach (string part in parts)
            {
                string trimmedPart = part.Trim();
                trimmedPart = trimmedPart.ReplaceColorTags();
                if (!string.IsNullOrEmpty(trimmedPart))
                {
                    if (commandInfo != null && commandInfo.CallingContext == CounterStrikeSharp.API.Modules.Commands.CommandCallingContext.Console)
                    {
                        player.PrintToConsole(" " + trimmedPart);
                    }
                    else
                    {
                        player.PrintToChat(" " + trimmedPart);
                    }
                }
            }
        }
        else
        {
            message = message.ReplaceColorTags();
            if (commandInfo != null && commandInfo.CallingContext == CounterStrikeSharp.API.Modules.Commands.CommandCallingContext.Console)
            {
                player.PrintToConsole(message);
            }
            else
            {
                player.PrintToChat(message);
            }
        }
    }

    public static void AdvancedPlayerPrintToConsole(CCSPlayerController player, string message, params object[] args)
    {
        if (string.IsNullOrEmpty(message)) return;

        for (int i = 0; i < args.Length; i++)
        {
            message = message.Replace($"{{{i}}}", args[i].ToString() ?? "");
        }
        if (Regex.IsMatch(message, "{nextline}", RegexOptions.IgnoreCase))
        {
            string[] parts = Regex.Split(message, "{nextline}", RegexOptions.IgnoreCase);
            foreach (string part in parts)
            {
                string trimmedPart = part.Trim();
                trimmedPart = trimmedPart.ReplaceColorTags();
                if (!string.IsNullOrEmpty(trimmedPart))
                {
                    player.PrintToConsole(" " + trimmedPart);
                }
            }
        }
        else
        {
            message = message.ReplaceColorTags();
            player.PrintToConsole(message);
        }
    }
    //----
    public static bool IsPlayerInGroupPermission(CCSPlayerController player, string groups)
    {
        if (string.IsNullOrEmpty(groups) || player == null || !player.IsValid)
            return false;

        return groups.Split('|')
            .Select(segment => segment.Trim())
            .Any(trimmedSegment => Permission_CheckPermissionSegment(player, trimmedSegment));
    }

    private static bool Permission_CheckPermissionSegment(CCSPlayerController player, string segment)
    {
        if (string.IsNullOrEmpty(segment)) return false;

        int colonIndex = segment.IndexOf(':');
        if (colonIndex == -1 || colonIndex == 0) return false;

        string prefix = segment.Substring(0, colonIndex).Trim().ToLower();
        string values = segment.Substring(colonIndex + 1).Trim();

        return prefix switch
        {
            "steamid" or "steamids" or "steam" or "steams" => Permission_CheckSteamIds(player, values),
            "flag" or "flags" => Permission_CheckFlags(player, values),
            "group" or "groups" => Permission_CheckGroups(player, values),
            _ => false
        };
    }

    private static bool Permission_CheckSteamIds(CCSPlayerController player, string steamIds)
    {
        if (string.IsNullOrEmpty(steamIds)) return false;

        steamIds = steamIds.Replace("[", "").Replace("]", "");

        var (steam2, steam3, steam32, steam64) = player.SteamID.GetPlayerSteamID();
        var steam3NoBrackets = steam3.Trim('[', ']');

        return steamIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim())
            .Any(trimmedId =>
                string.Equals(trimmedId, steam2, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmedId, steam3NoBrackets, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmedId, steam32, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmedId, steam64, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static bool Permission_CheckFlags(CCSPlayerController player, string flags)
    {
        if (player == null || !player.IsValid ||
            player.Connected != PlayerConnectedState.Connected ||
            player.IsBot || player.IsHLTV)
            return false;

        if (string.IsNullOrEmpty(flags))
            return false;

        var playerData = AdminManager.GetPlayerAdminData(player);
        if (playerData == null)
            return false;

        var requiredFlags = flags
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToList();

        if (playerData._flags != null &&
            requiredFlags.Any(reqFlag =>
                playerData._flags.Contains(reqFlag, StringComparer.OrdinalIgnoreCase)))
            return true;

        var allFlags = playerData.GetAllFlags();
        return allFlags != null &&
            requiredFlags.Any(reqFlag =>
                allFlags.Contains(reqFlag, StringComparer.OrdinalIgnoreCase));
    }

    private static bool Permission_CheckGroups(CCSPlayerController player, string groups)
    {
        if (player == null || !player.IsValid ||
            player.Connected != PlayerConnectedState.Connected ||
            player.IsBot || player.IsHLTV)
            return false;

        if (string.IsNullOrEmpty(groups))
            return false;

        var playerData = AdminManager.GetPlayerAdminData(player);
        if (playerData == null || playerData.Groups == null || !playerData.Groups.Any())
            return false;

        return groups
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(g => g.Trim())
            .Any(reqGroup => playerData.Groups.Contains(reqGroup, StringComparer.OrdinalIgnoreCase));
    }

    public static List<CCSPlayerController> GetPlayersController(bool IncludeBots = false, bool IncludeHLTV = false, bool IncludeNone = true, bool IncludeSPEC = true, bool IncludeCT = true, bool IncludeT = true)
    {
        return Utilities
            .FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller")
            .Where(p =>
                p != null &&
                p.IsValid &&
                p.Connected == PlayerConnectedState.Connected &&
                (IncludeBots || !p.IsBot) &&
                (IncludeHLTV || !p.IsHLTV) &&
                ((IncludeCT && p.TeamNum == (byte)CsTeam.CounterTerrorist) ||
                (IncludeT && p.TeamNum == (byte)CsTeam.Terrorist) ||
                (IncludeNone && p.TeamNum == (byte)CsTeam.None) ||
                (IncludeSPEC && p.TeamNum == (byte)CsTeam.Spectator)))
            .ToList();
    }
    public static int GetPlayersCount(bool IncludeBots = false, bool IncludeHLTV = false, bool IncludeSPEC = true, bool IncludeCT = true, bool IncludeT = true)
    {
        return Utilities.GetPlayers().Count(p =>
            p != null &&
            p.IsValid &&
            p.Connected == PlayerConnectedState.Connected &&
            (IncludeBots || !p.IsBot) &&
            (IncludeHLTV || !p.IsHLTV) &&
            ((IncludeCT && p.TeamNum == (byte)CsTeam.CounterTerrorist) ||
            (IncludeT && p.TeamNum == (byte)CsTeam.Terrorist) ||
            (IncludeSPEC && p.TeamNum == (byte)CsTeam.Spectator))
        );
    }

    public static void ClearVariables()
    {
        var g_Main = MainPlugin.Instance.g_Main;

        g_Main.Clear();
    }

    
    public static void DebugMessage(string message, bool important = false, Con? prefixColor = null)
    {
        const string prefix = "[ESP Players]";
        if (!Configs.Instance.EnableDebug && !important) return;
        Con defaultColor = important ? Con.Red : Con.Magenta;
        prefixColor ??= Con.Purple;
        Con.WriteLine($"{prefixColor}{prefix}: {defaultColor}{message}{Con.Reset}");
    }

    public static void MuteCommands(CounterStrikeSharp.API.Modules.UserMessages.UserMessage? um, int Config, bool Fully = false)
    {
        if (um == null) return;
        if ((!Fully && Config > 0) || (Fully && Config == 2))
        {
            um.Recipients.Clear();
        }
    }

    public static void CheckPlayerInGlobals(CCSPlayerController player)
    {
        if(player == null || !player.IsValid) return;

        var g_Main = MainPlugin.Instance.g_Main;
        if (!g_Main.Player_Data.ContainsKey(player.Slot))
        {
            var initialData = new Globals.PlayerDataClass(
                player,
                "",
                false,
                null!,
                DateTime.MinValue
            );
            g_Main.Player_Data.TryAdd(player.Slot, initialData);
        }else
        {
            g_Main.Player_Data[player.Slot].Player = player;
        }
    }

    public static CCSGameRules? GetGameRules()
    {
        try
        {
            var gameRulesEntities = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules");
            return gameRulesEntities.First().GameRules;
        }
        catch
        {
            return null;
        }
    }
    
    public static bool IsWarmup()
    {
        return GetGameRules()?.WarmupPeriod ?? false;
    }

    public static void RemoveGlow(CCSPlayerController player)
    {
        var g_Main = MainPlugin.Instance.g_Main;
        if(player == null || !player.IsValid) return;

        if (!g_Main.Player_Data.TryGetValue(player.Slot, out var handle))return;

        if(handle.ModelRelay != null && handle.ModelRelay.IsValid)
        {
            handle.ModelRelay.Remove();
        }
        handle.ModelRelay = null!;

        foreach (var glow in handle.ModelGlows.Values)
        {
            if (glow != null && glow.IsValid)
            {
                glow.Remove();
            }
        }
        handle.ModelGlows.Clear();

        handle.Player_ModelName = "";
    }

    public static void SetGlowPlayer(CCSPlayerController Getplayer)
    {
        var g_Main = MainPlugin.Instance.g_Main;
        if(Getplayer == null 
        || !Getplayer.IsValid
        || !g_Main.Player_Data.TryGetValue(Getplayer.Slot, out var handle)) return;

        var player = Getplayer.Get_CCSPlayerController_ControlledBot();
        if(player == null || !player.IsValid) return;

        string player_model = "";
        if (player.IsAlive())
        {
            player_model = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName ?? "";
        }

        bool hasGlowSetup = handle.ModelRelay != null && handle.ModelRelay.IsValid;
        if(hasGlowSetup)
        {
            bool ModelChanged = !string.IsNullOrEmpty(player_model) && !string.IsNullOrEmpty(handle.Player_ModelName) && handle.Player_ModelName != player_model;

            if(!ModelChanged && player.IsAlive())
            {
                EnsureViewerGlows(handle, player, player_model);
                return;
            }

            RemoveGlow(player);

            if(!player.IsAlive()) return;
        }

        RemoveGlow(player);

        if (!player.IsAlive()
        || string.IsNullOrEmpty(player_model))return;

        string uniqueName_ModelRelay = "MR_" + Guid.NewGuid().ToString("N");
        handle.ModelRelay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic")!;
        if (handle.ModelRelay == null)return;
        handle.ModelRelay.Spawnflags = 256u;
        handle.ModelRelay.SetModel(player_model);
        handle.ModelRelay.DispatchSpawn();
        if(handle.ModelRelay.Entity != null)handle.ModelRelay.Entity.Name = uniqueName_ModelRelay;
        handle.ModelRelay.RenderMode = RenderMode_t.kRenderNone;
        handle.ModelRelay.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NONE;

        handle.ModelRelay.AcceptInput("FollowEntity", player.PlayerPawn.Value, handle.ModelRelay, "!activator");
        handle.Player_ModelName = player_model;
        EnsureViewerGlows(handle, player, player_model);
    }

    private static void EnsureViewerGlows(Globals.PlayerDataClass handle, CCSPlayerController target, string playerModel)
    {
        var desiredColors = new HashSet<int>();
        foreach (var viewer in GetPlayersController(false))
        {
            if (viewer == null || !viewer.IsValid || viewer.IsBot || viewer.IsHLTV) continue;
            Color desiredColor = MainPlugin.Instance.GetViewerGlowColor(viewer.Slot, target.TeamNum);
            int colorKey = desiredColor.ToArgb();
            desiredColors.Add(colorKey);
            if (handle.ModelGlows.TryGetValue(colorKey, out var existing) && existing != null && existing.IsValid) continue;

            var glow = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (glow == null) continue;

            glow.Spawnflags = 256u;
            glow.SetModel(playerModel);
            glow.DispatchSpawn();
            if (glow.Entity != null) glow.Entity.Name = $"MG_{target.Slot}_{unchecked((uint)colorKey):X8}_{Guid.NewGuid():N}";
            glow.Render = Color.FromArgb(1, 0, 0, 0);
            glow.Glow.GlowColorOverride = desiredColor;
            glow.Glow.GlowRange = Configs.Instance.GlowRange;
            glow.Glow.GlowTeam = -1;
            glow.Glow.GlowType = Configs.Instance.GlowType ? 2 : 3;
            glow.Glow.GlowRangeMin = 100;
            glow.AcceptInput("FollowEntity", handle.ModelRelay, glow, "!activator");
            handle.ModelGlows[colorKey] = glow;
        }

        foreach (int staleColor in handle.ModelGlows.Keys.Where(key => !desiredColors.Contains(key)).ToArray())
        {
            if (handle.ModelGlows.Remove(staleColor, out var staleGlow) && staleGlow != null && staleGlow.IsValid)
            {
                staleGlow.Remove();
            }
        }
    }

    public static void RefreshViewerGlows(CCSPlayerController viewer)
    {
        if (viewer == null || !viewer.IsValid) return;

        foreach (var data in MainPlugin.Instance.g_Main.Player_Data.Values)
        {
            var target = data.Player?.Get_CCSPlayerController_ControlledBot();
            if (target == null || !target.IsValid || !target.IsAlive() || data.ModelRelay == null || !data.ModelRelay.IsValid) continue;
            EnsureViewerGlows(data, target, data.Player_ModelName);
        }
    }

    public static void RemoveViewerGlows(int viewerSlot)
    {
        foreach (var data in MainPlugin.Instance.g_Main.Player_Data.Values)
        {
            var target = data.Player?.Get_CCSPlayerController_ControlledBot();
            if (target == null || !target.IsValid || !target.IsAlive() || data.ModelRelay == null || !data.ModelRelay.IsValid) continue;
            EnsureViewerGlows(data, target, data.Player_ModelName);
        }
    }

    public static void StartTimer()
    {
        var g_Main = MainPlugin.Instance.g_Main;
        if(g_Main.Timer == null)
        {
            g_Main.Timer = MainPlugin.Instance.AddTimer(3.0f, () => ESP_Timer(), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }
    }

    public static void ESP_Timer()
    {
        foreach(var players in GetPlayersController(true))
        {
            if(players == null || !players.IsValid) continue;

            CheckPlayerInGlobals(players);
            
            SetGlowPlayer(players);
        }
    }

    public static void ReloadPlayersGlobals()
    {
        foreach (var players in GetPlayersController(true))
        {
            if(players == null || !players.IsValid) continue;

            CheckPlayerInGlobals(players);
        }
    }

    public static void RemoveAllPlayersGlow()
    {
        foreach (var data in MainPlugin.Instance.g_Main.Player_Data.Values)
        {
            var ModelRelay = data.ModelRelay;

            if(ModelRelay != null && ModelRelay.IsValid)
            {
                ModelRelay.Remove();
            }
            foreach (var glow in data.ModelGlows.Values)
            {
                if (glow != null && glow.IsValid)
                {
                    glow.Remove();
                }
            }
        }
        MainPlugin.Instance.g_Main.Player_Data?.Clear();
    }

    public static void RegisterCommandsAndHooks()
    {
        MainPlugin.Instance.RegisterListener<Listeners.OnMapStart>(MainPlugin.Instance.OnMapStart);
        MainPlugin.Instance.RegisterListener<Listeners.OnClientPutInServer>(MainPlugin.Instance.OnClientPutInServer);
        MainPlugin.Instance.RegisterListener<Listeners.CheckTransmit>(MainPlugin.Instance.OnCheckTransmit);
        MainPlugin.Instance.RegisterListener<Listeners.OnMapEnd>(MainPlugin.Instance.OnMapEnd);

        MainPlugin.Instance.RegisterEventHandler<EventRoundStart>(MainPlugin.Instance.OnEventRoundStart);
        MainPlugin.Instance.RegisterEventHandler<EventPlayerDeath>(MainPlugin.Instance.OnEventPlayerDeath);
        MainPlugin.Instance.RegisterEventHandler<EventPlayerDisconnect>(MainPlugin.Instance.OnEventPlayerDisconnect);

        MainPlugin.Instance.AddCommandListener("say", MainPlugin.Instance.OnPlayerSay, HookMode.Post);
        MainPlugin.Instance.AddCommandListener("say_team", MainPlugin.Instance.OnPlayerSay_Team, HookMode.Post);
        MainPlugin.Instance.AddCommandListener("say", MainPlugin.Instance.OnColorInput, HookMode.Pre);
        MainPlugin.Instance.AddCommandListener("say_team", MainPlugin.Instance.OnColorInput, HookMode.Pre);
        MainPlugin.Instance.HookUserMessage(118, MainPlugin.Instance.OnUserMessage_OnSayText2, HookMode.Pre);

        RegisterCssCommands(Configs.Instance.Reload_Plugin.Reload_Plugin_CommandsInGame.ConvertCommands(), "Commands To Reload ESP Plugin", MainPlugin.Instance.Game_UserMessages.CommandsAction_ReloadPlugin);

        StartTimer();
    }

    public static void RemoveRegisterCommandsAndHooks()
    {
        MainPlugin.Instance.RemoveListener<Listeners.OnMapStart>(MainPlugin.Instance.OnMapStart);
        MainPlugin.Instance.RemoveListener<Listeners.OnClientPutInServer>(MainPlugin.Instance.OnClientPutInServer);
        MainPlugin.Instance.RemoveListener<Listeners.CheckTransmit>(MainPlugin.Instance.OnCheckTransmit);
        MainPlugin.Instance.RemoveListener<Listeners.OnMapEnd>(MainPlugin.Instance.OnMapEnd);

        MainPlugin.Instance.DeregisterEventHandler<EventRoundStart>(MainPlugin.Instance.OnEventRoundStart);
        MainPlugin.Instance.DeregisterEventHandler<EventPlayerDeath>(MainPlugin.Instance.OnEventPlayerDeath);
        MainPlugin.Instance.DeregisterEventHandler<EventPlayerDisconnect>(MainPlugin.Instance.OnEventPlayerDisconnect);

        MainPlugin.Instance.RemoveCommandListener("say", MainPlugin.Instance.OnPlayerSay, HookMode.Post);
        MainPlugin.Instance.RemoveCommandListener("say_team", MainPlugin.Instance.OnPlayerSay_Team, HookMode.Post);
        MainPlugin.Instance.RemoveCommandListener("say", MainPlugin.Instance.OnColorInput, HookMode.Pre);
        MainPlugin.Instance.RemoveCommandListener("say_team", MainPlugin.Instance.OnColorInput, HookMode.Pre);
        MainPlugin.Instance.UnhookUserMessage(118, MainPlugin.Instance.OnUserMessage_OnSayText2, HookMode.Pre);

        RemoveCssCommands(Configs.Instance.Reload_Plugin.Reload_Plugin_CommandsInGame.ConvertCommands(), MainPlugin.Instance.Game_UserMessages.CommandsAction_ReloadPlugin);
    }
}
