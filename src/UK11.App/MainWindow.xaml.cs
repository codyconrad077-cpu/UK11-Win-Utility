using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using UK11.Core;
using UK11.Windows;

namespace UK11.App;

public partial class MainWindow : Window
{
    private readonly string data;
    private readonly string? smokePath;
    private readonly Engine engine;
    private readonly WindowsWorkflows workflows;
    private readonly Sensors sensors = new();
    private readonly CpuSampler cpu = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly List<double?> cpuTemperatures = [], gpuTemperatures = [];
    private FileStream? instance;
    private List<Tweak> allTweaks = Catalog.All.ToList(), advanced = [];
    private List<AppItem> apps = [];
    private List<DriverItem> driverUpdates = [];
    private JsonElement system;
    private SensorReading latest = new(null, null, null, "Detecting…", "Unavailable", "Unavailable", "Waiting for sensors");
    private bool ready, busy, sampling, closed;
    private string page = "Home";
    private readonly string[] pageNames = ["Home", "System Restore", "Drivers", "General", "Debloating", "Networking", "Advanced", "Services", "BIOS / SCEWIN"];
    private string Cache => Path.Combine(data, "power-catalogue.json");
    private bool AdvancedPage => page == "Advanced";
    private bool Nvidia => NvidiaTab.IsChecked == true;

    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs();
        smokePath = args.Length >= 3 && args[1] == "--ui-smoke" ? Path.GetFullPath(args[2]) : null;
        // Retain the established data directory so a rebrand cannot orphan existing originals.
        data = smokePath is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UK11Ultimate") : Path.Combine(Path.GetTempPath(), "ApexSmoke-" + Guid.NewGuid());
        engine = new(new WindowsSettings(), Path.Combine(data, "backups")); workflows = new(data);
        if (args.Length >= 3 && args[1] == "--restart-from" && int.TryParse(args[2], out var previousId))
        { try { using var previous = Process.GetProcessById(previousId); previous.WaitForExit(10000); } catch (ArgumentException) { } }
        InitializeComponent();
        try { Directory.CreateDirectory(data); instance = new(Path.Combine(data, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (Exception ex) { MessageBox.Show("Apex is already open or cannot access its backups: " + ex.Message); Application.Current.Shutdown(); return; }
        try
        {
            if (File.Exists(Cache)) allTweaks.AddRange((JsonSerializer.Deserialize<List<Tweak>>(File.ReadAllText(Cache)) ?? []).Where(ValidPowerIdentity));
            var prefs = PreferenceFile.Load(Path.Combine(data, "settings.json")); Search.Text = prefs.Search;
        }
        catch (Exception ex) { Status.Text = "Saved settings need attention: " + ex.Message; }
        ready = true;
        Loaded += async (_, _) =>
        {
            await RefreshSystem(); await Sample(); timer.Start();
            if (smokePath is not null)
            {
                try { await Smoke(); Application.Current.Shutdown(0); }
                catch (Exception ex) { File.WriteAllText(smokePath + ".error.txt", ex.ToString()); Application.Current.Shutdown(1); }
            }
        };
        timer.Tick += async (_, _) => await Sample();
        Closing += (_, e) => { if (busy) { e.Cancel = true; return; } timer.Stop(); try { PreferenceFile.Save(Path.Combine(data, "settings.json"), new(Search.Text, page)); } catch { } };
        Closed += (_, _) => { closed = true; instance?.Dispose(); sensors.Dispose(); if (smokePath is not null && Directory.Exists(data)) Directory.Delete(data, true); };
    }
    private static bool ValidPowerIdentity(Tweak t) => t.Adjustable && Guid.TryParse(t.Scheme, out var s) && Guid.TryParse(t.Path, out var g) && Guid.TryParse(t.Name, out var n) && t.Id == $"power.{s:N}.{g:N}.{n:N}.{(t.Backend == "PowerAC" ? "ac" : "dc")}";
    private void SetStatus(string message, bool error = false) { Status.Text = message; Status.ToolTip = message; StatusDot.Fill = error ? Brushes.Orange : Brushes.DodgerBlue; }
    private void SetBusy(bool value) { busy = value; Navigation.IsEnabled = ContentHost.IsEnabled = BulkButtons.IsEnabled = RefreshButton.IsEnabled = !value; }
    private async Task<WorkflowResult?> Workflow(string action, object? payload = null, bool admin = false)
    {
        if (busy) return null;
        SetBusy(true); SetStatus("Working… " + action.Replace('-', ' '));
        try
        {
            var result = await workflows.RunAsync(action, payload, admin);
            if (!result.Success) { if (smokePath is not null) throw new InvalidOperationException(action + ": " + result.Error); SetStatus(result.Error ?? "Windows operation failed", true); MessageBox.Show(this, result.Error, "Apex Tweaks"); return null; }
            var message = result.Data.Text("message");
            var failed = result.Data.ValueKind == JsonValueKind.Object && result.Data.TryGetProperty("failed", out var f) ? f.Items() : [];
            SetStatus(string.IsNullOrEmpty(message) ? "Windows operation completed." : message, failed.Length > 0);
            if (failed.Length > 0) MessageBox.Show(this, string.Join("\n", failed.Select(x => x.ToString())), "Some operations failed");
            return result;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { SetStatus("Administrator request cancelled. No workflow was started."); return null; }
        catch (Exception ex) { if (smokePath is not null) throw; SetStatus(ex.Message, true); MessageBox.Show(this, ex.Message, "Apex Tweaks"); return null; }
        finally { SetBusy(false); }
    }
    private async Task RefreshSystem()
    {
        if (busy) return;
        SetBusy(true); SetStatus("Detecting your system and supported tweaks…");
        try
        {
            var machine = await Task.Run(Hardware.Inventory); CpuName.Text = machine.Cpu;
            GpuName.Text = await Task.Run(Sensors.GpuIdentity);
            var found = await Task.Run(() => PowerSettings.Discover());
            advanced = await Task.Run(() => AdvancedCatalog.Build(found).ToList());
            var retained = allTweaks.Where(t => t.Adjustable && engine.GetBackup(t) is { State: not "Restored" }).ToArray();
            allTweaks = Catalog.All.Concat(advanced).Concat(retained).DistinctBy(t => t.Id).ToList();
            File.WriteAllText(Cache + ".tmp", JsonSerializer.Serialize(allTweaks.Where(t => t.Adjustable))); File.Move(Cache + ".tmp", Cache, true);
            PlanName.Text = "Power plan · " + PowerSettings.ActiveName();
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            DiskInfo.Text = $"{(drive.TotalSize - drive.AvailableFreeSpace) / 1073741824d:F0} / {drive.TotalSize / 1073741824d:F0} GB used";
            DiskBar.Value = 100d * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
            BuildToggles(); UpdateBackups(); SetStatus("Ready · General and Advanced preserve your original values.");
        }
        catch (Exception ex) { SetStatus("Some system data is unavailable: " + ex.Message, true); BuildToggles(); }
        finally { SetBusy(false); }
    }
    private async Task Sample()
    {
        if (sampling || closed) return;
        sampling = true;
        try
        {
            var memory = Hardware.Memory(); var ram = 100d * (memory.Total - memory.Available) / memory.Total;
            RamUsage.Text = $"{ram:F0}%"; RamGauge.Value = ram; MemoryInfo.Text = $"RAM · {memory.Total / 1073741824d:F1} GiB installed";
            var load = cpu.Sample(); if (load is not null) { CpuUsage.Text = $"{load:F0}%"; CpuGauge.Value = load.Value; }
            var reading = await Task.Run(sensors.Read); if (closed) return;
            latest = reading; GpuUsage.Text = reading.GpuLoad is null ? "N/A" : $"{reading.GpuLoad:F0}%"; GpuGauge.Value = reading.GpuLoad ?? double.NaN;
            Push(cpuTemperatures, reading.CpuTemperature); Push(gpuTemperatures, reading.GpuTemperature); UpdateTemperature();
        }
        catch (Exception ex) { if (!closed) SetStatus("Telemetry unavailable: " + ex.Message, true); }
        finally { sampling = false; }
    }
    private static void Push(List<double?> points, double? value) { points.Add(value); if (points.Count > 60) points.RemoveAt(0); }
    private void UpdateTemperature()
    {
        var gpu = GpuTempTab.IsChecked == true; var value = gpu ? latest.GpuTemperature : latest.CpuTemperature;
        TemperatureKind.Text = gpu ? "GPU" : "CPU"; TemperatureValue.Text = value is null ? "Unavailable" : $"{value:F0}°C";
        SensorStatus.Text = value is null ? (gpu ? "No compatible GPU sensor data is available." : latest.Status) : (gpu ? latest.GpuName + " · " + latest.GpuSensor : latest.CpuSensor);
        TemperaturePlot.Update(gpu ? gpuTemperatures : cpuTemperatures);
    }
    private IEnumerable<Tweak> SectionTweaks() => AdvancedPage ? advanced.Where(t => t.Backend == (DcTab.IsChecked == true ? "PowerDC" : "PowerAC")) : Catalog.All;
    private void BuildToggles()
    {
        if (!ready) return;
        var source = SectionTweaks().Where(t => t.Title.Contains(Search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        var items = source.Select(t =>
        {
            try
            {
                var state = engine.Inspect(t); var backup = engine.GetBackup(t); var owned = backup is { State: not "Restored" };
                var enabled = state.Supported && t.Writable && (state.Applied ? owned : !owned);
                var hint = !state.Supported ? "Unavailable on this Windows configuration. " + t.Support : !t.Writable ? "No validated target default is available. " + t.Effect : state.Applied && !owned ? "Already set outside Apex. No original was invented; this switch is read-only. " + t.Effect : owned && !state.Applied ? "An older original is saved. Use Revert all before applying a different target. " + t.Effect : t.Effect + "\n" + t.Restart;
                return new ToggleItem(t, t.Title, state.Applied, enabled, hint);
            }
            catch (Exception ex) { return new ToggleItem(t, t.Title, false, false, ex.Message); }
        }).ToArray();
        ToggleList.ItemsSource = items;
        TweakCount.Text = $"{items.Count(x => x.Enabled)} changeable · {items.Length} shown · Hover a name for its effect and compatibility";
        if (AdvancedPage) TweakCount.Text = $"{advanced.Count} AC/DC controls in this catalogue · Showing {(DcTab.IsChecked == true ? "battery" : "plugged-in")} settings · Heat and power use can increase";
    }
    private void UpdateBackups()
    {
        var count = allTweaks.Count(t => { try { return engine.GetBackup(t) is { State: not "Restored" }; } catch { return false; } });
        BackupSummary.Text = $"{count} General / Advanced original value(s) are ready to restore. Revert restores originals, not guessed factory values.";
    }
    private async Task ShowPage(string selected)
    {
        if (!ready || busy) return;
        page = pageNames.Contains(selected) ? selected : "Home";
        foreach (var p in new UIElement[] { HomePanel, TweaksPanel, RestorePanel, DriversPanel, DebloatPanel, NetworkPanel, ServicesPanel, BiosPanel }) p.Visibility = Visibility.Collapsed;
        (page switch { "General" or "Advanced" => (UIElement)TweaksPanel, "System Restore" => RestorePanel, "Drivers" => DriversPanel, "Debloating" => DebloatPanel, "Networking" => NetworkPanel, "Services" => ServicesPanel, "BIOS / SCEWIN" => BiosPanel, _ => HomePanel }).Visibility = Visibility.Visible;
        Breadcrumb.Text = "⌂   /   " + page; PageTitle.Text = page == "Home" ? "Welcome to Apex Tweaks" : page;
        PageSubtitle.Text = page switch { "Home" => "Performance, configuration and recovery — made simple.", "General" => "45 Windows preferences. One switch per change.", "Advanced" => "Windows High Performance defaults for supported controls. Review heat and battery trade-offs.", "Drivers" => "Official, hardware-compatible updates with visible results.", "BIOS / SCEWIN" => "Hardware-aware firmware information. Compatibility comes first.", _ => "Real Windows operations. Clear results and recovery." };
        BulkButtons.Visibility = page is "General" or "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.Visibility = page is "General" or "Advanced" ? Visibility.Collapsed : Visibility.Visible;
        PowerSides.Visibility = AdvancedPage ? Visibility.Visible : Visibility.Collapsed;
        var navs = new[] { HomeNav, RestoreNav, DriversNav, GeneralNav, DebloatNav, NetworkNav, AdvancedNav, ServicesNav, BiosNav };
        for (var i = 0; i < pageNames.Length; i++) navs[i].IsChecked = pageNames[i] == page;
        if (page is "General" or "Advanced") BuildToggles();
        else if (page == "System Restore") await LoadRestore();
        else if (page is "Drivers" or "BIOS / SCEWIN") await LoadSystemDetails();
        else if (page == "Debloating") await LoadApps();
        else if (page == "Networking") await LoadNetwork();
        else if (page == "Services") await LoadServices();
    }
    private bool Confirm(string title, string message)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 640, Height = 550, MinWidth = 500, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(19, 22, 28)), FontFamily = FontFamily };
        var grid = new Grid { Margin = new(24) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 18) });
        var content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = message, Foreground = Brushes.LightSteelBlue, FontSize = 13 } }; Grid.SetRow(content, 1); grid.Children.Add(content);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new(0, 0, 10, 0) }; cancel.Click += (_, _) => dialog.DialogResult = false;
        var apply = new Button { Content = "Confirm", Style = (Style)FindResource("Primary") }; apply.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(apply); Grid.SetRow(buttons, 2); grid.Children.Add(buttons); dialog.Content = grid;
        return dialog.ShowDialog() == true;
    }
    private async Task ChangeTweaks(IReadOnlyList<Tweak> targets, bool restore, bool confirm)
    {
        if (busy || targets.Count == 0) { SetStatus("No applicable changes or saved originals in this selection."); return; }
        Dictionary<string, Value> before;
        try { before = targets.ToDictionary(t => t.Id, t => engine.Inspect(t).Current); }
        catch (Exception ex) { SetStatus(ex.Message, true); return; }
        var preview = string.Join("\n\n", targets.Select(t => "• " + t.Title + "\n" + t.Format(before[t.Id].Number) + " → " + (restore ? "saved original" : t.Format(t.Desired))));
        if (confirm && !Confirm(restore ? "Revert all saved tweaks?" : "Apply section tweaks?", preview + "\n\n" + (restore ? "Apex restores its recorded originals and preserves conflicting external values. Apps, drivers, services and DNS use separate recovery controls." : "Original values are saved before writes. Every result is read back. A failure triggers reverse-order recovery of this batch. Advanced performance defaults can increase heat, fan activity and battery drain; benchmark your own workload."))) return;
        SetBusy(true);
        try
        {
            if (restore)
            {
                var errors = new List<string>(); var count = 0;
                await Task.Run(() => { foreach (var t in targets) { try { engine.Restore(t); count++; } catch (Exception ex) { errors.Add(t.Title + ": " + ex.Message); } } });
                SetStatus($"{count} original value(s) restored" + (errors.Count > 0 ? $" · {errors.Count} conflicts/failures" : " and verified."), errors.Count > 0);
                if (errors.Count > 0) MessageBox.Show(this, string.Join("\n\n", errors), "Revert needs attention");
            }
            else { var count = await Task.Run(() => engine.ApplyBatch(targets, expected: before)); SetStatus($"{count} tweak(s) applied and verified. Some settings need sign-out or reopening apps."); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true); MessageBox.Show(this, ex.Message, "Apex Tweaks"); }
        finally { SetBusy(false); BuildToggles(); UpdateBackups(); }
    }
    private async void ToggleClick(object sender, RoutedEventArgs e)
    {
        if (((ToggleButton)sender).Tag is not ToggleItem item || busy) return;
        var turnOn = ((ToggleButton)sender).IsChecked == true;
        if (item.Kind == "service")
        {
            if (!Confirm(turnOn ? "Disable optional component?" : "Restore optional component?", item.Name + "\n\n" + item.Hint)) { await LoadServices(); return; }
            await Workflow(item.Key == "MicrosoftStore" ? "store-change" : "service-change", new { name = item.Key, disable = turnOn }, true); await LoadServices(); return;
        }
        if (item.Tweak is not { } t) return;
        if (t.Adjustable && turnOn && !Confirm("Enable performance default?", t.Title + "\n\n" + t.Effect + "\nTarget: " + t.Format(t.Desired))) { BuildToggles(); return; }
        await ChangeTweaks([t], !turnOn, false);
    }
    private async void ApplyAllClick(object sender, RoutedEventArgs e)
    {
        var targets = SectionTweaks().Where(t => { try { var state = engine.Inspect(t); return t.Writable && state.Supported && !state.Applied && engine.GetBackup(t) is null or { State: "Restored" }; } catch { return false; } }).ToArray();
        await ChangeTweaks(targets, false, true);
    }
    private async void RevertAllClick(object sender, RoutedEventArgs e)
    {
        var originals = allTweaks.Where(t => { try { return engine.GetBackup(t) is { State: not "Restored" }; } catch { return false; } }).ToArray();
        await ChangeTweaks(originals, true, true);
    }
    private async Task LoadRestore()
    {
        UpdateBackups(); var result = await Workflow("restore-list"); if (result is null) return;
        // A standard user's inventory permissions do not establish whether an elevated
        // creation request can succeed. The creation workflow checks and verifies it.
        RestoreStatus.Text = result.Data.Text("message");
        RestorePoints.Text = result.Data.TryGetProperty("points", out var points) ? string.Join("\n\n", points.Items().Select(p => p.Text("SequenceNumber") + " · " + p.Text("Description") + "\n" + p.Text("CreationTime"))) : "";
        if (RestorePoints.Text.Length == 0) RestorePoints.Text = "No restore points are currently reported.";
    }
    private async Task LoadSystemDetails()
    {
        var result = await Workflow("system"); if (result is null) return; system = result.Data;
        InstalledGpuDrivers.Text = system.TryGetProperty("gpu", out var devices) ? string.Join("\n", devices.Items().Select(g => g.Text("Name") + " · Driver " + g.Text("DriverVersion"))) : "No graphics device reported.";
        BiosDetails.Text = $"Motherboard   {system.Text("board")}\nRevision   {system.Text("revision")}\nFirmware vendor   {system.Text("biosVendor")}\nBIOS version   {system.Text("biosVersion")}\nRelease date   {system.Text("biosDate")}\nSecure Boot   {system.Text("secureBoot")}";
    }
    private async Task LoadApps()
    {
        var result = await Workflow("apps-list"); if (result is null) return;
        apps = result.Data.Items().Select(a => new AppItem(a.Text("name"), a.Text("fullName"), a.Flag("microsoft"), a.Flag("auto"))).ToList(); FilterApps();
    }
    private void FilterApps() => AppList.ItemsSource = apps.Where(a => a.Microsoft == (MicrosoftAppsTab.IsChecked == true)).OrderBy(a => a.Name).ToList();
    private async Task RemoveApps(IReadOnlyList<AppItem> selected, bool automatic)
    {
        if (selected.Count == 0) { SetStatus("No matching optional apps are installed."); return; }
        if (!Confirm(automatic ? "Auto debloat preview" : "Remove app?", string.Join("\n", selected.Select(a => "• " + a.Name)) + "\n\nOnly these current-user packages will be removed. This is not a reversible tweak toggle: inventory is backed up, but package files can be deleted and reinstall may require Microsoft Store or the original publisher. Save your work first.")) return;
        await Workflow("apps-remove", new { packages = selected.Select(a => a.FullName).ToArray(), automatic }); await LoadApps();
    }
    private async Task LoadNetwork()
    {
        var result = await Workflow("network-list"); if (result is null) return;
        Adapters.ItemsSource = result.Data.Items().Select(a => new AdapterItem(int.Parse(a.Text("index")), a.Text("name"), $"{a.Text("description")}\n{a.Text("status")} · Link {a.Text("speed")}\nIPv4 DNS: {(a.Flag("dnsAvailable") ? a.GetProperty("dns").ToString() : "Unavailable — " + a.Text("dnsError"))} ")).ToList(); Adapters.SelectedIndex = Adapters.Items.Count > 0 ? 0 : -1;
    }
    private async Task LoadServices()
    {
        var result = await Workflow("service-list"); if (result is null) return;
        ServiceList.ItemsSource = result.Data.Items().Select(s =>
        {
            var name = s.Text("name"); var disabled = s.Text("mode") == "Disabled"; var owned = s.Flag("owned");
            var hint = name switch { "WSearch" => "Disables indexed search. Searches may be slower.", "SysMain" => "Disables prefetch assistance; performance can get worse.", "Spooler" => "Disables local/network printing and some PDF printers.", "XblGameSave" or "XboxNetApiSvc" => "Xbox networking, cloud saves or multiplayer features can stop working.", "MicrosoftStore" => "Blocks the Store app on supported Enterprise/Education editions. Store infrastructure and Windows Update are preserved.", _ => "This optional feature will be unavailable until restored." };
            var supported = !s.TryGetProperty("supported", out _) || s.Flag("supported");
            return new ToggleItem(null, "Disable " + s.Text("title"), disabled || owned, supported && (!disabled || owned), hint + " Current: " + s.Text("status") + " / " + s.Text("mode") + (disabled && !owned ? " Disabled outside Apex; no original exists." : owned ? " Turn off to recover the saved original, including an interrupted operation." : " Original startup mode and running state are backed up before changes."), name, "service");
        }).ToArray();
    }
    private async Task ScanDrivers()
    {
        var result = await Workflow("driver-scan"); if (result is null) return;
        driverUpdates = result.Data.Items().Where(u => u.Text("driverClass").Equals("Display", StringComparison.OrdinalIgnoreCase) && ((u.Text("manufacturer") + " " + u.Text("title")).Contains(Nvidia ? "NVIDIA" : "AMD", StringComparison.OrdinalIgnoreCase) || !Nvidia && u.Text("manufacturer").Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase)))
            .Select(u => new DriverItem(u.Text("id"), int.Parse(u.Text("revision")), u.Text("title"), u.Text("manufacturer"), u.Text("eula"))).ToList();
        DriverResults.Text = driverUpdates.Count == 0 ? "Windows Update reports no applicable updates for this vendor. This does not prove the latest vendor release is installed." : string.Join("\n\n", driverUpdates.Select(u => u.Title)); InstallDriversButton.IsEnabled = driverUpdates.Count > 0;
    }
    private async void InstallDriversClick(object sender, RoutedEventArgs e)
    {
        if (driverUpdates.Count == 0) return;
        if (!Confirm("Review GPU driver installation", string.Join("\n\n", driverUpdates.Select(u => u.Title + "\n" + u.License)) + "\n\nConfirming accepts these license terms. Apex exports third-party driver packages before installation, attempts a Windows restore point and continues with the driver archive if System Restore is unavailable. Windows Update verifies downloads and hardware applicability. Do not interrupt installation. Reboot is never automatic.")) return;
        var result = await Workflow("driver-install", new { vendor = Nvidia ? "NVIDIA" : "AMD", updates = driverUpdates.Select(u => new { id = u.Id, revision = u.Revision, licenseSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(u.License))).ToLowerInvariant() }).ToArray(), acceptTerms = true }, true);
        if (result is not null) DriverResults.Text = JsonSerializer.Serialize(result.Data, new JsonSerializerOptions { WriteIndented = true });
        InstallDriversButton.IsEnabled = false;
    }
    private async void RuntimesClick(object sender, RoutedEventArgs e)
    {
        var preview = await Workflow("runtime-preview"); if (preview is null) return;
        if (!Confirm("Recommended Microsoft runtimes", preview.Data.Text("details") + "\n\nConfirming accepts the displayed package/source terms. Installs or verifies Visual C++ x64/x86, .NET Desktop 8/10 and the DirectX legacy runtime through winget. Installer verification stays enabled; other game runtimes may still be needed.")) return;
        var result = await Workflow("runtime-install", new { acceptTerms = true }); if (result is not null) DriverResults.Text = JsonSerializer.Serialize(result.Data, new JsonSerializerOptions { WriteIndented = true });
    }
    private async void AutoNetworkClick(object sender, RoutedEventArgs e)
    {
        if (Adapters.SelectedItem is not AdapterItem adapter) { SetStatus("No physical adapter is available.", true); return; }
        if (!Confirm("Configure network DNS?", adapter.Name + "\n\nThis sets IPv4 DNS to Cloudflare (1.1.1.1 / 1.0.0.1), flushes the DNS cache and tests name resolution. Original static/DHCP DNS settings are saved. Corporate or VPN networks may require their own DNS. No MTU/TCP guessing or game-ping promise is involved.")) return;
        await Workflow("network-configure", new { index = adapter.Index }, true); await LoadNetwork();
    }
    private async void RestoreNetworkClick(object sender, RoutedEventArgs e) { if (Adapters.SelectedItem is AdapterItem a && Confirm("Restore DNS?", "Restore Apex's saved IPv4 DNS mode for " + a.Name + "? External conflicts are preserved.")) { await Workflow("network-restore", new { index = a.Index }, true); await LoadNetwork(); } }
    private async void NetworkTestClick(object sender, RoutedEventArgs e)
    {
        if (busy) return; SetBusy(true); SetStatus("Testing five ICMP replies from 1.1.1.1…");
        try
        {
            var replies = new List<long>(); var lost = 0; using var ping = new Ping();
            for (var i = 0; i < 5; i++) { var r = await ping.SendPingAsync("1.1.1.1", 2000); if (r.Status == IPStatus.Success) replies.Add(r.RoundtripTime); else lost++; await Task.Delay(100); }
            NetworkResults.Text = $"1.1.1.1 · {lost}/5 lost replies" + (replies.Count > 0 ? $"\nAverage {replies.Average():F1} ms · Range {replies.Min()}–{replies.Max()} ms\nMean successive variation {(replies.Count > 1 ? replies.Zip(replies.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0):F1} ms" : "\nNo ICMP response. Firewall or routing policy may block this test.");
            SetStatus("Connection test completed. This target is not your game server.");
        }
        catch (Exception ex) { NetworkResults.Text = ex.Message; SetStatus("Connection test failed: " + ex.Message, true); }
        finally { SetBusy(false); }
    }
    private async void ReinstallAppClick(object sender, RoutedEventArgs e)
    {
        var history = Path.Combine(data, "apps"); if (!Directory.Exists(history)) { SetStatus("No removed-package inventory is saved."); return; }
        var picker = new OpenFileDialog { InitialDirectory = history, Filter = "Apex package inventory (*.json)|*.json", Title = "Choose the original package inventory" };
        if (picker.ShowDialog(this) != true) return;
        try { var b = JsonDocument.Parse(File.ReadAllText(picker.FileName)); var name = b.RootElement.Text("name"); if (Confirm("Reinstall retained package?", name + "\n\nUses the saved original manifest only if its files remain. Otherwise use Microsoft Store or the original publisher.")) await Workflow("apps-reinstall", new { name }); await LoadApps(); }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }
    private void Launch(string path, bool shell = true) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = shell }); } catch (Exception ex) { SetStatus(ex.Message, true); } }
    private async void NavigateClick(object sender, RoutedEventArgs e) => await ShowPage(((RadioButton)sender).Tag.ToString()!);
    private async void RefreshClick(object sender, RoutedEventArgs e) { if (page == "Home") await RefreshSystem(); else await ShowPage(page); }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (ready) BuildToggles(); }
    private void PowerSideClick(object sender, RoutedEventArgs e) => BuildToggles();
    private void TempTabClick(object sender, RoutedEventArgs e) => UpdateTemperature();
    private void AppTabClick(object sender, RoutedEventArgs e) => FilterApps();
    private void AdapterChanged(object sender, SelectionChangedEventArgs e) => AdapterDetails.Text = (Adapters.SelectedItem as AdapterItem)?.Detail ?? "No physical network adapter is reported.";
    private void DriverVendorClick(object sender, RoutedEventArgs e) { driverUpdates.Clear(); DriverResults.Text = "Scan Windows Update for this vendor's applicable drivers."; InstallDriversButton.IsEnabled = false; }
    private async void ScanDriversClick(object sender, RoutedEventArgs e) => await ScanDrivers();
    private async void DriverBackupClick(object sender, RoutedEventArgs e) { if (Confirm("Export installed drivers?", "Exports installed third-party driver packages to Apex's local driver-backups folder. Windows UAC approval is required. No drivers will be removed.")) { var r = await Workflow("driver-backup", admin: true); if (r is not null) MessageBox.Show(this, "Driver archive: " + r.Data.Text("path"), "Driver export verified"); } }
    private void VendorWebsiteClick(object sender, RoutedEventArgs e) => Launch(Nvidia ? "https://www.nvidia.com/Download/index.aspx" : "https://www.amd.com/en/support/download/drivers.html");
    private void DduClick(object sender, RoutedEventArgs e) => Launch("https://www.wagnardsoft.com/");
    private void DeviceManagerClick(object sender, RoutedEventArgs e) => Launch("devmgmt.msc");
    private void ProtectionClick(object sender, RoutedEventArgs e) => Launch("SystemPropertiesProtection.exe");
    private void WindowsRecoveryClick(object sender, RoutedEventArgs e) => Launch("rstrui.exe");
    private void InstalledAppsClick(object sender, RoutedEventArgs e) => Launch("ms-settings:appsfeatures");
    private async void CreateRestoreClick(object sender, RoutedEventArgs e) { if (Confirm("Create Windows restore point?", "Requires System Protection enabled on the system drive. Windows enforces its own frequency/free-space rules; Apex does not bypass them.")) { await Workflow("restore-create", admin: true); await LoadRestore(); } }
    private async void AutoDebloatClick(object sender, RoutedEventArgs e) => await RemoveApps(apps.Where(a => a.Automatic).ToArray(), true);
    private async void RemoveAppClick(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is AppItem app) await RemoveApps([app], false); }
    private void SensorHelpClick(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Hardware sensor access", latest.Status + "\n\nApex uses LibreHardwareMonitor 0.9.6. CPU temperature support depends on hardware, available sensor interfaces/drivers and permissions. No temperature is estimated and Windows driver/security checks stay enabled.\n\nConfirm to restart Apex with the standard Windows administrator prompt. Unsupported hardware can still remain unavailable.")) return;
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--restart-from " + Environment.ProcessId }); Close(); }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }
    private void ExportBiosClick(object sender, RoutedEventArgs e)
    {
        if (system.ValueKind != JsonValueKind.Object) { SetStatus("BIOS inventory has not loaded."); return; }
        var picker = new SaveFileDialog { FileName = "Apex-BIOS-report.json", Filter = "JSON report|*.json" };
        if (picker.ShowDialog(this) == true) { try { File.WriteAllText(picker.FileName, JsonSerializer.Serialize(system, new JsonSerializerOptions { WriteIndented = true })); SetStatus("Read-only BIOS report exported."); } catch (Exception ex) { SetStatus(ex.Message, true); } }
    }
    private void OemWebsiteClick(object sender, RoutedEventArgs e)
    {
        var board = system.Text("board").ToUpperInvariant();
        var url = board.Contains("ASUSTEK") || board.Contains("ASUS") ? "https://www.asus.com/support/" : board.Contains("MICRO-STAR") || board.Contains("MSI") ? "https://www.msi.com/support" : board.Contains("GIGABYTE") ? "https://www.gigabyte.com/Support" : board.Contains("ASROCK") ? "https://www.asrock.com/support/" : board.Contains("DELL") ? "https://www.dell.com/support/home" : board.Contains("LENOVO") ? "https://support.lenovo.com/" : board.Contains("HP") ? "https://support.hp.com/" : "https://www.ami.com/bios-uefi-utilities/";
        Launch(url);
    }
    private void LicensesClick(object sender, RoutedEventArgs e)
    {
        var text = "Apex Tweaks — made by riley\n\nLibreHardwareMonitorLib 0.9.6 (MPL 2.0)\nUnmodified library source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6\nMicrosoft System.Management and .NET runtime: Microsoft open-source licenses.\n\n";
        var assembly = Assembly.GetExecutingAssembly(); foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains("license", StringComparison.OrdinalIgnoreCase))) { using var stream = assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); text += reader.ReadToEnd() + "\n\n"; }
        Confirm("About & licenses", text);
    }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private async Task Smoke()
    {
        await Task.Delay(2300); await Sample();
        if (string.IsNullOrWhiteSpace(CpuName.Text) || RamGauge.Value is < 0 or > 100) throw new Exception("Home inventory failed.");
        await ShowPage("General"); Search.Text = "transparency"; if (ToggleList.Items.Count != 1) throw new Exception("General search failed."); Search.Text = "";
        if (ToggleList.Items.Count != 45) throw new Exception("General catalogue count failed.");
        await ShowPage("Advanced"); var ac = ToggleList.Items.Count; DcTab.IsChecked = true; BuildToggles(); if (ac == 0 || ToggleList.Items.Count == 0 || advanced.Count > 120) throw new Exception("Advanced AC/DC catalogue failed.");
        AcTab.IsChecked = true; BuildToggles();
        foreach (var selected in new[] { "System Restore", "Drivers", "Debloating", "Networking", "Services", "BIOS / SCEWIN" }) await ShowPage(selected);
        var savedWidth = Width; var savedHeight = Height; Width = MinWidth; Height = MinHeight; await ShowPage("General"); UpdateLayout();
        if (Search.ActualWidth < 200 || ToggleList.ActualWidth < 500) throw new Exception("Minimum-size toggle layout failed.");
        Width = savedWidth; Height = savedHeight; await ShowPage("Home"); UpdateLayout();
        var directory = Path.GetDirectoryName(smokePath!)!; Directory.CreateDirectory(directory); Capture(smokePath!);
        await ShowPage("General"); UpdateLayout(); Capture(Path.Combine(directory, "ui-preview-catalogue.png"));
        File.WriteAllText(smokePath + ".json", JsonSerializer.Serialize(new { branding = "Apex Tweaks — made by riley", general = Catalog.All.Count, advanced = advanced.Count, advancedWritable = advanced.Count(t => t.Writable), pages = pageNames, uiChecks = 12, changesApplied = 0, cpuTemperatureAvailable = latest.CpuTemperature is not null, gpuTemperatureAvailable = latest.GpuTemperature is not null }));
    }
    private void Capture(string path) { var visual = (FrameworkElement)Content; var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream); }
}
