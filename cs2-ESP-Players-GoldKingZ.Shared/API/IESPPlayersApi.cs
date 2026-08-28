using CounterStrikeSharp.API.Core;

namespace ESP_Players_GoldKingZ.Shared;

public interface IESPPlayersApi
{
    /// <summary>
    /// True for every valid connected player. personalToggleOnly is retained for binary/source compatibility and is ignored.
    /// </summary>
    bool IsESPActive(CCSPlayerController player, bool personalToggleOnly = false);

    /// <summary>
    /// True for every valid connected player. personalToggleOnly is retained for binary/source compatibility and is ignored.
    /// </summary>
    bool IsESPActive(int slot, bool personalToggleOnly = false);

    /// <summary>
    /// True if the player matches the rule. Use a full permissionsString OR the named segments (full string wins).
    /// </summary>
    bool HasPermission(CCSPlayerController player, string permissionsString = "", string SteamIDs = "", string Flags = "", string Groups = "");

    /// <summary>
    /// True if the player matches the rule. Use a full permissionsString OR the named segments (full string wins).
    /// </summary>
    bool HasPermission(int slot, string permissionsString = "", string SteamIDs = "", string Flags = "", string Groups = "");

    /// <summary>
    /// Compatibility no-op: ESP is always enabled and cannot be revoked.
    /// </summary>
    void SetESP(CCSPlayerController player, bool state);

    /// <summary>
    /// Compatibility no-op: ESP is always enabled and cannot be revoked.
    /// </summary>
    void SetESP(int slot, bool state);

    /// <summary>
    /// Subscribes a handler fired when a player's granted-ESP changes. Safe to call twice (no double-subscribe).
    /// </summary>
    void Hook(Action<CCSPlayerController, bool> onESPChanged);

    /// <summary>
    /// Removes a handler added via Hook. Call in your plugin's Unload.
    /// </summary>
    void Unhook(Action<CCSPlayerController, bool> onESPChanged);
}
