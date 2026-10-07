using System.Runtime.InteropServices;
namespace EnglishMemory.Core;

public static class Hardware
{
    [StructLayout(LayoutKind.Sequential)] private sealed class MemoryStatus { public uint Length = 64, MemoryLoad; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice { public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString; public int Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice display, uint flags);
    public static string Describe()
    {
        var m = new MemoryStatus(); GlobalMemoryStatusEx(m); var gpus = new List<string>(); for (uint i = 0; i < 8; i++) { var d = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() }; if (!EnumDisplayDevices(null, i, ref d, 0)) break; if ((d.Flags & 1) != 0 && !gpus.Contains(d.DeviceString)) gpus.Add(d.DeviceString); }
        using var cpu = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return $"{cpu?.GetValue("ProcessorNameString") ?? "CPU"}\n{Environment.ProcessorCount} logical cores · {m.TotalPhysical / 1073741824d:F1} GB RAM\nGPU: {string.Join(", ", gpus)}\nInference backend: CPU. GPU VRAM and acceleration are not profiled in this build.\nSuggested setup: {(m.TotalPhysical < 12UL * 1024 * 1024 * 1024 ? "Fast" : "Balanced")} (memory-based suggestion; compare latency on your PC).";
    }
}
