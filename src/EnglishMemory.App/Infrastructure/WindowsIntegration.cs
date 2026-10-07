using System.Runtime.InteropServices;
using EnglishMemory.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
namespace EnglishMemory.App.Infrastructure;

public sealed class WindowsIntegration : Forms.NativeWindow, IDisposable
{
    private readonly Forms.NotifyIcon tray;
    private Drawing.Icon? icon;
    private readonly Action<int> hotkey;
    private readonly Action open;
    private readonly List<int> registrations = [];
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint hIcon);
    public WindowsIntegration(nint handle, Action open, Action listen, Action capture, Action privacy, Action quit, Action<int> hotkey)
    {
        AssignHandle(handle); this.hotkey = hotkey; this.open = open;
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Open", null, (_, _) => open()); menu.Items.Add("Pause / Resume Listening", null, (_, _) => listen()); menu.Items.Add("Capture Last Moment", null, (_, _) => capture()); menu.Items.Add("Private Mode", null, (_, _) => privacy()); menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Quit", null, (_, _) => quit());
        tray = new Forms.NotifyIcon { Text = "English Memory · Idle", Visible = true, ContextMenuStrip = menu }; tray.DoubleClick += (_, _) => open(); Update("Idle");
    }
    private readonly Dictionary<Forms.ToolStripItem, string> menuLabels = new();
    public void LocalizeMenu()
    {
        if (tray.ContextMenuStrip is null) return;
        foreach (Forms.ToolStripItem item in tray.ContextMenuStrip.Items)
        {
            if (!menuLabels.ContainsKey(item)) menuLabels[item] = item.Text ?? "";
            item.Text = EnglishMemory.App.UI.Localization.T(menuLabels[item]);
        }
    }
    public void Update(string state)
    {
        var color = state.Contains("Processing") ? Drawing.Color.SteelBlue : state == "Listening" ? Drawing.Color.SeaGreen : state == "Error" ? Drawing.Color.IndianRed : state == "Private" ? Drawing.Color.DarkOrange : state == "Paused" ? Drawing.Color.Olive : Drawing.Color.Gray;
        using var bitmap = new Drawing.Bitmap(32, 32); using (var g = Drawing.Graphics.FromImage(bitmap)) { g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Drawing.Color.Transparent); using var brush = new Drawing.SolidBrush(color); g.FillEllipse(brush, 2, 2, 28, 28); using var font = new Drawing.Font("Segoe UI", 15, Drawing.FontStyle.Bold); g.DrawString("E", font, Drawing.Brushes.White, 6, 2); }
        var h = bitmap.GetHicon(); using var borrowed = Drawing.Icon.FromHandle(h); var next = (Drawing.Icon)borrowed.Clone(); DestroyIcon(h); tray.Icon = next; icon?.Dispose(); icon = next; tray.Text = "English Memory · " + state;
    }
    public void SetHotkeys(UserSettings settings)
    {
        var parsed = new[] { Parse(settings.ListeningHotkey), Parse(settings.PrivateHotkey), Parse(settings.CaptureHotkey) };
        if (parsed.Distinct().Count() != 3) throw new ArgumentException("Each action needs a different shortcut.");
        foreach (int id in registrations) UnregisterHotKey(Handle, id); registrations.Clear();
        for (int i = 0; i < parsed.Length; i++) { if (!RegisterHotKey(Handle, 100 + i, parsed[i].Mods | 0x4000, parsed[i].Key)) { foreach (int id in registrations) UnregisterHotKey(Handle, id); registrations.Clear(); throw new InvalidOperationException($"Shortcut is already in use: {new[] { settings.ListeningHotkey, settings.PrivateHotkey, settings.CaptureHotkey }[i]}"); } registrations.Add(100 + i); }
    }
    private static (uint Mods, uint Key) Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); uint mods = 0; for (int i = 0; i < parts.Length - 1; i++) mods |= parts[i].ToLowerInvariant() switch { "ctrl" => 2u, "shift" => 4u, "alt" => 1u, _ => throw new ArgumentException("Use Ctrl, Shift or Alt modifiers.") };
        if (mods == 0 || parts.Length < 2) throw new ArgumentException("Use a shortcut such as Ctrl+Shift+L.");
        uint key = parts[^1].Equals("space", StringComparison.OrdinalIgnoreCase) ? 32u : parts[^1].Length == 1 ? (uint)char.ToUpperInvariant(parts[^1][0]) : throw new ArgumentException("Use a letter, number or Space as the shortcut key."); return (mods, key);
    }
    protected override void WndProc(ref Forms.Message m) { if (m.Msg == SingleInstance.OpenMessage) { open(); return; } if (m.Msg == 0x312) { hotkey((int)m.WParam); return; } if (m.Msg == 0x24) { var min = Marshal.PtrToStructure<MinMaxInfo>(m.LParam); min.MinTrack.X = 720; min.MinTrack.Y = 480; Marshal.StructureToPtr(min, m.LParam, false); return; } base.WndProc(ref m); }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    public void Dispose() { foreach (int id in registrations) UnregisterHotKey(Handle, id); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); icon?.Dispose(); ReleaseHandle(); }
    public static bool StartupEnabled { get { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return k?.GetValue("EnglishMemory") is string; } }
    public static void SetStartup(bool enabled) { using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); if (enabled) k.SetValue("EnglishMemory", "\"" + Environment.ProcessPath + "\""); else k.DeleteValue("EnglishMemory", false); }
}
