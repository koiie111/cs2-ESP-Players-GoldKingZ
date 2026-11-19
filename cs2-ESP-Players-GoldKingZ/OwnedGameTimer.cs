using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using GameTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace ESP_Players_GoldKingZ;

// CSS keeps Timer delegates registered after native expiry or Kill. Own both lifetimes
// together so neither callbacks nor obsolete native timer handles survive a reload.
internal sealed class OwnedGameTimer
{
    private readonly GameTimer _timer;
    private readonly int _reference;
    private bool _active = true;

    public OwnedGameTimer(float seconds, Action elapsed, bool repeat = false)
    {
        void Tick()
        {
            if (!_active) return;
            if (!repeat) _active = false;
            try { elapsed(); }
            finally
            {
                if (!repeat) FunctionReference.Remove(_reference);
            }
        }

        Action callback = Tick;
        _reference = FunctionReference.Create(callback).Identifier;
        try { _timer = new GameTimer(seconds, callback, repeat ? TimerFlags.REPEAT : null); }
        catch
        {
            FunctionReference.Remove(_reference);
            throw;
        }
    }

    public void Cancel()
    {
        if (!_active) return;
        _active = false;
        _timer.Kill();
        FunctionReference.Remove(_reference);
    }
}
