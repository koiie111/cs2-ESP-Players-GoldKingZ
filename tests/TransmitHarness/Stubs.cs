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
        public bool IsValid = true, IsHLTV, IsBot;
        public int Slot;
        public byte TeamNum;
        public CCSPlayerController? Controlled;
        public CCSPlayerController Get_CCSPlayerController_ControlledBot() => Controlled ?? this;
    }
    // Managed equivalent of the pinned SDK's 16384-edict CFixedBitVecBase.
    public sealed class TransmitBits
    {
        private readonly uint[] _words = new uint[16384 / 32];
        public TransmitBits(bool full = false)
        {
            if (full) Array.Fill(_words, uint.MaxValue);
        }
        public void Add(int index) => _words[index >> 5] |= 1u << (index & 31);
        public void Remove(int index) => _words[index >> 5] &= ~(1u << (index & 31));
        public bool Contains(int index) => (_words[index >> 5] & (1u << (index & 31))) != 0;
        public void Clear() => Array.Clear(_words);
        public bool IsEmpty => !_words.AsSpan().ContainsAnyExcept(0u);
        public bool SetEquals(TransmitBits other) => _words.AsSpan().SequenceEqual(other._words);
    }
    public struct CCheckTransmitInfo { public TransmitBits TransmitEntities; }
    public class CCheckTransmitInfoList : List<(CCheckTransmitInfo, CCSPlayerController?)> { public CCheckTransmitInfoList() {} public CCheckTransmitInfoList(IEnumerable<(CCheckTransmitInfo, CCSPlayerController?)> items) : base(items) {} }
}
namespace ESP_Players_GoldKingZ.Config
{
    public class Configs
    {
        public static Configs Instance = new();
        public static void Load(string directory, bool reload) { }
        public string Glow_Color_CT = "0, 190, 255, 255", Glow_Color_T = "243, 0, 93, 255";
        public bool PanoramaMenuEnabled;
        public bool ShowOnlyEnemyTeam, DisableGlowOnGOTV;
    }
}
namespace ESP_Players_GoldKingZ
{
    using CounterStrikeSharp.API.Core;
    public class Prefs { public string Glow_Color_CT = "", Glow_Color_T = ""; }
    public class Globals
    {
        public class PlayerDataClass
        {
            public CCSPlayerController Player = new();
            public CDynamicProp? ModelRelay;
            public Dictionary<int, CDynamicProp> ModelGlows = new();
        }
        public CounterStrikeSharp.API.Modules.Timers.Timer Timer = null!;
        public Dictionary<int, PlayerDataClass> Player_Data = new();
    }
    public partial class MainPlugin
    {
        public static MainPlugin Instance = null!;
        public string ModuleDirectory = "";
        public class Enabled { public bool Value = true; }
        public Enabled g_EnablePlugin = new();
        public CounterStrikeSharp.API.Modules.Timers.Timer AddTimer(float interval, Action action, CounterStrikeSharp.API.Modules.Timers.TimerFlags flags) => new(action);
        public void QueueReconcile() => ScheduleEspReconcile();
        public int TrackedCount => _trackedEntities.Count;
        public int PendingInputs => _pendingColorInputs.Count;
        public void BeginInput(int slot) => BeginHexInput(new CCSPlayerController { Slot = slot }, GlowTeamChoice.Terrorist);
        public Globals g_Main = new();
        public Dictionary<int, Prefs> _prefs = new();
        public Color BaselineColor(int slot, byte team)
        {
            string hex = team == 3 ? Config.Configs.Instance.Glow_Color_CT : Config.Configs.Instance.Glow_Color_T;
            if (_prefs.TryGetValue(slot, out var prefs))
            {
                string personal = team == 3 ? prefs.Glow_Color_CT : prefs.Glow_Color_T;
                if (System.Text.RegularExpressions.Regex.IsMatch(personal, "^#[0-9a-fA-F]{6}$"))
                    return Color.FromArgb(unchecked((int)0xff000000) | Convert.ToInt32(personal[1..], 16));
            }
            return Color.FromArgb(hex.ToColor().ToArgb() | unchecked((int)0xff000000));
        }
        public void Run(CCheckTransmitInfoList infos) => ApplyTransmitPlans(infos);
    }
}

namespace CounterStrikeSharp.API
{
    public static class Utilities
    {
        public static List<Core.CCSPlayerController> Connected = new();
        public static int Calls;
        public static List<Core.CCSPlayerController> GetPlayers() { Calls++; return Connected; }
        public static Core.CCSPlayerController? GetPlayerFromSlot(int slot) => Connected.Find(p => p.Slot == slot);
    }
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
    public class Timer(Action action)
    {
        public static readonly List<Timer> OneShots = new();
        public readonly float Interval;
        public Timer(float interval, Action action) : this(action)
        {
            Interval = interval;
            Core.FunctionReference.Create(action);
            OneShots.Add(this);
        }
        public void Fire() { OneShots.Remove(this); action(); }
        public void Kill() { if (!OneShots.Remove(this)) throw new Exception("Killed an inactive one-shot"); }
    }
}
namespace CounterStrikeSharp.API.Core
{
    public enum HookResult { Continue, Handled }
    public class GameEventInfo { }
    public class EventPlayerSpawn { public CCSPlayerController? Userid; }
    public class EventPlayerTeam { }
    public class EventBotTakeover { }
}
namespace ESP_Players_GoldKingZ
{
    using CounterStrikeSharp.API.Core;
    public partial class Helper
    {
        public static int Reconciles => CounterStrikeSharp.API.Utilities.Calls;
        public static int Spawns, Reloads;
        public static void RemoveRegisterCommandsAndHooks() { }
        public static void ClearVariables() { }
        public static void RegisterCommandsAndHooks() { }
        public static void ReloadPlayersGlobals() => Reloads++;
        public static void StartTimer() => MainPlugin.Instance.g_Main.Timer = MainPlugin.Instance.CreateEspTimer();
        private static void SetGlowPlayer(CCSPlayerController player, GlowDemand colors) => Spawns++;
        public static void CheckPlayerInGlobals(CCSPlayerController player)
        {
            MainPlugin.Instance.g_Main.Player_Data.TryAdd(player.Slot, new Globals.PlayerDataClass { Player = player });
        }
        public static void RemoveGlowData(Globals.PlayerDataClass data)
        {
            MainPlugin.Instance.RemoveEspEntity(data.ModelRelay);
            foreach (var prop in data.ModelGlows.Values) MainPlugin.Instance.RemoveEspEntity(prop);
            data.ModelGlows.Clear(); data.ModelRelay = null;
        }
    }
}
