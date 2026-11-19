using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace ESP_Players_GoldKingZ;

public partial class MainPlugin
{
    public void OnMapStart(string mapname)
    {
        StopEspRuntime();
        // Discard wrappers belonging to the previous world.
        g_Main.Player_Data.Clear();
        ClearTransmitCache();
        StartEspRuntime();
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

    private OwnedGameTimer? _sweepTimer;

    public void StartEspRuntime()
    {
        _runtimeActive = true;
        _sweepTimer ??= new OwnedGameTimer(3.0f, () =>
        {
            if (_runtimeActive && g_EnablePlugin.Value) Helper.ESP_Timer();
        }, repeat: true);
    }

    public void StopEspRuntime()
    {
        _runtimeActive = false;
        _sweepTimer?.Cancel();
        _sweepTimer = null;
        _runtimeGeneration++;
        _reconcilePending = false;
        _pendingSpawns = null;
        CloseAllEspHuds();
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
        // The native controller may already be invalid. Reconcile surviving viewers next frame.
        ScheduleEspReconcile();
    }

}
