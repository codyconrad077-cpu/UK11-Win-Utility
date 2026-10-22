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
Run("dynamic power bounds and increments reject invalid targets", (e, s) =>
{
    var power = t with { Id = "power-test", Backend = "PowerAC", Minimum = 10, Maximum = 100, Increment = 5, Desired = 12 };
    Throws(() => e.Apply(power)); Check(s.Writes == 0, "invalid power target must not write");
    power = power with { Desired = 20 }; e.Apply(power);
    Check(s.Current.Number == 20, "valid target writes");
    e.Restore(power with { Desired = 55 }); Check(s.Current.Number == 1, "original restores independently from current UI selection");
});
Run("enumerated power choices exclude arbitrary numbers", (e, s) =>
{
    var power = t with { Id = "power-choice", Backend = "PowerDC", Choices = [new(0, "Off"), new(2, "On")], Desired = 1 };
    Throws(() => e.Apply(power)); Check(s.Writes == 0, "unknown choice rejected");
    e.Apply(power with { Desired = 2 }); e.Restore(power);
    Check(s.Current.Number == 1, "choice original restored");
});
Run("read-only power settings cannot write", (e, s) =>
{
    Throws(() => e.Apply(t with { Writable = false })); Check(s.Writes == 0, "policy restriction respected");
});
var batchDir = Path.Combine(Path.GetTempPath(), "uk11-batch-" + Guid.NewGuid());
try
{
    var pair = Catalog.All.Take(2).ToArray();
    var map = new MapStore(pair); var batch = new Engine(map, batchDir);
    Check(batch.ApplyBatch(pair) == 2, "batch applies all targets");
    foreach (var item in pair) batch.Restore(item);
    Check(pair.All(x => map.Read(x).Number == 7), "batch originals restore");
    count++; Console.WriteLine("PASS successful multi-setting batch");
    map.FailAfterMutation = pair[1].Id;
    Throws(() => batch.ApplyBatch(pair));
    Check(pair.All(x => map.Read(x).Number == 7), "partial batch rolls back including failed mutation");
    count++; Console.WriteLine("PASS partial batch failure rolls back every attempted change");
    var writes = map.Writes;
    var preview = pair.ToDictionary(x => x.Id, _ => new Value(true, 9));
    Throws(() => batch.ApplyBatch(pair, expected: preview));
    Check(map.Writes == writes, "stale preview aborts before writes");
    count++; Console.WriteLine("PASS stale preview blocks mutation");
    Throws(() => batch.ApplyBatch([pair[0], pair[0]]));
    Check(map.Writes == writes, "duplicates rejected without mutation");
    count++; Console.WriteLine("PASS duplicate batch targets rejected");
    map.Values[pair[1].Id] = new(false);
    Throws(() => batch.ApplyBatch(pair)); Check(map.Writes == writes, "unavailable target blocks whole batch");
    count++; Console.WriteLine("PASS complete batch preflight before any write");
}
finally { if (Directory.Exists(batchDir)) Directory.Delete(batchDir, true); }
Check(Catalog.All.Select(x => x.Id).Distinct().Count() == Catalog.All.Count, "unique IDs");
Check(Catalog.All.Select(x => x.Path + "\\" + x.Name).Distinct().Count() == Catalog.All.Count, "unique registry targets");
count++; Console.WriteLine("PASS catalogue IDs and targets are unique");
Console.WriteLine($"{count} tests passed. These use fake settings stores, not the Windows registry or power APIs.");

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
sealed class MapStore(IEnumerable<Tweak> tweaks) : ISettingsStore
{
    public bool Supported => true;
    public readonly Dictionary<string, Value> Values = tweaks.ToDictionary(t => t.Id, _ => new Value(true, 7));
    public string? FailAfterMutation;
    public int Writes;
    public Value Read(Tweak t) => Values[t.Id];
    public void Write(Tweak t, Value value)
    {
        Writes++; Values[t.Id] = value;
        if (FailAfterMutation == t.Id) { FailAfterMutation = null; throw new IOException("Injected failure after mutation"); }
    }
}
