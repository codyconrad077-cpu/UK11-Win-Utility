using System.ComponentModel;
using System.Windows.Media;
using UK11.Core;

namespace UK11.App;

public sealed class SettingRow(Tweak tweak, Inspection inspection, Backup? backup, bool favorite, string? error = null) : INotifyPropertyChanged
{
    public Tweak Tweak { get; } = tweak;
    public Inspection Inspection { get; } = inspection;
    public Backup? Backup { get; } = backup;
    public string? Error { get; } = error;
    public string Title => Tweak.Title;
    public string CategoryLabel => Tweak.Category + (Tweak.Group.Length > 0 ? " / " + Tweak.Group : "");
    public bool CanSelect => !Tweak.Adjustable && CanApply && !Inspection.Applied;
    public bool CanApply => Error is null && Inspection.Supported && Tweak.Writable && (Backup is null || Backup.State == "Restored");
    public bool CanRestore => Error is null && Backup is not null && Backup.State != "Restored";
    public string StateLabel => Error is not null ? "Inspection failed" : !Inspection.Supported ? "Unavailable on this configuration" : Tweak.Adjustable
        ? (Tweak.Writable ? "Adjustable" : "Read-only") + " · " + Tweak.Format(Inspection.Current.Number)
        : Inspection.Applied ? "Target value detected" : CanApply ? "Available to configure" : "Original saved · restore before reapplying";
    public Brush StateBrush => Error is not null ? Brushes.Salmon : !Inspection.Supported ? Brushes.SlateGray : !Tweak.Adjustable && Inspection.Applied ? Brushes.MediumAquamarine : Brushes.LightSkyBlue;
    public string BackupLabel => Backup is null ? "No original saved" : $"{Backup.Created:yyyy-MM-dd HH:mm} UTC · {Backup.State} · Original: {Tweak.Format(Backup.Original.Number)} · Current: {(Inspection.Current.Exists ? Tweak.Format(Inspection.Current.Number) : "unavailable")}";
    private bool selected;
    public bool Selected { get => selected; set { selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    private bool isFavorite = favorite;
    public bool Favorite { get => isFavorite; set { isFavorite = value; PropertyChanged?.Invoke(this, new(nameof(Favorite))); PropertyChanged?.Invoke(this, new(nameof(Star))); } }
    public string Star => Favorite ? "★" : "☆";
    public event PropertyChangedEventHandler? PropertyChanged;
}
