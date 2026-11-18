using System.Drawing;
using CounterStrikeSharp.API.Core;
using ESP_Players_GoldKingZ.Config;

namespace ESP_Players_GoldKingZ;

public partial class MainPlugin
{
    private sealed class ViewerColors
    {
        public string? CT;
        public string? T;
        public string DefaultCT = "";
        public string DefaultT = "";
        public Color CTColor;
        public Color TColor;
        public bool Initialized;
    }

    private sealed class TransmitPlan
    {
        public byte Team;
        public bool Hltv;
        public int CTColor;
        public int TColor;
        public readonly List<int> Add = new();
        public readonly List<int> Remove = new();
    }

    private readonly record struct TargetState(int Slot, byte Team, bool Valid, int Relay);
    private readonly record struct GlowState(int Target, int Color, int Index);
    private readonly List<TargetState> _targets = new();
    private readonly List<GlowState> _glows = new();
    private readonly List<TargetState> _nextTargets = new();
    private readonly List<GlowState> _nextGlows = new();
    private readonly Dictionary<int, ViewerColors> _viewerColors = new();
    private readonly Dictionary<int, TransmitPlan> _transmitPlans = new();
    private readonly HashSet<uint> _deletedEntities = new();
    private int _runtimeGeneration;
    private bool _enemyOnly;
    private bool _disableGotv;

    public Color GetViewerGlowColor(int slot, byte team)
    {
        if (!_viewerColors.TryGetValue(slot, out var cached))
            _viewerColors[slot] = cached = new ViewerColors();
        string? ct = null;
        string? t = null;
        if (_prefs != null && _prefs.TryGetValue(slot, out var prefs))
        {
            ct = prefs.Glow_Color_CT;
            t = prefs.Glow_Color_T;
        }
        var config = Configs.Instance;
        if (!cached.Initialized || cached.CT != ct || cached.T != t
            || cached.DefaultCT != config.Glow_Color_CT || cached.DefaultT != config.Glow_Color_T)
        {
            cached.CTColor = ParseViewerGlowColor(slot, 3);
            cached.TColor = ParseViewerGlowColor(slot, 2);
            cached.CT = ct;
            cached.T = t;
            cached.DefaultCT = config.Glow_Color_CT;
            cached.DefaultT = config.Glow_Color_T;
            cached.Initialized = true;
        }
        return team == 3 ? cached.CTColor : cached.TColor;
    }

    public void ForgetViewer(int slot)
    {
        _viewerColors.Remove(slot);
        _transmitPlans.Remove(slot);
    }

    public void ClearTransmitCache()
    {
        _runtimeGeneration++;
        _reconcilePending = false;
        _viewerColors.Clear();
        _transmitPlans.Clear();
        _targets.Clear();
        _glows.Clear();
        _deletedEntities.Clear();
        _trackedEntities.Clear();
    }

    public void OnEspEntityDeleted(CEntityInstance entity)
    {
        uint raw = entity.EntityHandle.Raw;
        if (!_trackedEntities.Remove(raw)) return;
        _deletedEntities.Add(raw);
        _transmitPlans.Clear();
    }

    private readonly HashSet<uint> _trackedEntities = new();

    public void TrackEspEntity(CDynamicProp prop) => _trackedEntities.Add(prop.EntityHandle.Raw);

    private int LiveIndex(CDynamicProp? prop)
        => prop != null && prop.IsValid && !_deletedEntities.Contains(prop.EntityHandle.Raw)
            ? (int)prop.Index : -1;

    private void CaptureTargets()
    {
        _nextTargets.Clear();
        _nextGlows.Clear();
        foreach (var entry in g_Main.Player_Data)
        {
            var data = entry.Value;
            var player = data.Player;
            bool valid = player != null && player.IsValid;
            byte team = valid ? player!.Get_CCSPlayerController_ControlledBot()?.TeamNum ?? (byte)0 : (byte)0;
            int target = _nextTargets.Count;
            _nextTargets.Add(new TargetState(entry.Key, team, valid, LiveIndex(data.ModelRelay)));
            foreach (var glow in data.ModelGlows)
            {
                int index = LiveIndex(glow.Value);
                if (index >= 0) _nextGlows.Add(new GlowState(target, glow.Key, index));
            }
        }
        if (!_targets.SequenceEqual(_nextTargets) || !_glows.SequenceEqual(_nextGlows)
            || _enemyOnly != Configs.Instance.ShowOnlyEnemyTeam || _disableGotv != Configs.Instance.DisableGlowOnGOTV)
        {
            _targets.Clear();
            _targets.AddRange(_nextTargets);
            _glows.Clear();
            _glows.AddRange(_nextGlows);
            _enemyOnly = Configs.Instance.ShowOnlyEnemyTeam;
            _disableGotv = Configs.Instance.DisableGlowOnGOTV;
            _transmitPlans.Clear();
        }
    }

    private bool HideTarget(TargetState target, int slot, byte team, bool hltv)
        => (hltv && _disableGotv) || target.Slot == slot || !target.Valid
            || (_enemyOnly && (team == 2 || team == 3) && target.Team == team);

    private void ApplyTransmitPlans(CCheckTransmitInfoList infos)
    {
        if (g_Main.Player_Data.Count == 0) return;
        // Sample native target state once per callback, including bot takeover and team changes.
        CaptureTargets();
        if (_glows.Count == 0 && !_targets.Any(t => t.Relay >= 0)) return;
        int count = infos.Count;
        for (int i = 0; i < count; i++)
        {
            var (info, viewer) = infos[i];
            if (viewer == null || !viewer.IsValid) continue;
            int slot = viewer.Slot;
            byte team = viewer.TeamNum;
            bool hltv = viewer.IsHLTV;
            int ct = GetViewerGlowColor(slot, 3).ToArgb();
            int t = GetViewerGlowColor(slot, 2).ToArgb();
            if (!_transmitPlans.TryGetValue(slot, out var plan) || plan.Team != team
                || plan.Hltv != hltv || plan.CTColor != ct || plan.TColor != t)
            {
                plan = new TransmitPlan { Team = team, Hltv = hltv, CTColor = ct, TColor = t };
                foreach (var target in _targets)
                {
                    if (target.Relay < 0) continue;
                    (HideTarget(target, slot, team, hltv) ? plan.Remove : plan.Add).Add(target.Relay);
                }
                foreach (var glow in _glows)
                {
                    var target = _targets[glow.Target];
                    bool hide = HideTarget(target, slot, team, hltv) || glow.Color != (target.Team == 3 ? ct : t);
                    (hide ? plan.Remove : plan.Add).Add(glow.Index);
                }
                _transmitPlans[slot] = plan;
            }
            foreach (int index in plan.Remove) info.TransmitEntities.Remove(index);
            foreach (int index in plan.Add) info.TransmitEntities.Add(index);
        }
    }
}
