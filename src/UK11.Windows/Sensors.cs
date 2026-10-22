using LibreHardwareMonitor.Hardware;
using System.Management;

namespace UK11.Windows;

public sealed record SensorReading(double? GpuLoad, double? CpuTemperature, double? GpuTemperature, string GpuName, string CpuSensor, string GpuSensor, string Status);
public sealed class Sensors : IDisposable
{
    // Unsupported/virtual sensor interfaces can return zero instead of missing data.
    // Subzero cooling is outside this monitor's supported range; never display a sentinel as a reading.
    public static double? ValidTemperature(float? value) => value is > 0 and <= 150 ? value : null;
    private readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    private bool opened;
    private readonly object gate = new();
    public SensorReading Read() { lock (gate) return ReadCore(); }
    private SensorReading ReadCore()
    {
        try
        {
            if (!opened) { computer.Open(); opened = true; }
            foreach (var h in computer.Hardware) { h.Update(); foreach (var sub in h.SubHardware) sub.Update(); }
            var cpu = computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu).ToArray();
            var cpuTemperatures = cpu.SelectMany(h => h.Sensors).Where(s => s.SensorType == SensorType.Temperature && ValidTemperature(s.Value) is not null).ToArray();
            var cpuSensor = cpuTemperatures.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)) ?? cpuTemperatures.OrderByDescending(s => s.Value).FirstOrDefault();
            var gpus = computer.Hardware.Where(h => h.HardwareType.ToString().StartsWith("Gpu", StringComparison.Ordinal)).ToArray();
            var gpu = gpus.OrderByDescending(h => h.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))?.Value ?? -1).FirstOrDefault();
            var load = gpu?.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))?.Value;
            var gpuTemperatures = gpu?.Sensors.Where(s => s.SensorType == SensorType.Temperature && ValidTemperature(s.Value) is not null).ToArray() ?? [];
            var gpuSensor = gpuTemperatures.FirstOrDefault(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) ?? gpuTemperatures.FirstOrDefault();
            return new(load is >= 0 and <= 100 ? load : null, ValidTemperature(cpuSensor?.Value), ValidTemperature(gpuSensor?.Value), gpu?.Name ?? GpuIdentity(), cpuSensor?.Name ?? "No compatible CPU sensor", gpuSensor?.Name ?? "No compatible GPU sensor",
                cpuSensor?.Value is null ? "CPU temperature unavailable. Some devices need administrator access and a supported sensor driver. Windows security protections stay enabled." : "Read-only LibreHardwareMonitor sensor data");
        }
        catch (Exception ex) { return new(null, null, null, GpuIdentity(), "Unavailable", "Unavailable", "Sensor access unavailable: " + ex.Message); }
    }
    public static string GpuIdentity()
    {
        try
        {
            using var query = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            using var result = query.Get();
            return string.Join(", ", result.Cast<ManagementBaseObject>().Select(x => x["Name"]?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } name ? name : "No GPU reported";
        }
        catch { return "GPU information unavailable"; }
    }
    public void Dispose() { lock (gate) { if (opened) computer.Close(); } }
}
