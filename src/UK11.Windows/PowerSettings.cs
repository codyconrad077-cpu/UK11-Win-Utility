using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using UK11.Core;

namespace UK11.Windows;

/// <summary>Documented PowrProf API; does not manufacture unsupported settings or recommended values.</summary>
public sealed class PowerSettings : ISettingsStore
{
    public bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10);
    public bool Supports(Tweak t) => Supported && t.Adjustable && Guid.TryParse(t.Scheme, out _) && Guid.TryParse(t.Path, out _) && Guid.TryParse(t.Name, out _);
    private static void Check(uint error) { if (error != 0) throw new Win32Exception((int)error); }
    public static Guid ActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }
    public static string ActiveName() => Friendly(ActiveScheme(), null, null);
    public Value Read(Tweak t)
    {
        if (!Supports(t)) return new(false);
        uint number;
        var error = t.Backend == "PowerAC"
            ? PowerReadACValueIndex(IntPtr.Zero, [Guid.Parse(t.Scheme)], [Guid.Parse(t.Path)], [Guid.Parse(t.Name)], out number)
            : PowerReadDCValueIndex(IntPtr.Zero, [Guid.Parse(t.Scheme)], [Guid.Parse(t.Path)], [Guid.Parse(t.Name)], out number);
        if (error is 2 or 1168) return new(false);
        Check(error);
        return new(true, unchecked((int)number));
    }
    public void Write(Tweak t, Value value)
    {
        if (!Supports(t) || !value.Exists) throw new InvalidOperationException("Power target is unavailable.");
        var scheme = Guid.Parse(t.Scheme); var group = Guid.Parse(t.Path); var setting = Guid.Parse(t.Name);
        Check(PowerSettingAccessCheck(t.Backend == "PowerAC" ? 0u : 1u, [setting]));
        var error = t.Backend == "PowerAC"
            ? PowerWriteACValueIndex(IntPtr.Zero, [scheme], [group], [setting], unchecked((uint)value.Number))
            : PowerWriteDCValueIndex(IntPtr.Zero, [scheme], [group], [setting], unchecked((uint)value.Number));
        if (error != 0) throw new Win32Exception((int)error, $"Windows rejected {t.Title}: value {unchecked((uint)value.Number)} ({setting}), error {error}.");
        // Never deliberately switch to an inactive captured plan. Reapply only the currently active plan.
        if (ActiveScheme() == scheme) Check(PowerSetActiveScheme(IntPtr.Zero, [scheme]));
    }
    public static Guid Duplicate(Guid source)
    {
        Check(PowerDuplicateScheme(IntPtr.Zero, [source], out var pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }
    public static void DeleteInactive(Guid scheme)
    {
        if (ActiveScheme() == scheme) throw new InvalidOperationException("Refusing to delete the active scheme.");
        Check(PowerDeleteScheme(IntPtr.Zero, [scheme]));
    }
    public static IReadOnlyList<Tweak> Discover(Guid? selectedScheme = null)
    {
        var scheme = selectedScheme ?? ActiveScheme();
        var result = new List<Tweak>();
        var store = new PowerSettings();
        foreach (var group in Enumerate(scheme, null, 17))
        {
            var groupName = Friendly(scheme, group, null);
            foreach (var setting in Enumerate(scheme, group, 18))
            {
                var title = Friendly(scheme, group, setting);
                var description = Description(scheme, group, setting);
                var ranged = PowerReadValueMin(IntPtr.Zero, [group], [setting], out var min) == 0
                    && PowerReadValueMax(IntPtr.Zero, [group], [setting], out _) == 0;
                PowerReadValueMax(IntPtr.Zero, [group], [setting], out var max);
                PowerReadValueIncrement(IntPtr.Zero, [group], [setting], out var step);
                if (step == 0) step = 1;
                // Finite choices take precedence over ranges (enumerations can have gaps).
                // Continuous settings can expose unbounded possible-value sequences; use their range instead.
                Choice[] options = [];
                var complete = true;
                try { options = Options(group, setting); }
                catch (InvalidDataException ex) { complete = ranged; if (!ranged) description += " Editing unavailable: " + ex.Message; }
                var units = Units(group, setting);
                foreach (var ac in new[] { true, false })
                {
                    var t = new Tweak($"power.{scheme:N}.{group:N}.{setting:N}.{(ac ? "ac" : "dc")}", title + (ac ? " · Plugged in" : " · On battery"), "Power configuration",
                        group.ToString(), setting.ToString(), 0,
                        (string.IsNullOrWhiteSpace(description) ? "Windows exposes this power parameter without a description." : description) + " Windows-valid values are not performance recommendations. Battery and sleep settings can affect data loss, temperature and runtime.",
                        "Stored value is verified; active plans are reapplied. Inactive plans take effect when selected in Windows.")
                    {
                        Backend = ac ? "PowerAC" : "PowerDC", Scheme = scheme.ToString(), Group = groupName,
                        Choices = options, Minimum = min, Maximum = max, Increment = step, Units = units,
                        Writable = complete && (options.Length > 0 || ranged) && PowerSettingAccessCheck(ac ? 0u : 1u, [setting]) == 0
                    };
                    var current = store.Read(t);
                    if (current.Exists)
                    {
                        var validOriginal = t.Accepts(current.Number);
                        result.Add(t with
                        {
                            Desired = current.Number, Writable = t.Writable && validOriginal,
                            Effect = t.Effect + (validOriginal ? "" : " Editing disabled because the original is outside the reported choices/range; exact API restoration cannot be assumed.")
                        });
                    }
                }
            }
        }
        return result;
    }
    private static IEnumerable<Guid> Enumerate(Guid scheme, Guid? group, uint accessor)
    {
        for (uint index = 0; index < 4096; index++)
        {
            var bytes = new byte[16]; uint length = 16;
            var error = PowerEnumerate(IntPtr.Zero, [scheme], group is null ? null : [group.Value], accessor, index, bytes, ref length);
            if (error == 259) yield break;
            Check(error);
            if (length != 16) throw new InvalidDataException("Invalid power GUID size.");
            yield return new Guid(bytes);
        }
        throw new InvalidDataException("Power enumeration exceeded its safety limit; no complete catalogue claimed.");
    }
    private static string Friendly(Guid scheme, Guid? group, Guid? setting)
    {
        var buffer = new byte[65536]; uint size = (uint)buffer.Length;
        var code = PowerReadFriendlyName(IntPtr.Zero, [scheme], group is null ? null : [group.Value], setting is null ? null : [setting.Value], buffer, ref size);
        return code == 0 ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0') : (setting ?? group ?? scheme).ToString();
    }
    private static string Description(Guid scheme, Guid group, Guid setting)
    {
        var buffer = new byte[65536]; uint size = (uint)buffer.Length;
        return PowerReadDescription(IntPtr.Zero, [scheme], [group], [setting], buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0') : "";
    }
    private static string Units(Guid group, Guid setting)
    {
        var buffer = new byte[4096]; uint size = (uint)buffer.Length;
        return PowerReadValueUnitsSpecifier(IntPtr.Zero, [group], [setting], buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0') : "";
    }
    private static Choice[] Options(Guid group, Guid setting)
    {
        var options = new List<Choice>();
        for (uint index = 0; index < 256; index++)
        {
            var value = new byte[4]; uint size = 4;
            var error = PowerReadPossibleValue(IntPtr.Zero, [group], [setting], out var type, index, value, ref size);
            if (error is 259 or 2) return options.ToArray();
            if (error != 0 || type != 4 || size != 4) return options.ToArray();
            var name = new byte[4096]; uint nameSize = (uint)name.Length;
            var label = PowerReadPossibleFriendlyName(IntPtr.Zero, [group], [setting], index, name, ref nameSize) == 0
                ? Encoding.Unicode.GetString(name, 0, (int)nameSize).TrimEnd('\0') : BitConverter.ToUInt32(value).ToString();
            options.Add(new(BitConverter.ToInt32(value), label));
        }
        throw new InvalidDataException("Power option count exceeds the safety limit.");
    }
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, [In] Guid[] scheme, [In] Guid[]? group, uint accessor, uint index, byte[] buffer, ref uint length);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, [In] Guid[] scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerDuplicateScheme(IntPtr root, [In] Guid[] source, out IntPtr destination);
    [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(IntPtr root, [In] Guid[] scheme);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, [In] Guid[]? scheme, [In] Guid[]? group, [In] Guid[]? setting, byte[] buffer, ref uint length);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDescription(IntPtr root, [In] Guid[]? scheme, [In] Guid[]? group, [In] Guid[]? setting, byte[] buffer, ref uint length);
    [DllImport("powrprof.dll")] private static extern uint PowerReadValueUnitsSpecifier(IntPtr root, [In] Guid[] group, [In] Guid[] setting, byte[] buffer, ref uint length);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, [In] Guid[] scheme, [In] Guid[] group, [In] Guid[] setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, [In] Guid[] scheme, [In] Guid[] group, [In] Guid[] setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, [In] Guid[] scheme, [In] Guid[] group, [In] Guid[] setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, [In] Guid[] scheme, [In] Guid[] group, [In] Guid[] setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerSettingAccessCheck(uint accessor, [In] Guid[] setting);
    [DllImport("powrprof.dll")] private static extern uint PowerReadValueMin(IntPtr root, [In] Guid[] group, [In] Guid[] setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadValueMax(IntPtr root, [In] Guid[] group, [In] Guid[] setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadValueIncrement(IntPtr root, [In] Guid[] group, [In] Guid[] setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadPossibleValue(IntPtr root, [In] Guid[] group, [In] Guid[] setting, out uint type, uint index, byte[] buffer, ref uint length);
    [DllImport("powrprof.dll")] private static extern uint PowerReadPossibleFriendlyName(IntPtr root, [In] Guid[] group, [In] Guid[] setting, uint index, byte[] buffer, ref uint length);
}

public sealed class WindowsSettings : ISettingsStore
{
    private readonly RegistrySettings registry = new();
    private readonly PowerSettings power = new();
    public bool Supported => registry.Supported;
    private ISettingsStore Store(Tweak t) => t.Adjustable ? power : registry;
    public bool Supports(Tweak t) => Store(t).Supports(t);
    public Value Read(Tweak t) => Store(t).Read(t);
    public void Write(Tweak t, Value value) => Store(t).Write(t, value);
}
