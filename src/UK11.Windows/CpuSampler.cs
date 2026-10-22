using System.Runtime.InteropServices;

namespace UK11.Windows;

public sealed class CpuSampler
{
    private ulong lastIdle, lastTotal;
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    public double? Sample()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var total = unchecked((ulong)kernel + (ulong)user);
        var prior = lastTotal; var priorIdle = lastIdle;
        lastTotal = total; lastIdle = (ulong)idle;
        if (prior == 0 || total <= prior) return null;
        return Math.Clamp(100d * (1 - ((ulong)idle - priorIdle) / (double)(total - prior)), 0, 100);
    }
}
