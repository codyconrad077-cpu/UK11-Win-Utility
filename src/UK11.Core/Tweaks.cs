using System.Text.Json;

namespace UK11.Core;

public sealed record Tweak(string Id, string Title, string Category, string Path, string Name, int Desired, string Effect, string Restart)
{
    public string Risk => "Low • current user only";
    public string Permissions => "Standard user";
    public string Support => "Windows 10/11 desktop; requires an existing DWORD value";
}

public static class Catalog
{
    // Only existing DWORDs are accepted: missing/unknown configurations are unsupported.
    public static IReadOnlyList<Tweak> All { get; } = new[]
    {
        new Tweak("visual.transparency", "Reduce desktop transparency", "Visual effects",
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0,
            "Uses opaque desktop surfaces. May reduce compositing work; no guaranteed FPS benefit.", "Sign out and back in if surfaces do not refresh."),
        new Tweak("visual.taskbar-animation", "Reduce taskbar animations", "Visual effects",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0,
            "Removes taskbar transitions. A responsiveness preference, not a measured game performance improvement.", "Sign out and back in to refresh Explorer."),
        new Tweak("capture.app-capture", "Disable app game capture", "Gaming",
            @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0,
            "Disables this user’s app capture setting. Recording may become unavailable; other capture policies can override it.", "Close and reopen games and Game Bar.")
    };
}

public sealed record Value(bool Exists, int Number = 0);
public interface ISettingsStore
{
    bool Supported { get; }
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
        if (!store.Supported) return new(false, false, new(false), "Unsupported operating system");
        var v = store.Read(t);
        return new(v.Exists, v.Exists && v.Number == t.Desired, v,
            !v.Exists ? "Unavailable: setting absent or not DWORD" : v.Number == t.Desired ? "Target value detected" : "Available");
    }
    public Backup? GetBackup(Tweak t)
    {
        if (!File.Exists(FileFor(t))) return null;
        var b = JsonSerializer.Deserialize<Backup>(File.ReadAllText(FileFor(t))) ?? throw new InvalidDataException("Empty backup");
        if (b.TweakId != t.Id || b.Original is null || !b.Original.Exists || b.Applied != new Value(true, t.Desired)
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
                store.Write(t, b.Original);
                if (store.Read(t) != b.Original) throw new IOException("Restore verification failed. Backup retained.");
                Save(t, b with { State = "Restored" });
                Log(t, "Restore verified");
            }
            catch (Exception ex) { Log(t, "Restore failed: " + ex.Message); throw; }
        }
    }
}

public sealed record Preferences(string Search = "", string Page = "Dashboard");
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
