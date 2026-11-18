using System.Drawing;
using CounterStrikeSharp.API.Core;

namespace ESP_Players_GoldKingZ;

public class Globals
{
    public CounterStrikeSharp.API.Modules.Timers.Timer Timer = null!;

    public class PlayerDataClass
    {
        public CCSPlayerController Player { get; set; }
        public string Player_ModelName  { get; set; }
        public bool Gived_ESP  { get; set; }
        public uint PawnHandle { get; set; }
        public CDynamicProp ModelRelay { get; set; }
        public Dictionary<int, CDynamicProp> ModelGlows { get; } = new();
        public DateTime EventPlayerChat { get; set; }

        public PlayerDataClass(CCSPlayerController Playerr, string Player_ModelNamee, bool Gived_ESPP, CDynamicProp ModelRelayy, DateTime EventPlayerChatt)
        {
            Player = Playerr;
            Player_ModelName = Player_ModelNamee;
            Gived_ESP = Gived_ESPP;
            ModelRelay = ModelRelayy;
            EventPlayerChat = EventPlayerChatt;
        }
    }
    public Dictionary<int, PlayerDataClass> Player_Data = new Dictionary<int, PlayerDataClass>();

    public void Clear()
    {
        var timer = Timer;
        Timer = null!;
        try
        {
            timer?.Kill();
        }
        finally
        {
            Helper.RemoveAllPlayersGlow();
        }
    }
}
