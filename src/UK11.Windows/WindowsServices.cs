using Microsoft.Win32;
using System.Runtime.InteropServices;
using UK11.Core;

namespace UK11.Windows;

public sealed class RegistrySettings : ISettingsStore
{
    public bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240);
    public bool Supports(Tweak t) => Supported && Environment.OSVersion.Version.Build >= t.MinimumBuild && Environment.OSVersion.Version.Build <= t.MaximumBuild;
    public Value Read(Tweak t)
    {
        using var key = Registry.CurrentUser.OpenSubKey(t.Path);
        if (key is null || !key.GetValueNames().Contains(t.Name) || key.GetValueKind(t.Name) != RegistryValueKind.DWord)
            return new(false);
        return new(true, (int)key.GetValue(t.Name)!);
    }
    public void Write(Tweak t, Value value)
    {
        if (!Supports(t)) throw new PlatformNotSupportedException();
        using var key = Registry.CurrentUser.OpenSubKey(t.Path, true) ?? throw new IOException("Registry key unavailable");
        if (value.Exists) key.SetValue(t.Name, value.Number, RegistryValueKind.DWord);
        else key.DeleteValue(t.Name, false);
        key.Flush();
    }
}

public sealed record MachineInfo(string Cpu, string Board, string Firmware, string Os, int Threads);
public sealed record MemorySample(ulong Total, ulong Available);
public static class Hardware
{
    private static string Read(string path, string name)
    {
        try { using var key = Registry.LocalMachine.OpenSubKey(path); var value = key?.GetValue(name); return value is string[] parts ? string.Join(" · ", parts) : value?.ToString()?.Trim() ?? "Unavailable"; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { return "Unavailable: " + ex.Message; }
    }
    public static MachineInfo Inventory() => new(
        Read(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString"),
        Read(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer") + " " + Read(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct"),
        Read(@"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVersion"),
        RuntimeInformation.OSDescription, Environment.ProcessorCount);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    public static MemorySample Memory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new(status.TotalPhysical, status.AvailablePhysical);
    }
}
