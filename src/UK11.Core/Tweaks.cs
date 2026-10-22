using System.Text.Json;

namespace UK11.Core;

public sealed record Tweak(string Id, string Title, string Category, string Path, string Name, int Desired, string Effect, string Restart)
{
    public string Backend { get; init; } = "Registry";
    public string Scheme { get; init; } = "";
    public string Group { get; init; } = "";
    public bool Adjustable => Backend is "PowerAC" or "PowerDC";
    public Choice[] Choices { get; init; } = [];
    public uint Minimum { get; init; }
    public uint Maximum { get; init; }
    public uint Increment { get; init; } = 1;
    public string Units { get; init; } = "";
    public bool Writable { get; init; } = true;
    public int MinimumBuild { get; init; } = 10240;
    public int MaximumBuild { get; init; } = int.MaxValue;
    public string Risk => Adjustable ? "Advanced • affects energy, thermals or sleep behavior" : "Low • current-user preference";
    public string Evidence => Adjustable ? "Documented Windows power API; benefit depends on workload and hardware." : "Existing Windows preference; stored-value verification, not benchmark proof.";
    public string Source => Adjustable ? "https://learn.microsoft.com/windows/win32/power/power-management-functions" : "https://support.microsoft.com/windows";
    public bool Accepts(int number) => !Adjustable ? number == Desired : Choices.Length > 0
        ? Choices.Any(c => c.Number == number)
        : unchecked((uint)number) >= Minimum && unchecked((uint)number) <= Maximum && Increment > 0 && (unchecked((uint)number) - Minimum) % Increment == 0;
    public string Format(int number) => Choices.FirstOrDefault(c => c.Number == number)?.Label ?? unchecked((uint)number).ToString() + (string.IsNullOrWhiteSpace(Units) ? "" : " " + Units);
    public string Permissions => Adjustable ? "Windows power policy permissions; may be restricted" : "Standard user";
    public string Support => Adjustable ? "Discovered from this computer; only OS-reported options/ranges are accepted" : "Windows 10/11 desktop; requires an existing DWORD and compatible build";
}

public sealed record Choice(int Number, string Label);

public sealed record Value(bool Exists, int Number = 0);
public interface ISettingsStore
{
    bool Supported { get; }
    bool Supports(Tweak tweak) => Supported;
    Value Read(Tweak tweak);
    void Write(Tweak tweak, Value value);
}

public sealed record Backup(string TweakId, Value Original, Value Applied, DateTimeOffset Created, string State);
public sealed record Inspection(bool Supported, bool Applied, Value Current, string Detail);

public sealed class Engine(ISettingsStore store, string directory)
{
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private string FileFor(Tweak t) => Path.Combine(directory, t.Id + ".json");
    public Inspection Inspect(Tweak t)
    {
        if (!store.Supports(t)) return new(false, false, new(false), "Unsupported operating system");
        var v = store.Read(t);
        return new(v.Exists, v.Exists && v.Number == t.Desired, v,
            !v.Exists ? "Unavailable: setting absent or not DWORD" : v.Number == t.Desired ? "Target value detected" : "Available");
    }
    public Backup? GetBackup(Tweak t)
    {
        if (!File.Exists(FileFor(t))) return null;
        var b = JsonSerializer.Deserialize<Backup>(File.ReadAllText(FileFor(t))) ?? throw new InvalidDataException("Empty backup");
        if (b.TweakId != t.Id || b.Original is null || !b.Original.Exists || b.Applied is null || !b.Applied.Exists || !t.Accepts(b.Applied.Number)
            || b.State is not ("Prepared" or "Applied" or "Restored"))
            throw new InvalidDataException("Backup metadata is invalid. No changes made.");
        return b;
    }
    private void Save(Tweak t, Backup backup)
    {
        Directory.CreateDirectory(directory);
        var temp = FileFor(t) + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, backup, Json);
            stream.Flush(true);
        }
        File.Move(temp, FileFor(t), true);
    }
    private void Log(Tweak t, string action) => File.AppendAllText(Path.Combine(directory, "transactions.log"),
        $"{DateTimeOffset.UtcNow:O} {t.Id} {action}{Environment.NewLine}");
    public void Apply(Tweak t)
    {
        lock (gate)
        {
            var current = Inspect(t);
            if (!current.Supported) throw new InvalidOperationException(current.Detail);
            if (!t.Writable || !t.Accepts(t.Desired)) throw new InvalidOperationException("This target is read-only or outside the OS-reported allowed values.");
            if (current.Applied) return;
            var prior = GetBackup(t);
            if (prior is not null && prior.State != "Restored")
                throw new InvalidOperationException("An unresolved backup exists. Restore it before applying again.");
            var backup = new Backup(t.Id, current.Current, new(true, t.Desired), DateTimeOffset.UtcNow, "Prepared");
            Save(t, backup); // Durable original before any mutation; retained even on write/verification failure.
            Log(t, "Apply prepared");
            try
            {
                store.Write(t, backup.Applied);
                if (store.Read(t) != backup.Applied) throw new IOException("Apply verification failed. Original retained for restore.");
                Save(t, backup with { State = "Applied" });
                Log(t, "Apply verified");
            }
            catch (Exception ex) { Log(t, "Apply failed: " + ex.Message); throw; }
        }
    }
    public void Restore(Tweak t)
    {
        lock (gate)
        {
            if (!store.Supported) throw new PlatformNotSupportedException();
            var b = GetBackup(t) ?? throw new InvalidOperationException("No original backup exists.");
            if (b.TweakId != t.Id) throw new InvalidDataException("Backup identity mismatch");
            var current = store.Read(t);
            if (current != b.Applied && current != b.Original)
                throw new InvalidOperationException("Setting changed outside UK11. Restore refused to avoid overwriting your change.");
            if (b.State == "Restored" && current == b.Original) return;
            Log(t, "Restore prepared");
            try
            {
                if (current != b.Original) store.Write(t, b.Original);
                if (store.Read(t) != b.Original) throw new IOException("Restore verification failed. Backup retained.");
                Save(t, b with { State = "Restored" });
                Log(t, "Restore verified");
            }
            catch (Exception ex) { Log(t, "Restore failed: " + ex.Message); throw; }
        }
    }
    public int ApplyBatch(IReadOnlyList<Tweak> tweaks, Action<int, int, string>? progress = null, IReadOnlyDictionary<string, Value>? expected = null)
    {
        lock (gate)
        {
            if (tweaks.Select(t => t.Id).Distinct().Count() != tweaks.Count) throw new InvalidOperationException("Duplicate setting in batch.");
            var snapshots = tweaks.Select(t => (Tweak: t, State: Inspect(t), Backup: GetBackup(t))).ToArray();
            foreach (var item in snapshots)
            {
                if (expected is not null && (!expected.TryGetValue(item.Tweak.Id, out var previewed) || previewed != item.State.Current))
                    throw new InvalidOperationException("Configuration changed since the preview. Review again: " + item.Tweak.Title);
                if (!item.State.Supported || !item.Tweak.Writable || !item.Tweak.Accepts(item.Tweak.Desired))
                    throw new InvalidOperationException("Batch preflight failed: " + item.Tweak.Title);
                if (!item.State.Applied && item.Backup is { State: not "Restored" })
                    throw new InvalidOperationException("Unresolved original must be restored first: " + item.Tweak.Title);
            }
            var attempted = new List<Tweak>();
            try
            {
                foreach (var item in snapshots)
                {
                    if (Inspect(item.Tweak).Current != item.State.Current) throw new InvalidOperationException("Configuration changed after preflight: " + item.Tweak.Title);
                    if (item.State.Applied) continue;
                    attempted.Add(item.Tweak);
                    Apply(item.Tweak);
                    progress?.Invoke(attempted.Count, snapshots.Length, item.Tweak.Title);
                }
                return attempted.Count;
            }
            catch (Exception error)
            {
                var recovery = new List<string>();
                foreach (var t in attempted.AsEnumerable().Reverse())
                {
                    try { if (GetBackup(t) is not null) Restore(t); }
                    catch (Exception ex) { recovery.Add(t.Title + ": " + ex.Message); }
                }
                throw new InvalidOperationException("Batch stopped: " + error.Message + (recovery.Count == 0
                    ? " Changes made by this batch were restored."
                    : " Recovery needs attention: " + string.Join("; ", recovery)), error);
            }
        }
    }
}

public sealed record Preferences(string Search = "", string Page = "Dashboard", string[]? Favorites = null, string Category = "All categories");
public static class PreferenceFile
{
    public static Preferences Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new() : new();
    public static void Save(string path, Preferences settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings));
        File.Move(path + ".tmp", path, true);
    }
}
