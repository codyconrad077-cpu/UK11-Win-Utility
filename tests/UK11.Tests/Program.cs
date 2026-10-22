using UK11.Core;

var count = 0;
foreach (var tweak in Catalog.All)
{
    Run(tweak.Id + " applies and restores exactly", (e, s) =>
    {
        s.Current = new(true, 7);
        e.Apply(tweak);
        Check(s.Current == new Value(true, tweak.Desired), "desired value");
        Check(e.GetBackup(tweak)?.Original == new Value(true, 7), "original backup");
        e.Apply(tweak);
        Check(e.GetBackup(tweak)?.Original.Number == 7, "reapply must not replace original");
        e.Restore(tweak);
        Check(s.Current.Number == 7 && e.GetBackup(tweak)?.State == "Restored", "exact restore");
    });
    Run(tweak.Id + " rejects missing values", (e, s) =>
    {
        s.Current = new(false);
        Throws(() => e.Apply(tweak));
        Check(e.GetBackup(tweak) is null && s.Writes == 0, "no changes to unsupported settings");
    });
    Run(tweak.Id + " detects already applied without claiming ownership", (e, s) =>
    {
        s.Current = new(true, tweak.Desired);
        e.Apply(tweak);
        Check(e.GetBackup(tweak) is null && s.Writes == 0, "no fabricated original");
        Throws(() => e.Restore(tweak));
    });
}
var t = Catalog.All[0];
Run("unsupported platform is read-only", (e, s) =>
{
    s.Supported = false;
    Throws(() => e.Apply(t)); Throws(() => e.Restore(t));
    Check(s.Writes == 0 && !e.Inspect(t).Supported, "unsupported");
});
Run("write failure preserves durable original", (e, s) =>
{
    s.FailWrite = true;
    Throws(() => e.Apply(t));
    Check(e.GetBackup(t)?.State == "Prepared", "recoverable pending operation");
    s.FailWrite = false; e.Restore(t);
    Check(s.Current.Number == 1, "recover failed write");
});
Run("verification failure is reported and recoverable", (e, s) =>
{
    s.IgnoreWrite = true; Throws(() => e.Apply(t));
    Check(e.GetBackup(t)?.Original.Number == 1, "original remains");
    s.IgnoreWrite = false; e.Restore(t);
});
Run("external change blocks restore and preserves original", (e, s) =>
{
    e.Apply(t); s.Current = new(true, 9);
    Throws(() => e.Restore(t));
    Check(s.Current.Number == 9 && e.GetBackup(t)?.Original.Number == 1, "external value preserved");
    Throws(() => e.Apply(t));
});
Run("restored setting can start a new transaction", (e, s) =>
{
    e.Apply(t); e.Restore(t); s.Current = new(true, 3);
    e.Apply(t); e.Restore(t); Check(s.Current.Number == 3, "new original");
});
Run("restore verification failure retains backup", (e, s) =>
{
    e.Apply(t); s.IgnoreWrite = true; Throws(() => e.Restore(t));
    Check(e.GetBackup(t)?.State == "Applied", "backup retained");
    s.IgnoreWrite = false; e.Restore(t);
});
var restartDir = Path.Combine(Path.GetTempPath(), "uk11-restart-" + Guid.NewGuid());
try
{
    var store = new Fake(); new Engine(store, restartDir).Apply(t);
    new Engine(store, restartDir).Restore(t);
    Check(store.Current.Number == 1, "new engine restores durable backup");
    count++; Console.WriteLine("PASS restart recovery");
    var prefs = Path.Combine(restartDir, "prefs.json");
    PreferenceFile.Save(prefs, new("capture", "Optimisations"));
    Check(PreferenceFile.Load(prefs) == new Preferences("capture", "Optimisations"), "preferences round trip");
    count++; Console.WriteLine("PASS preference persistence");
    File.WriteAllText(Path.Combine(restartDir, t.Id + ".json"), "corrupt");
    Throws(() => new Engine(store, restartDir).Apply(t));
    Check(store.Current.Number == 1, "corrupt backup prevents mutation");
    count++; Console.WriteLine("PASS corrupt backup fails closed");
    var invalid = new Backup("wrong-id", new(true, 1), new(true, 0), DateTimeOffset.UtcNow, "Applied");
    File.WriteAllText(Path.Combine(restartDir, t.Id + ".json"), System.Text.Json.JsonSerializer.Serialize(invalid));
    Throws(() => new Engine(store, restartDir).Restore(t));
    Check(store.Current.Number == 1, "invalid backup cannot change a setting");
    count++; Console.WriteLine("PASS invalid backup metadata rejected");
}
finally { if (Directory.Exists(restartDir)) Directory.Delete(restartDir, true); }
Console.WriteLine($"{count} tests passed. These use a fake settings store, not the Windows registry.");

void Run(string name, Action<Engine, Fake> test)
{
    var dir = Path.Combine(Path.GetTempPath(), "uk11-test-" + Guid.NewGuid());
    var store = new Fake();
    try { test(new(store, dir), store); count++; Console.WriteLine("PASS " + name); }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
static void Check(bool result, string message) { if (!result) throw new Exception(message); }
static void Throws(Action action)
{
    try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or PlatformNotSupportedException or System.Text.Json.JsonException) { return; }
    throw new Exception("Expected operation to fail");
}
sealed class Fake : ISettingsStore
{
    public bool Supported { get; set; } = true;
    public Value Current = new(true, 1);
    public bool FailWrite, IgnoreWrite;
    public int Writes;
    public Value Read(Tweak t) => Current;
    public void Write(Tweak t, Value value)
    {
        Writes++;
        if (FailWrite) throw new IOException("Injected write failure");
        if (!IgnoreWrite) Current = value;
    }
}
