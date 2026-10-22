using Microsoft.Win32;
using UK11.Core;
using UK11.Windows;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Run on Windows 10/11");
var path = @"Software\UK11Ultimate\IntegrationTests\" + Guid.NewGuid().ToString("N");
var dir = Path.Combine(Path.GetTempPath(), "uk11-native-" + Guid.NewGuid());
try
{
    using var key = Registry.CurrentUser.CreateSubKey(path);
    var store = new RegistrySettings();
    var engine = new Engine(store, dir);
    foreach (var original in Catalog.All)
    {
        var t = original with { Path = path, MinimumBuild = 10240, MaximumBuild = int.MaxValue };
        key.SetValue(t.Name, 7, RegistryValueKind.DWord);
        engine.Apply(t);
        if (store.Read(t) != new Value(true, t.Desired)) throw new Exception("Native apply failed: " + t.Id);
        engine.Restore(t);
        if (store.Read(t) != new Value(true, 7)) throw new Exception("Native restore failed");
        key.SetValue(t.Name, "7", RegistryValueKind.String);
        if (engine.Inspect(t).Supported) throw new Exception("Wrong registry type accepted");
        key.DeleteValue(t.Name);
        if (engine.Inspect(t).Supported) throw new Exception("Absent value accepted");
        Console.WriteLine("PASS isolated native registry: " + t.Id);
    }
    var h = Hardware.Inventory();
    var m = Hardware.Memory();
    if (h.Threads < 1 || m.Total == 0 || m.Available > m.Total) throw new Exception("Hardware inventory failed");
    Console.WriteLine("PASS hardware inventory and memory API");
}
finally
{
    Registry.CurrentUser.DeleteSubKeyTree(path, false);
    if (Directory.Exists(dir)) Directory.Delete(dir, true);
}
var active = PowerSettings.ActiveScheme();
var discovered = PowerSettings.Discover();
if (discovered.Count == 0) throw new Exception("No power settings discovered");
if (AdvancedBlueprints.All.Length != 60 || AdvancedBlueprints.All.Select(x => x.Id).Distinct().Count() != 60) throw new Exception("Advanced identities must be unique");
var advanced = AdvancedCatalog.Build(discovered);
if (advanced.Count == 0 || advanced.Count > 120 || advanced.Select(t => t.Id).Distinct().Count() != advanced.Count) throw new Exception("Invalid advanced catalogue");
foreach (var target in advanced.Where(t => t.Writable))
    if (!target.Accepts(target.Desired) || PowerSettings.PerformanceDefault(target) != target.Desired) throw new Exception("Advanced default is not an OS-reported valid target");
if (!advanced.Any(t => t.Writable)) throw new Exception("No Windows High Performance defaults could be validated");
Console.WriteLine($"PASS {advanced.Count} advanced targets, {advanced.Count(t => t.Writable)} valid Windows performance defaults");
var workflowChecks = await WorkflowChecks.Run();
var clone = PowerSettings.Duplicate(active);
var powerDirectory = Path.Combine(Path.GetTempPath(), "uk11-power-" + Guid.NewGuid());
var testedPower = 0; var readOnlyPower = 0; var noAlternativePower = 0;
var powerFailures = new List<string>();
var powerChecks = new List<object>();
try
{
    var store = new PowerSettings(); var engine = new Engine(store, powerDirectory);
    foreach (var setting in PowerSettings.Discover(clone))
    {
        if (!setting.Writable) { readOnlyPower++; powerChecks.Add(new { setting.Name, setting.Path, setting.Backend, setting.Title, result = "ReadOnly" }); continue; }
        var before = store.Read(setting);
        int? alternative = setting.Choices.FirstOrDefault(c => c.Number != before.Number)?.Number;
        if (setting.Choices.Length == 0)
        {
            var current = unchecked((uint)before.Number);
            if ((ulong)current >= (ulong)setting.Minimum + setting.Increment) alternative = unchecked((int)(current - setting.Increment));
            else if ((ulong)current + setting.Increment <= setting.Maximum) alternative = unchecked((int)(current + setting.Increment));
        }
        if (alternative is null) { noAlternativePower++; powerChecks.Add(new { setting.Name, setting.Path, setting.Backend, setting.Title, result = "NoAlternative" }); continue; }
        var target = setting with { Desired = alternative.Value };
        if (!target.Accepts(target.Desired)) throw new Exception("Invalid discovered range: " + target.Title);
        try
        {
            engine.Apply(target);
            if (store.Read(target).Number != target.Desired) throw new Exception("Power write did not verify: " + target.Title);
            // Recreate the descriptor and engine to exercise restart recovery with an independently selected target.
            new Engine(store, powerDirectory).Restore(setting);
            if (store.Read(setting) != before) throw new Exception("Power original did not restore: " + target.Title);
            testedPower++;
            powerChecks.Add(new { setting.Name, setting.Path, setting.Backend, setting.Title, result = "WriteVerifiedOriginalRestored", original = unchecked((uint)before.Number), target = unchecked((uint)target.Desired) });
        }
        catch (Exception ex)
        {
            powerFailures.Add(ex.Message);
            try { if (engine.GetBackup(target) is not null) engine.Restore(target); }
            catch (Exception recovery) { powerFailures.Add("Test-plan recovery: " + recovery.Message); }
        }
    }
    if (PowerSettings.ActiveScheme() != active) throw new Exception("Inactive-plan test unexpectedly switched plans");
    if (powerFailures.Count > 0) throw new Exception($"Power checks: {testedPower} passed, {powerFailures.Count} failed, {discovered.Count} discovered. " + string.Join(" | ", powerFailures.Take(20)));
    if (testedPower == 0) throw new Exception("No native power write/restore checks executed");
}
finally
{
    PowerSettings.DeleteInactive(clone);
    if (Directory.Exists(powerDirectory)) Directory.Delete(powerDirectory, true);
}
var report = new
{
    registryAdapterChecks = Catalog.All.Count,
    hardwareChecks = 1,
    advancedTargets = advanced.Count,
    advancedWritable = advanced.Count(t => t.Writable),
    workflowChecks,
    powerTargetsDiscovered = discovered.Count,
    powerSettingsDiscovered = discovered.Select(t => t.Path + "/" + t.Name).Distinct().Count(),
    powerWriteRestoreChecks = testedPower,
    readOnlyPower,
    noAlternativePower,
    powerChecks,
    testingMode = "Temporary HKCU registry keys and an inactive duplicate power plan; no real desktop preferences or active power settings changed."
};
if (args.Length > 0)
{
    var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"PASS {Catalog.All.Count} isolated registry settings, hardware inventory, {testedPower} inactive-plan power write/restore checks");
Console.WriteLine($"Discovered {discovered.Count} AC/DC power targets; read-only {readOnlyPower}; no alternative value {noAlternativePower}. No performance gains or real preference effects are proven by these tests.");
