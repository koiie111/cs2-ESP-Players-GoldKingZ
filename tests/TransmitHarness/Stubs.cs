using System.Drawing;
namespace CounterStrikeSharp.API.Core
{
    public class CEntityInstance { public (uint Raw, int Index) EntityHandle; }
    public class CDynamicProp : CEntityInstance
    {
        public bool IsValid = true;
        public uint Index => (uint)EntityHandle.Index;
        public int RemoveCalls;
        public void Remove() { RemoveCalls++; IsValid = false; }
    }
    public class CCSPlayerController : CEntityInstance
    {
        public bool IsValid = true, IsHLTV;
        public int Slot;
        public byte TeamNum;
        public CCSPlayerController? Controlled;
        public CCSPlayerController Get_CCSPlayerController_ControlledBot() => Controlled ?? this;
    }
    public struct CCheckTransmitInfo { public HashSet<int> TransmitEntities; }
    public class CCheckTransmitInfoList : List<(CCheckTransmitInfo, CCSPlayerController?)> { public CCheckTransmitInfoList() {} public CCheckTransmitInfoList(IEnumerable<(CCheckTransmitInfo, CCSPlayerController?)> items) : base(items) {} }
}
namespace ESP_Players_GoldKingZ.Config
{
    public class Configs
    {
        public static Configs Instance = new();
        public string Glow_Color_CT = "#00BEFF", Glow_Color_T = "#F3005D";
        public bool ShowOnlyEnemyTeam, DisableGlowOnGOTV;
    }
}
namespace ESP_Players_GoldKingZ
{
    using CounterStrikeSharp.API.Core;
    public class Prefs { public string Glow_Color_CT = "", Glow_Color_T = ""; }
    public class PlayerData
    {
        public CCSPlayerController Player = new();
        public CDynamicProp? ModelRelay;
        public Dictionary<int, CDynamicProp> ModelGlows = new();
    }
    public class Globals { public CounterStrikeSharp.API.Modules.Timers.Timer Timer = null!; public Dictionary<int, PlayerData> Player_Data = new(); }
    public partial class MainPlugin
    {
        public static MainPlugin Instance = null!;
        private readonly Dictionary<int, int> _pendingColorInputs = new();
        public class Enabled { public bool Value = true; }
        public Enabled g_EnablePlugin = new();
        public CounterStrikeSharp.API.Modules.Timers.Timer AddTimer(float interval, Action action, CounterStrikeSharp.API.Modules.Timers.TimerFlags flags) => new(action);
        public void QueueReconcile() => ScheduleEspReconcile();
        public int TrackedCount => _trackedEntities.Count;
        public int PendingInputs => _pendingColorInputs.Count;
        public void BeginInput(int slot) => _pendingColorInputs[slot] = 1;
        public Globals g_Main = new();
        public Dictionary<int, Prefs> _prefs = new();
        private Color ParseViewerGlowColor(int slot, byte team)
        {
            string hex = team == 3 ? Config.Configs.Instance.Glow_Color_CT : Config.Configs.Instance.Glow_Color_T;
            if (_prefs.TryGetValue(slot, out var prefs))
            {
                string personal = team == 3 ? prefs.Glow_Color_CT : prefs.Glow_Color_T;
                if (System.Text.RegularExpressions.Regex.IsMatch(personal, "^#[0-9a-fA-F]{6}$")) hex = personal;
            }
            return Color.FromArgb(unchecked((int)0xff000000) | Convert.ToInt32(hex[1..], 16));
        }
        public void Run(CCheckTransmitInfoList infos) => ApplyTransmitPlans(infos);
        public Color BaselineColor(int slot, byte team) => ParseViewerGlowColor(slot, team);
    }
}

namespace CounterStrikeSharp.API
{
    public static class Server
    {
        public static readonly Queue<Action> Frames = new();
        public static void NextFrame(Action action) => Frames.Enqueue(action);
        public static void Flush() { while (Frames.TryDequeue(out var action)) action(); }
    }
}
namespace CounterStrikeSharp.API.Modules.Timers
{
    public enum TimerFlags { REPEAT = 1, STOP_ON_MAPCHANGE = 2 }
    public class Timer(Action action) { public void Fire() => action(); }
}
namespace CounterStrikeSharp.API.Core
{
    public enum HookResult { Continue }
    public class GameEventInfo { }
    public class EventPlayerSpawn { public CCSPlayerController? Userid; }
    public class EventPlayerTeam { }
    public class EventBotTakeover { }
}
namespace ESP_Players_GoldKingZ
{
    using CounterStrikeSharp.API.Core;
    public static class Helper
    {
        public static int Reconciles, Spawns;
        public static void StartTimer() => MainPlugin.Instance.g_Main.Timer = MainPlugin.Instance.CreateEspTimer();
        public static void ESP_Timer() => Reconciles++;
        public static void SetGlowPlayer(CCSPlayerController player) => Spawns++;
        public static void CheckPlayerInGlobals(CCSPlayerController player)
        {
            MainPlugin.Instance.g_Main.Player_Data.TryAdd(player.Slot, new PlayerData { Player = player });
        }
        public static void RemoveGlowData(PlayerData data)
        {
            MainPlugin.Instance.RemoveEspEntity(data.ModelRelay);
            foreach (var prop in data.ModelGlows.Values) MainPlugin.Instance.RemoveEspEntity(prop);
            data.ModelGlows.Clear(); data.ModelRelay = null;
        }
    }
}
