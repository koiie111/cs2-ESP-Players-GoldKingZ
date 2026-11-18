using System.Drawing;
namespace CounterStrikeSharp.API.Core
{
    public class CEntityInstance { public (uint Raw, int Index) EntityHandle; }
    public class CDynamicProp : CEntityInstance { public bool IsValid = true; public uint Index => (uint)EntityHandle.Index; }
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
    public class Globals { public Dictionary<int, PlayerData> Player_Data = new(); }
    public partial class MainPlugin
    {
        public bool _reconcilePending;
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

