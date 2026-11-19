using System.Drawing;
using System.Globalization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace CounterStrikeSharp.API.Core
{
    public class FunctionReference
    {
        private static readonly Dictionary<Delegate, FunctionReference> Callbacks = new();
        private static int _next;
        public static int Count => Callbacks.Count;
        public int Identifier { get; } = ++_next;
        public static FunctionReference Create(Delegate callback)
        {
            if (!Callbacks.TryGetValue(callback, out var reference)) Callbacks[callback] = reference = new();
            return reference;
        }
        public static void Remove(int id) => Callbacks.Remove(Callbacks.Single(pair => pair.Value.Identifier == id).Key);
    }
}

namespace CounterStrikeSharp.API.Core.Attributes.Registration
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ConsoleCommandAttribute(string name, string description) : Attribute
    {
        public string Name { get; } = name;
        public string Description { get; } = description;
    }
}

namespace CounterStrikeSharp.API.Modules.Commands
{
    public enum CommandUsage { CLIENT_ONLY }
    public class CommandHelperAttribute(CommandUsage whoCanExecute) : Attribute
    {
        public CommandUsage Usage { get; } = whoCanExecute;
    }
    public class CommandInfo(string text = "") { public string ArgString => text; }
}

namespace CounterStrikeSharp.API.Core.Translations
{
    public static class HudLanguage
    {
        public static CultureInfo GetLanguage(this CCSPlayerController player) => CultureInfo.InvariantCulture;
    }
}

namespace ESP_Players_GoldKingZ
{
    public partial class MainPlugin
    {
        public class StubLocalizer
        {
            public string ForPlayer(CCSPlayerController player, string key) => key;
        }
        public StubLocalizer Localizer { get; } = new();
    }

    public partial class Helper
    {
        public static void AdvancedPlayerPrintToChat(CCSPlayerController player, CommandInfo info, string text, params object[] args) { }
    }

    public static class StubColorExtensions
    {
        public static Color ToColor(this string configured)
        {
            int[] parts = configured.Split(',').Select(int.Parse).ToArray();
            return Color.FromArgb(parts[0], parts[1], parts[2]);
        }
    }
}

namespace PanoramaManager
{
    public enum PanelAction { Close, Restored, Button }
    public record PanelEvent(CCSPlayerController Player, PanelHandle Menu, PanelAction Action, string ElementId);
    public class LayoutContract
    {
        public string? RootPanelId, RevealClass;
        public int RowCount;
    }

    public static class Panorama
    {
        public static bool IsInitialised;
        public static bool CanReceiveClicks = true;
        public static PanelHandle? Live;
        public static void Init(ESP_Players_GoldKingZ.MainPlugin plugin) => IsInitialised = true;
        public static PanelHandle Spawn(string path, LayoutContract contract)
        {
            if (Live != null) throw new Exception("Allocated a second live handle");
            return Live = new();
        }
    }

    public class PanelHandle : IDisposable
    {
        private readonly Dictionary<int, CCSPlayerController> _open = new();
        public Dictionary<(int Slot, string Key), string> Text = new();
        public HashSet<(int Slot, string Panel, string Class)> Classes = new();
        public int Writes { get; private set; }
        public string? RefuseNextClassOn;
        public event Action<PanelEvent>? OnEvent;
        public bool IsOpenFor(CCSPlayerController player) => _open.ContainsKey(player.Slot);
        public void Open(CCSPlayerController player) => _open.Add(player.Slot, player);
        public void Close(int slot)
        {
            if (_open.Remove(slot, out var player)) OnEvent?.Invoke(new(player, this, PanelAction.Close, "menu_close"));
        }
        public void Click(CCSPlayerController player, string button) => OnEvent?.Invoke(new(player, this, PanelAction.Button, button));
        public void Restore(CCSPlayerController player)
        {
            foreach (var key in Text.Keys.Where(k => k.Slot == player.Slot).ToArray()) Text.Remove(key);
            Classes.RemoveWhere(c => c.Slot == player.Slot);
            OnEvent?.Invoke(new(player, this, PanelAction.Restored, ""));
        }
        public PanelHandle SetVariableFor(CCSPlayerController player, string key, string value)
        {
            if (!IsOpenFor(player)) throw new Exception("Text written to closed session");
            Writes++;
            Text[(player.Slot, key)] = value;
            return this;
        }
        public bool TrySetClassFor(CCSPlayerController player, string panel, string name, bool enabled)
        {
            if (!IsOpenFor(player)) throw new Exception("Class written to closed session");
            Writes++;
            if (RefuseNextClassOn == panel) { RefuseNextClassOn = null; return false; }
            var key = (player.Slot, panel, name);
            if (enabled) Classes.Add(key);
            else Classes.Remove(key);
            return true;
        }
        public void Dispose()
        {
            if (_open.Count != 0) throw new Exception("Disposed a handle still in use");
            Panorama.Live = null;
        }
    }
}
