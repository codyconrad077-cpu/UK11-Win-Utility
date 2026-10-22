using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UK11.Core;
using UK11.Windows;

namespace UK11.App;

public partial class MainWindow : Window
{
    private readonly string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UK11Ultimate");
    private readonly Engine engine;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private FileStream? instance;
    private bool busy;
    private string page = "Dashboard";
    public MainWindow()
    {
        InitializeComponent();
        engine = new(new RegistrySettings(), Path.Combine(data, "backups"));
        try
        {
            Directory.CreateDirectory(data);
            instance = new FileStream(Path.Combine(data, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show("UK11 is already open or its data directory is unavailable: " + ex.Message); Application.Current.Shutdown(); return; }
        try
        {
            var prefs = PreferenceFile.Load(Path.Combine(data, "settings.json"));
            Search.Text = prefs.Search;
            ShowPage(prefs.Page);
        }
        catch (Exception ex) { Status.Text = "Settings could not load: " + ex.Message; }
        Loaded += async (_, _) =>
        {
            try
            {
                var h = await Task.Run(Hardware.Inventory);
                Inventory.Text = $"{h.Cpu}\n{h.Threads} logical processors\n{h.Board}\nBIOS: {h.Firmware}\n{h.Os}";
                Sample();
                timer.Start();
                Render();
            }
            catch (Exception ex) { Status.Text = ex.Message; }
        };
        timer.Tick += (_, _) => Sample();
        Closing += (_, e) =>
        {
            if (busy) { e.Cancel = true; return; }
            timer.Stop();
            try { PreferenceFile.Save(Path.Combine(data, "settings.json"), new(Search.Text, page)); }
            catch (Exception ex) { MessageBox.Show("Preferences could not be saved: " + ex.Message); }
        };
        Closed += (_, _) => instance?.Dispose();
    }
    private void Sample()
    {
        try
        {
            var m = Hardware.Memory();
            var used = m.Total - m.Available;
            MemoryText.Text = $"{used / 1073741824d:F1} / {m.Total / 1073741824d:F1} GiB in use";
            MemoryBar.Value = 100d * used / m.Total;
            Uptime.Text = $"System uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\.hh\\:mm\\:ss} • refreshed {DateTime.Now:T}";
        }
        catch (Exception ex) { MemoryText.Text = "Unavailable: " + ex.Message; timer.Stop(); }
    }
    private void ShowPage(string selected)
    {
        page = selected is "Optimisations" or "Backups" ? selected : "Dashboard";
        DashboardPanel.Visibility = page == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        TweaksPanel.Visibility = page == "Optimisations" ? Visibility.Visible : Visibility.Collapsed;
        BackupsPanel.Visibility = page == "Backups" ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = page == "Dashboard" ? "System overview" : page == "Backups" ? "Recovery & activity" : "Choose your changes";
        if (engine is not null) Render();
    }
    private void DashboardClick(object sender, RoutedEventArgs e) => ShowPage("Dashboard");
    private void TweaksClick(object sender, RoutedEventArgs e) => ShowPage("Optimisations");
    private void BackupsClick(object sender, RoutedEventArgs e) => ShowPage("Backups");
    private void RefreshClick(object sender, RoutedEventArgs e) => Render();
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (engine is not null) Render(); }
    private void Render()
    {
        Cards.Children.Clear(); BackupCards.Children.Clear();
        foreach (var t in Catalog.All)
        {
            try
            {
                var state = engine.Inspect(t);
                var backup = engine.GetBackup(t);
                if ($"{t.Title} {t.Category} {t.Effect}".Contains(Search.Text, StringComparison.OrdinalIgnoreCase))
                    Cards.Children.Add(Card(t, state.Detail, state.Supported && !state.Applied, backup is not null && backup.State != "Restored"));
                if (backup is not null) BackupCards.Children.Add(Card(t, $"Backup: {backup.Created:u} • {backup.State}\nOriginal DWORD: {backup.Original.Number} • Current: {(state.Current.Exists ? state.Current.Number.ToString() : "unavailable")}", false, backup.State != "Restored"));
            }
            catch (Exception ex) { Cards.Children.Add(new TextBlock { Text = $"{t.Title}: {ex.Message}", Margin = new(0, 8, 0, 12) }); }
        }
        try
        {
            var log = Path.Combine(data, "backups", "transactions.log");
            Activity.Text = File.Exists(log) ? string.Join(Environment.NewLine, File.ReadLines(log).TakeLast(100)) : "No operations recorded.";
        }
        catch (Exception ex) { Activity.Text = ex.Message; }
    }
    private Border Card(Tweak t, string detail, bool apply, bool restore)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = t.Title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = $"{t.Category} • {t.Risk}\n{t.Effect}\n{detail}\n{t.Restart}", Margin = new(0, 10, 0, 10) });
        var buttons = new WrapPanel();
        var preview = new Button { Content = "Preview / apply", IsEnabled = apply && !busy };
        preview.Click += async (_, _) => await Execute(t, false);
        var undo = new Button { Content = "Restore original", IsEnabled = restore && !busy };
        undo.Click += async (_, _) => await Execute(t, true);
        buttons.Children.Add(preview); buttons.Children.Add(undo); content.Children.Add(buttons);
        return new Border { Background = new SolidColorBrush(Color.FromRgb(16, 29, 44)), CornerRadius = new(12), Padding = new(20), Margin = new(0, 0, 0, 16), Child = content };
    }
    private async Task Execute(Tweak t, bool restore)
    {
        if (busy) return;
        var message = restore
            ? $"Restore the original backed-up value for {t.Title}? External changes will block restoration."
            : $"{t.Effect}\n\nHKCU\\{t.Path}\\{t.Name}\nDWORD → {t.Desired}\n{t.Risk}\n{t.Support}\n{t.Restart}\n\nThe original value is saved before writing and the result is read back. Apply?";
        if (MessageBox.Show(this, message, restore ? "Restore preview" : "Change preview", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        busy = true; IsEnabled = false;
        try
        {
            Status.Text = "Working…";
            await Task.Run(() => { if (restore) engine.Restore(t); else engine.Apply(t); });
            Status.Text = "Registry value verified. " + t.Restart;
        }
        catch (Exception ex) { Status.Text = "Operation failed: " + ex.Message; MessageBox.Show(this, ex.Message, "UK11 operation failed"); }
        finally { busy = false; IsEnabled = true; Render(); }
    }
}
