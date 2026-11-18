using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace ESP_Players_GoldKingZ;

public partial class MainPlugin
{
    public void OnMapStart(string mapname)
    {
        StopEspRuntime();
        // STOP_ON_MAPCHANGE timers and old-map entities already belong to the previous world.
        g_Main.Timer = null!;
        g_Main.Player_Data.Clear();
        ClearTransmitCache();
        StartEspRuntime();
        Helper.StartTimer();
        ScheduleEspReconcile();
    }

    public HookResult OnEspSpawn(EventPlayerSpawn ev, GameEventInfo info)
    {
        if (!_runtimeActive) return HookResult.Continue;
        var player = ev.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;
        Helper.CheckPlayerInGlobals(player);
        int slot = player.Slot;
        var connection = g_Main.Player_Data[slot];
        var identity = player.EntityHandle.Raw;
        int generation = _runtimeGeneration;
        Server.NextFrame(() =>
        {
            if (generation != _runtimeGeneration || !_runtimeActive || !g_EnablePlugin.Value || !player.IsValid || player.EntityHandle.Raw != identity) return;
            if (!g_Main.Player_Data.TryGetValue(slot, out var current)
                || !ReferenceEquals(current, connection) || current.Player.EntityHandle.Raw != identity) return;
            Helper.SetGlowPlayer(player);
        });
        return HookResult.Continue;
    }

    public HookResult OnEspTeam(EventPlayerTeam ev, GameEventInfo info)
    {
        ScheduleEspReconcile();
        return HookResult.Continue;
    }

    public HookResult OnEspBotTakeover(EventBotTakeover ev, GameEventInfo info)
    {
        ScheduleEspReconcile();
        return HookResult.Continue;
    }

    private bool _runtimeActive;

    public void StartEspRuntime() => _runtimeActive = true;

    public void StopEspRuntime()
    {
        _runtimeActive = false;
        _runtimeGeneration++;
        _reconcilePending = false;
        _pendingColorInputs.Clear();
    }

    public CounterStrikeSharp.API.Modules.Timers.Timer CreateEspTimer()
    {
        int generation = _runtimeGeneration;
        return AddTimer(3.0f, () =>
        {
            if (_runtimeActive && generation == _runtimeGeneration && g_EnablePlugin.Value) Helper.ESP_Timer();
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private bool _reconcilePending;
    private void ScheduleEspReconcile()
    {
        if (!_runtimeActive || _reconcilePending) return;
        _reconcilePending = true;
        int generation = _runtimeGeneration;
        Server.NextFrame(() =>
        {
            if (generation != _runtimeGeneration) return;
            _reconcilePending = false;
            if (_runtimeActive && g_EnablePlugin.Value) Helper.ESP_Timer();
        });
    }

    public void OnEspClientDisconnect(int slot)
    {
        if (g_Main.Player_Data.Remove(slot, out var data)) Helper.RemoveGlowData(data);
        ForgetViewer(slot);
        _pendingColorInputs.Remove(slot);
        // The native controller may already be invalid. Reconcile surviving viewers next frame.
        ScheduleEspReconcile();
    }

}
