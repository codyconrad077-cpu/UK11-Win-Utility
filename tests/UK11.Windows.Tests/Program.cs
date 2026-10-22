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
        var t = original with { Path = path };
        key.SetValue(t.Name, 7, RegistryValueKind.DWord);
        engine.Apply(t);
        if (store.Read(t) != new Value(true, 0)) throw new Exception("Native apply failed");
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
Console.WriteLine("4 native integration checks passed. Actual desktop setting effects still require manual acceptance.");
