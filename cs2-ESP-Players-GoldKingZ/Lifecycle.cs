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
        if (!_reconcilePending || _pendingSpawns != null)
        {
            _pendingSpawns ??= new();
            _pendingSpawns[player.Slot] = g_Main.Player_Data[player.Slot];
            QueueReconcileFrame();
        }
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
        _pendingSpawns = null;
        CloseAllEspHuds();
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
    private Dictionary<int, Globals.PlayerDataClass>? _pendingSpawns;

    internal void ScheduleEspReconcile()
    {
        // A change in viewers or colors affects every target and supersedes spawn-only work.
        _pendingSpawns = null;
        QueueReconcileFrame();
    }

    private void QueueReconcileFrame()
    {
        if (!_runtimeActive || _reconcilePending) return;
        _reconcilePending = true;
        int generation = _runtimeGeneration;
        Server.NextFrame(() =>
        {
            if (generation != _runtimeGeneration) return;
            _reconcilePending = false;
            var spawns = _pendingSpawns;
            _pendingSpawns = null;
            if (_runtimeActive && g_EnablePlugin.Value) Helper.ESP_Timer(spawns);
        });
    }

    internal void ScheduleConfigReload()
    {
        int generation = _runtimeGeneration;
        Server.NextFrame(() =>
        {
            if (generation != _runtimeGeneration || !_runtimeActive || !g_EnablePlugin.Value) return;
            Helper.RemoveRegisterCommandsAndHooks();
            Helper.ClearVariables();
            Config.Configs.Load(ModuleDirectory, true);
            Helper.RegisterCommandsAndHooks();
            Helper.ReloadPlayersGlobals();
        });
    }

    public void OnEspClientDisconnect(int slot)
    {
        if (g_Main.Player_Data.Remove(slot, out var data)) Helper.RemoveGlowData(data);
        ForgetViewer(slot);
        ForgetEspHud(slot);
        _pendingColorInputs.Remove(slot);
        // The native controller may already be invalid. Reconcile surviving viewers next frame.
        ScheduleEspReconcile();
    }

}
