using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UK11.Core;
using UK11.Windows;

namespace UK11.App;

public partial class MainWindow : Window
{
    private readonly string data;
    private readonly Engine engine;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CpuSampler cpu = new();
    private readonly List<double> cpuHistory = [], memoryHistory = [];
    private readonly HashSet<string> favorites = [];
    private List<Tweak> catalogue = Catalog.All.ToList();
    private List<SettingRow> rows = [];
    private FileStream? instance;
    private bool busy, initialized;
    private string page = "Dashboard";
    private readonly string? smokePath;
    private string PowerCache => Path.Combine(data, "power-catalogue.json");
    private SettingRow? Selected => TweakList.SelectedItem as SettingRow;

    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs();
        smokePath = args.Length >= 3 && args[1] == "--ui-smoke" ? Path.GetFullPath(args[2]) : null;
        data = smokePath is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UK11Ultimate") : Path.Combine(Path.GetTempPath(), "UK11UiSmoke-" + Guid.NewGuid());
        engine = new(new WindowsSettings(), Path.Combine(data, "backups"));
        InitializeComponent();
        try
        {
            Directory.CreateDirectory(data);
            instance = new FileStream(Path.Combine(data, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show("UK11 is already open or its data directory is unavailable: " + ex.Message); Application.Current.Shutdown(); return; }
        Categories.ItemsSource = new[] { "All categories" }.Concat(Catalog.All.Select(t => t.Category).Append("Power configuration").Distinct().Order()).ToArray();
        States.ItemsSource = new[] { "All states", "Available", "At target", "Unavailable", "Favourites" };
        Categories.SelectedIndex = States.SelectedIndex = 0;
        try
        {
            var prefs = PreferenceFile.Load(Path.Combine(data, "settings.json"));
            Search.Text = prefs.Search;
            foreach (var id in prefs.Favorites ?? []) favorites.Add(id);
            if (Categories.Items.Contains(prefs.Category)) Categories.SelectedItem = prefs.Category;
            page = prefs.Page;
            if (File.Exists(PowerCache))
            {
                var saved = JsonSerializer.Deserialize<List<Tweak>>(File.ReadAllText(PowerCache)) ?? [];
                catalogue.AddRange(saved.Where(t => t.Adjustable && ValidIdentity(t)));
            }
        }
        catch (Exception ex) { SetStatus("Saved preferences or power catalogue could not load: " + ex.Message, true); }
        initialized = true;
        ShowPage(page);
        Loaded += async (_, _) =>
        {
            await Rescan();
            Sample(); cpu.Sample(); timer.Start();
            if (smokePath is not null)
            {
                try { await SmokeTest(); Application.Current.Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(smokePath + ".error.txt", ex.ToString()); Application.Current.Shutdown(1); }
            }
        };
        timer.Tick += (_, _) => Sample();
        Closing += (_, e) =>
        {
            if (busy) { e.Cancel = true; return; }
            timer.Stop(); SavePreferences();
        };
        Closed += (_, _) =>
        {
            instance?.Dispose();
            if (smokePath is not null && Directory.Exists(data)) Directory.Delete(data, true);
        };
    }
    private static bool ValidIdentity(Tweak t) => Guid.TryParse(t.Scheme, out var s) && Guid.TryParse(t.Path, out var g) && Guid.TryParse(t.Name, out var n)
        && t.Id == $"power.{s:N}.{g:N}.{n:N}.{(t.Backend == "PowerAC" ? "ac" : "dc")}";
    private async Task Rescan()
    {
        if (busy) return;
        SetBusy(true); SetStatus("Reading hardware, stored values and Windows power capabilities…");
        try
        {
            var h = await Task.Run(Hardware.Inventory);
            CpuName.Text = h.Cpu; BoardName.Text = h.Board; FirmwareName.Text = h.Firmware; OsName.Text = h.Os;
            ThreadText.Text = h.Threads + " logical processors";
            string? warning = null;
            try
            {
                var discovered = await Task.Run(() => PowerSettings.Discover());
                // Preserve descriptors with unresolved originals from previously active power plans.
                var retained = catalogue.Where(t => t.Adjustable && engine.GetBackup(t) is { State: not "Restored" });
                catalogue = Catalog.All.Concat(discovered).Concat(retained).DistinctBy(t => t.Id).ToList();
                File.WriteAllText(PowerCache + ".tmp", JsonSerializer.Serialize(catalogue.Where(t => t.Adjustable)));
                File.Move(PowerCache + ".tmp", PowerCache, true);
                PlanName.Text = PowerSettings.ActiveName();
            }
            catch (Exception ex) { warning = "Power discovery incomplete: " + ex.Message; PlanName.Text = "Unavailable"; }
            RefreshRows();
            SetStatus(warning ?? "Scan complete · No changes made", warning is not null);
        }
        catch (Exception ex) { SetStatus("Scan failed: " + ex.Message, true); }
        finally { SetBusy(false); }
    }
    private void Sample()
    {
        try
        {
            var m = Hardware.Memory(); var used = m.Total - m.Available;
            var percent = 100d * used / m.Total;
            MemoryText.Text = $"{used / 1073741824d:F1} / {m.Total / 1073741824d:F1} GiB";
            MemoryPercent.Text = $"{percent:F0}% used";
            Push(memoryHistory, percent); Draw(MemoryLine, MemoryCanvas, memoryHistory);
            var load = cpu.Sample();
            if (load is not null) { CpuText.Text = $"{load:F1}%"; Push(cpuHistory, load.Value); Draw(CpuLine, CpuCanvas, cpuHistory); }
            Uptime.Text = $"Uptime {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\.hh\\:mm\\:ss} · physical RAM";
        }
        catch (Exception ex) { SetStatus("Telemetry unavailable: " + ex.Message, true); }
    }
    private static void Push(List<double> values, double value) { values.Add(value); if (values.Count > 60) values.RemoveAt(0); }
    private static void Draw(System.Windows.Shapes.Polyline line, Canvas canvas, List<double> values)
    {
        var width = Math.Max(1, canvas.ActualWidth); var height = canvas.Height;
        line.Points = new PointCollection(values.Select((v, i) => new Point(i * width / 59, height * (1 - v / 100))));
    }
    private void RefreshRows()
    {
        var selectedIds = rows.Where(r => r.Selected).Select(r => r.Tweak.Id).ToHashSet();
        var currentId = Selected?.Tweak.Id;
        rows = catalogue.Select(t =>
        {
            try { return new SettingRow(t, engine.Inspect(t), engine.GetBackup(t), favorites.Contains(t.Id)); }
            catch (Exception ex) { return new SettingRow(t, new(false, false, new(false), ex.Message), null, favorites.Contains(t.Id), ex.Message); }
        }).ToList();
        foreach (var r in rows)
        {
            r.Selected = selectedIds.Contains(r.Tweak.Id) && r.CanSelect;
            r.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SettingRow.Selected)) UpdateSelectionCount(); };
        }
        CatalogueTotal.Text = rows.Count.ToString();
        CatalogueBreakdown.Text = $"{Catalog.All.Count} Windows preferences\n{rows.Count(r => r.Tweak.Adjustable)} AC/DC power targets\nNo benchmark claims";
        AvailableCount.Text = rows.Count(r => !r.Tweak.Adjustable && r.Inspection.Supported).ToString();
        BackupCount.Text = rows.Count(r => r.CanRestore).ToString();
        BackupList.ItemsSource = rows.Where(r => r.Backup is not null).OrderByDescending(r => r.Backup!.Created).ToList();
        FilterRows(currentId);
        try
        {
            var log = Path.Combine(data, "backups", "transactions.log");
            Activity.Text = File.Exists(log) ? string.Join(Environment.NewLine, File.ReadLines(log).TakeLast(80)) : "No changes recorded. Your original values will appear here after applying a setting.";
        }
        catch (Exception ex) { Activity.Text = "Activity log unavailable: " + ex.Message; }
    }
    private void FilterRows(string? selectedId = null)
    {
        if (!initialized) return;
        selectedId ??= Selected?.Tweak.Id;
        var category = Categories.SelectedItem?.ToString() ?? "All categories";
        var state = States.SelectedItem?.ToString() ?? "All states";
        var visible = rows.Where(r => (category == "All categories" || r.Tweak.Category == category)
            && $"{r.Title} {r.Tweak.Effect} {r.CategoryLabel} {r.Tweak.Id}".Contains(Search.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            && (state switch { "Available" => r.CanApply, "At target" => !r.Tweak.Adjustable && r.Inspection.Applied, "Unavailable" => !r.Inspection.Supported, "Favourites" => r.Favorite, _ => true }))
            .OrderBy(r => r.Tweak.Category).ThenBy(r => r.Title).ToList();
        TweakList.ItemsSource = visible;
        TweakList.SelectedItem = visible.FirstOrDefault(r => r.Tweak.Id == selectedId) ?? visible.FirstOrDefault();
        ResultCount.Text = $"{visible.Count} of {rows.Count} targets";
        UpdateSelectionCount(); ShowDetail();
    }
    private void UpdateSelectionCount()
    {
        if (!initialized) return;
        var n = rows.Count(r => r.Selected);
        SelectionCount.Text = n == 0 ? "Select preferences to review as a batch" : $"{n} preference{(n == 1 ? "" : "s")} selected";
        ReviewButton.IsEnabled = n > 0 && !busy;
    }
    private void ShowDetail()
    {
        if (!initialized) return;
        var r = Selected;
        ApplyButton.IsEnabled = r?.CanApply == true && (!r.Inspection.Applied || r.Tweak.Adjustable) && !busy;
        RestoreButton.IsEnabled = r?.CanRestore == true && !busy;
        TargetChoices.Visibility = TargetNumber.Visibility = Visibility.Collapsed;
        if (r is null) { DetailTitle.Text = "No matching settings"; DetailCategory.Text = "Adjust your filters to explore the library."; DetailEffect.Text = DetailCurrent.Text = DetailRisk.Text = DetailEvidence.Text = DetailRestart.Text = DetailPath.Text = TargetLabel.Text = RangeText.Text = ""; return; }
        var t = r.Tweak;
        DetailTitle.Text = t.Title; DetailCategory.Text = r.CategoryLabel;
        DetailCurrent.Text = r.Inspection.Current.Exists ? t.Format(r.Inspection.Current.Number) : "Unavailable";
        DetailEffect.Text = r.Error ?? t.Effect; DetailRisk.Text = t.Risk; DetailEvidence.Text = t.Evidence; DetailRestart.Text = t.Restart;
        DetailPath.Text = t.Adjustable ? $"{t.Backend}\nPlan: {t.Scheme}\nGroup: {t.Path}\nSetting: {t.Name}\n{t.Id}" : $"HKCU\\{t.Path}\\{t.Name}\nDWORD → {t.Desired}\n{t.Id}";
        TargetLabel.Text = t.Adjustable ? "Choose a Windows-reported value" : t.Format(t.Desired);
        RangeText.Text = t.Support;
        if (t.Adjustable && t.Choices.Length > 0)
        {
            TargetChoices.ItemsSource = t.Choices;
            TargetChoices.SelectedItem = t.Choices.FirstOrDefault(c => c.Number == r.Inspection.Current.Number);
            TargetChoices.Visibility = Visibility.Visible;
        }
        else if (t.Adjustable)
        {
            TargetNumber.Text = unchecked((uint)r.Inspection.Current.Number).ToString();
            TargetNumber.Visibility = Visibility.Visible;
            RangeText.Text = $"Range {t.Minimum}–{t.Maximum} · step {t.Increment} {t.Units}\n" + (t.Writable ? "Only valid OS-reported values accepted." : "No writable range was reported.");
        }
    }
    private void ShowPage(string requested)
    {
        if (!initialized) return;
        page = requested is "Optimisations" or "Backups" ? requested : "Dashboard";
        DashboardPanel.Visibility = page == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        TweaksPanel.Visibility = page == "Optimisations" ? Visibility.Visible : Visibility.Collapsed;
        BackupsPanel.Visibility = page == "Backups" ? Visibility.Visible : Visibility.Collapsed;
        OverviewNav.IsChecked = page == "Dashboard"; CatalogueNav.IsChecked = page == "Optimisations"; RecoveryNav.IsChecked = page == "Backups";
        Heading.Text = page == "Dashboard" ? "Your system. In focus." : page == "Backups" ? "A clear path back." : "Your optimisation library.";
        Subheading.Text = page == "Dashboard" ? "Live readings. Deliberate changes. Complete control." : page == "Backups" ? "Exact originals, verified restoration, and a visible audit trail." : "Explore preferences and hardware-exposed controls. Review before applying.";
    }
    private void SetBusy(bool value) { busy = value; RescanButton.IsEnabled = !value; UpdateSelectionCount(); ShowDetail(); }
    private void SetStatus(string text, bool error = false) { Status.Text = text; Status.ToolTip = text; StatusDot.Fill = error ? Brushes.Salmon : Brushes.MediumAquamarine; }
    private void SavePreferences()
    {
        if (!initialized) return;
        try { PreferenceFile.Save(Path.Combine(data, "settings.json"), new(Search.Text, page, favorites.Order().ToArray(), Categories.SelectedItem?.ToString() ?? "All categories")); }
        catch (Exception ex) { SetStatus("Could not save preferences: " + ex.Message, true); }
    }
    private bool Confirm(string title, IReadOnlyList<(Tweak Tweak, string Change)> changes, string note)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 680, Height = 640, MinWidth = 540, MinHeight = 440, Background = new SolidColorBrush(Color.FromRgb(12, 19, 31)), WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily };
        var grid = new Grid { Margin = new(26) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold }); header.Children.Add(new TextBlock { Text = note, Foreground = Brushes.LightSlateGray, Margin = new(0, 12, 0, 20) }); grid.Children.Add(header);
        var body = new StackPanel();
        foreach (var (t, change) in changes)
        {
            var block = new StackPanel(); block.Children.Add(new TextBlock { Text = t.Title, FontSize = 15, FontWeight = FontWeights.SemiBold });
            block.Children.Add(new TextBlock { Text = change + "\n" + t.Risk + "\n" + t.Restart, Foreground = Brushes.LightSteelBlue, Margin = new(0, 8, 0, 0) });
            block.Children.Add(new TextBlock { Text = t.Effect, Foreground = Brushes.SlateGray, Margin = new(0, 8, 0, 0) });
            body.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(20, 32, 48)), CornerRadius = new(10), Padding = new(18), Margin = new(0, 0, 0, 12), Child = block });
        }
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new(0, 0, 10, 0) }; cancel.Click += (_, _) => dialog.DialogResult = false;
        var confirm = new Button { Content = "Confirm " + (title.Contains("restore", StringComparison.OrdinalIgnoreCase) ? "restore" : "changes"), Style = (Style)FindResource("Primary") }; confirm.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); Grid.SetRow(buttons, 2); grid.Children.Add(buttons); dialog.Content = grid;
        return dialog.ShowDialog() == true;
    }
    private async Task Apply(IReadOnlyList<Tweak> targets)
    {
        if (busy || targets.Count == 0) return;
        (Tweak, string)[] preview;
        Dictionary<string, Value> before;
        try
        {
            before = targets.ToDictionary(t => t.Id, t => engine.Inspect(t).Current);
            preview = targets.Select(t => (t, $"Current: {t.Format(before[t.Id].Number)} → Target: {t.Format(t.Desired)}")).ToArray();
        }
        catch (Exception ex) { SetStatus("Preview could not read the current setting: " + ex.Message, true); return; }
        if (!Confirm("Review configuration changes", preview, "Original values are saved before writes. Every result is read back. If a batch fails, changes attempted in that batch are restored where possible.")) return;
        SetBusy(true);
        try
        {
            var changed = await Task.Run(() => engine.ApplyBatch(targets, (n, total, title) => Dispatcher.Invoke(() => SetStatus($"Applying {n}/{total} · {title}")), before));
            SetStatus($"{changed} stored setting(s) verified · Follow each setting's refresh guidance");
        }
        catch (Exception ex) { SetStatus(ex.Message, true); MessageBox.Show(this, ex.Message, "Configuration needs attention"); }
        finally { SetBusy(false); RefreshRows(); }
    }
    private async Task Restore(IReadOnlyList<SettingRow> targets)
    {
        if (busy) return;
        if (targets.Count == 0) { SetStatus("No pending original values to restore."); return; }
        if (!Confirm("Review restore", targets.Select(r => (r.Tweak, $"Current: {r.Tweak.Format(r.Inspection.Current.Number)} → Original: {r.Tweak.Format(r.Backup!.Original.Number)}")).ToArray(), "Restore only UK11's saved originals. Conflicting external values are preserved and reported; other settings continue restoring.")) return;
        SetBusy(true);
        try
        {
            var errors = new List<string>(); var restored = 0;
            await Task.Run(() => { foreach (var r in targets) { try { engine.Restore(r.Tweak); restored++; } catch (Exception ex) { errors.Add(r.Title + ": " + ex.Message); } } });
            SetStatus($"{restored} original value(s) restored" + (errors.Count > 0 ? $" · {errors.Count} need attention" : " · Registry/API read-back verified"), errors.Count > 0);
            if (errors.Count > 0) MessageBox.Show(this, string.Join("\n\n", errors), "Restore conflicts or failures");
        }
        finally { SetBusy(false); RefreshRows(); }
    }
    private void DashboardClick(object sender, RoutedEventArgs e) => ShowPage("Dashboard");
    private void TweaksClick(object sender, RoutedEventArgs e) => ShowPage("Optimisations");
    private void BackupsClick(object sender, RoutedEventArgs e) => ShowPage("Backups");
    private async void RefreshClick(object sender, RoutedEventArgs e) => await Rescan();
    private void SearchChanged(object sender, TextChangedEventArgs e) => FilterRows();
    private void FilterChanged(object sender, SelectionChangedEventArgs e) => FilterRows();
    private void TweakSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetail();
    private void SelectionChecked(object sender, RoutedEventArgs e) => UpdateSelectionCount();
    private void FavoritesClick(object sender, RoutedEventArgs e) { Categories.SelectedIndex = 0; States.SelectedItem = "Favourites"; ShowPage("Optimisations"); }
    private void CategoryShortcut(object sender, RoutedEventArgs e) { Categories.SelectedItem = ((Button)sender).Tag; States.SelectedIndex = 0; ShowPage("Optimisations"); }
    private void StarClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not SettingRow r) return;
        r.Favorite = !r.Favorite;
        if (r.Favorite) favorites.Add(r.Tweak.Id); else favorites.Remove(r.Tweak.Id);
        SavePreferences(); if (States.SelectedItem?.ToString() == "Favourites") FilterRows();
    }
    private async void ReviewClick(object sender, RoutedEventArgs e) => await Apply(rows.Where(r => r.Selected && r.CanSelect).Select(r => r.Tweak).ToArray());
    private async void ApplyDetailClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { CanApply: true } r) return;
        var t = r.Tweak;
        if (t.Adjustable)
        {
            int desired;
            if (t.Choices.Length > 0 && TargetChoices.SelectedItem is Choice choice) desired = choice.Number;
            else if (t.Choices.Length == 0 && uint.TryParse(TargetNumber.Text, out var n)) desired = unchecked((int)n);
            else { SetStatus("Choose a valid OS-reported option or whole number.", true); return; }
            if (!t.Accepts(desired)) { SetStatus("That value is outside the allowed range or increment.", true); return; }
            t = t with { Desired = desired };
        }
        try { await Apply([t]); } catch (Exception ex) { SetStatus(ex.Message, true); }
    }
    private async void RestoreDetailClick(object sender, RoutedEventArgs e) { if (Selected is { CanRestore: true } r) await Restore([r]); }
    private async void RestoreRowClick(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is SettingRow r) await Restore([r]); }
    private async void RestoreAllClick(object sender, RoutedEventArgs e) => await Restore(rows.Where(r => r.CanRestore).ToArray());
    private void CopyClick(object sender, RoutedEventArgs e) { try { Clipboard.SetText(DetailPath.Text); SetStatus("Implementation copied."); } catch (Exception ex) { SetStatus(ex.Message, true); } }
    private void SourceClick(object sender, RoutedEventArgs e) { if (Selected is null) return; try { Process.Start(new ProcessStartInfo(Selected.Tweak.Source) { UseShellExecute = true }); } catch (Exception ex) { SetStatus(ex.Message, true); } }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private async Task SmokeTest()
    {
        await Task.Delay(2200); Sample();
        if (rows.Count < Catalog.All.Count || string.IsNullOrWhiteSpace(CpuName.Text)) throw new InvalidOperationException("Dashboard inventory did not populate.");
        ShowPage("Optimisations"); Categories.SelectedIndex = 0; Search.Text = "transparency";
        if (TweakList.Items.Count != 1 || Selected?.Tweak.Id != "visual.transparency") throw new InvalidOperationException("Catalogue search/selection failed.");
        var favorite = Selected!; favorite.Favorite = true; favorites.Add(favorite.Tweak.Id); SavePreferences();
        if (PreferenceFile.Load(Path.Combine(data, "settings.json")).Favorites?.Contains(favorite.Tweak.Id) != true) throw new InvalidOperationException("Favourite persistence failed.");
        Search.Text = ""; States.SelectedItem = "Favourites";
        if (TweakList.Items.Count != 1) throw new InvalidOperationException("Favourite filter failed.");
        States.SelectedIndex = 0; Categories.SelectedItem = "Power configuration";
        if (rows.Any(r => r.Tweak.Adjustable) && TweakList.Items.Count == 0) throw new InvalidOperationException("Power filter failed.");
        Categories.SelectedIndex = 0; ShowPage("Backups");
        if (BackupsPanel.Visibility != Visibility.Visible) throw new InvalidOperationException("Recovery navigation failed.");
        ShowPage("Dashboard"); await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render);
        var directory = Path.GetDirectoryName(smokePath!)!; Directory.CreateDirectory(directory);
        Capture(smokePath!);
        ShowPage("Optimisations"); await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render);
        Capture(Path.Combine(directory, Path.GetFileNameWithoutExtension(smokePath!) + "-catalogue.png"));
        File.WriteAllText(smokePath + ".json", JsonSerializer.Serialize(new { catalogueTargets = rows.Count, registryPreferences = Catalog.All.Count, powerTargets = rows.Count(r => r.Tweak.Adjustable), distinctPowerSettings = rows.Where(r => r.Tweak.Adjustable).Select(r => r.Tweak.Path + "/" + r.Tweak.Name).Distinct().Count(), assertions = 7, changesApplied = 0 }));
    }
    private void Capture(string path)
    {
        var visual = (FrameworkElement)Content;
        var image = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
