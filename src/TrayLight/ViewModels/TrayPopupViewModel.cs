using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TrayLight.Models;
using TrayLight.Resources;
using TrayLight.Services;
using TrayLight.Services.Actions;
using TrayLight.Services.Logging;

namespace TrayLight.ViewModels;

public partial class TrayPopupViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Hardcoded, non-configurable footer text. Per spec this line MUST always
    /// be displayed and cannot be hidden via configuration.
    /// </summary>
    public const string PoweredByPrefix = "Powered by ";
    public const string PoweredByLinkText = "headsinthecloud.blog";
    public const string PoweredByUrl  = "https://headsinthecloud.blog";

    private readonly IConfigurationService _configService;
    private readonly ISystemInfoService    _systemInfo;
    private readonly IActionExecutor       _actionExecutor;
    private readonly ILogoService          _logoService;
    private readonly AppRefreshService?    _appRefresh;
    private readonly ILogger<TrayPopupViewModel>? _logger;

    /// <summary>
    /// Re-renders <see cref="LastSyncDisplay"/> on a UI cadence so the
    /// "Last refreshed" age increases live while the popup stays open.
    /// Without this the value is computed once at popup creation and never
    /// updates (e.g. stays at "just now" or, if the app has been running
    /// a long time, can drift into thousands-of-minutes territory).
    /// </summary>
    private readonly DispatcherTimer? _ageTickTimer;

    /// <summary>
    /// Dispatcher captured at construction so refresh-completed callbacks
    /// from the (background-thread) <see cref="AppRefreshService"/> can be
    /// marshaled onto the UI thread to mutate observable collections safely.
    /// </summary>
    private readonly Dispatcher? _uiDispatcher;

    [ObservableProperty] private string _title       = "TrayLight";
    [ObservableProperty] private string _logoSource  = string.Empty;
    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private Brush  _accentBrush = Brushes.DodgerBlue;
    [ObservableProperty] private string _footerText  = string.Empty;
    [ObservableProperty] private string _greeting    = string.Empty;
    [ObservableProperty] private DateTime? _lastSyncUtc;
    [ObservableProperty] private bool   _showLastSync = true;

    /// <summary>
    /// Controls the "Powered by headsinthecloud.blog" line in the popup footer.
    /// False only when the Branding\HideAttribution policy is explicitly enabled.
    /// The About dialog attribution is never affected by this.
    /// </summary>
    [ObservableProperty] private bool   _showAttribution = true;

    public ObservableCollection<InfoItemViewModel> InfoItems { get; } = new();
    public ObservableCollection<ShortcutViewModel> Shortcuts { get; } = new();
    public ObservableCollection<InfoTextLine>      InfoTextLines { get; } = new();

    public bool HasInfoText => InfoTextLines.Count > 0;

    public string PoweredBy        => Strings.PoweredBy;
    public string PoweredByLink    => PoweredByLinkText;
    public string PoweredByLinkUrl => PoweredByUrl;

    public string LastSyncDisplay
    {
        get
        {
            if (LastSyncUtc is null) return string.Empty;
            return Strings.Format("LastRefreshedFormat", FormatRelative(DateTime.UtcNow - LastSyncUtc.Value));
        }
    }

    /// <summary>
    /// Formats a positive elapsed time using compact relative-time rules:
    /// &lt;1m =&gt; "just now", &lt;60m =&gt; "X minutes ago",
    /// &lt;24h =&gt; "Xh Ym ago", otherwise "X days ago".
    /// Negative or absurdly large values collapse to "unknown".
    /// </summary>
    internal static string FormatRelative(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero || elapsed.TotalDays > 3650) return Strings.RelativeUnknown;
        if (elapsed.TotalMinutes < 1) return Strings.RelativeJustNow;
        if (elapsed.TotalMinutes < 60)
        {
            var m = (int)elapsed.TotalMinutes;
            return Strings.Format(m == 1 ? "RelativeMinuteAgoFormat" : "RelativeMinutesAgoFormat", m);
        }
        if (elapsed.TotalHours < 24)
        {
            return Strings.Format("RelativeHoursMinutesAgoFormat", elapsed.Hours, elapsed.Minutes);
        }
        var d = (int)elapsed.TotalDays;
        return Strings.Format(d == 1 ? "RelativeDayAgoFormat" : "RelativeDaysAgoFormat", d);
    }

    public TrayPopupViewModel(
        IConfigurationService configService,
        ISystemInfoService systemInfo,
        IActionExecutor actionExecutor,
        ILogoService logoService,
        AppRefreshService? appRefresh = null,
        ILogger<TrayPopupViewModel>? logger = null)
    {
        _configService   = configService;
        _systemInfo      = systemInfo;
        _actionExecutor  = actionExecutor;
        _logoService     = logoService;
        _appRefresh      = appRefresh;
        _logger          = logger;
        Greeting = Strings.Format("GreetingFormat", _systemInfo.UserName);

        ApplyConfig(_configService.Current);
        LogoSource = _logoService.ResolvedLogoPath;
        // Seed "Last refreshed" from the app-level refresh service so the
        // popup's footer matches actual provider freshness, not just the
        // moment this view-model happened to be constructed.
        LastSyncUtc = _appRefresh?.LastRefreshUtc ?? DateTime.UtcNow;
        _configService.PropertyChanged += OnConfigChanged;
        _logoService.PropertyChanged += OnLogoChanged;
        if (_appRefresh is not null)
            _appRefresh.RefreshCompleted += OnAppRefreshCompleted;

        // Tick the relative-time string every 30 seconds so an open popup
        // visibly progresses from "just now" -> "1 minute ago" -> ... .
        // NOTE: tile DATA refresh is owned by AppRefreshService (app-level,
        // survives popup close, sleep/wake, long uptime). This timer only
        // re-renders the existing LastSyncDisplay string.
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            _uiDispatcher = dispatcher;
            _ageTickTimer = new DispatcherTimer(
                TimeSpan.FromSeconds(30),
                DispatcherPriority.Background,
                (_, _) => OnPropertyChanged(nameof(LastSyncDisplay)),
                dispatcher);
            _ageTickTimer.Start();
        }
    }

    private void OnAppRefreshCompleted(object? sender, DateTime completedAtUtc)
    {
        // AppRefreshService fires on a background thread. Marshal to the UI
        // thread before touching observable collections.
        if (_uiDispatcher is null) { ApplyRefresh(completedAtUtc); return; }
        if (_uiDispatcher.CheckAccess()) ApplyRefresh(completedAtUtc);
        else _uiDispatcher.BeginInvoke(new Action(() => ApplyRefresh(completedAtUtc)));
    }

    private void ApplyRefresh(DateTime completedAtUtc)
    {
        try
        {
            RebuildInfoItems(_configService.Current);
            LastSyncUtc = completedAtUtc;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(LogEvents.InfoItemWarning,
                "TrayPopupViewModel: ApplyRefresh failed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Recomputes every visible info tile from live sources (WMI, registry,
    /// scheduled tasks, network APIs) and updates <see cref="LastSyncUtc"/>
    /// to the moment the refresh completes. Called on the
    /// <c>RefreshIntervalMinutes</c> cadence and whenever the popup becomes
    /// visible, so the "Last refreshed" footer reflects actual tile-data
    /// freshness rather than config-reload time.
    /// </summary>
    public void Refresh()
    {
        RebuildInfoItems(_configService.Current);
        LastSyncUtc = DateTime.UtcNow;
        _logger?.LogInformation(LogEvents.InfoItemUpdated,
            "Tile refresh cycle completed: {Count} tiles updated at {Timestamp:o}.",
            InfoItems.Count, LastSyncUtc);
    }

    private void OnLogoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ILogoService.ResolvedLogoPath))
            LogoSource = _logoService.ResolvedLogoPath;
    }

    partial void OnLastSyncUtcChanged(DateTime? value) => OnPropertyChanged(nameof(LastSyncDisplay));

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IConfigurationService.Current))
        {
            ApplyConfig(_configService.Current);
            // Config push counts as a refresh - tile data was just rebuilt.
            LastSyncUtc = DateTime.UtcNow;
        }
    }

    private void ApplyConfig(AppConfiguration config)
    {
        Title        = config.Branding.Title;
        CompanyName  = config.Branding.CompanyName;
        AccentBrush  = ParseAccent(config.Branding.AccentColor);
        FooterText   = config.Footer.Text;
        ShowLastSync = config.Footer.ShowLastSync;
        ShowAttribution = !config.Branding.HideAttribution;

        InfoTextLines.Clear();
        foreach (var line in InfoTextParser.Parse(config.Footer.InfoText))
            InfoTextLines.Add(line);
        OnPropertyChanged(nameof(HasInfoText));

        RebuildInfoItems(config);
        RebuildShortcuts(config);
    }

    private void RebuildInfoItems(AppConfiguration config)
    {
        InfoItems.Clear();

        // Take only enabled items with a valid grid slot, ordered by position.
        // Up to 8 tiles (4 rows × 2 columns), matching the ADMX Position range 0..7.
        var ordered = config.InfoItems
            .Where(i => i.Enabled && i.Position is >= 0 and <= 7)
            .OrderBy(i => i.Position)
            .Take(8);

        foreach (var item in ordered)
        {
            InfoItems.Add(BuildInfoItem(item));
        }
    }

    private InfoItemViewModel BuildInfoItem(InfoItemConfig cfg)
    {
        var behavior = _configService.Current.Behavior;
        var vm = new InfoItemViewModel
        {
            Type     = cfg.Type,
            Position = cfg.Position,
            IconGlyph = ParseGlyph(cfg.Icon) ?? DefaultGlyphFor(cfg.Type),
            // An empty or whitespace Title means "not configured": fall back to
            // the built-in default, which is localized to the display language.
            Title     = string.IsNullOrWhiteSpace(cfg.Title)
                ? DefaultTitleFor(cfg.Type)
                : cfg.Title
        };

        switch (cfg.Type)
        {
            case InfoItemType.ComputerName:
                vm.Value = _systemInfo.MachineName;
                vm.ValueTooltip = $"{_systemInfo.MachineName} (click to copy)";
                vm.ClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(
                    () => CopyToClipboard(_systemInfo.MachineName));
                break;

            case InfoItemType.OsVersion:
                var (osDisplay, osDetail) = GetOsVersionDisplay(_systemInfo.OsVersion);
                vm.Value = osDisplay;
                vm.ValueTooltip = $"{osDetail} (click to copy)";
                vm.ClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(
                    () => CopyToClipboard(osDetail));
                // OS version is informational - never a warning.
                break;

            case InfoItemType.LastReboot:
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                var bootTime = DateTime.Now - uptime;
                vm.Value = FormatUptime(uptime);
                vm.ValueTooltip = bootTime.ToString("dd.MM.yyyy HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture);
                // Behavior\RebootWarningDays: 0 disables the warning entirely.
                var rebootLimit = behavior.RebootWarningDays;
                vm.HasWarning = rebootLimit > 0 && uptime.TotalDays > rebootLimit;
                break;

            case InfoItemType.StorageUsed:
                var (sizeText, percent) = GetSystemDriveUsage();
                vm.Value = Strings.Format("StoragePercentUsedFormat", percent);
                vm.ValueTooltip = sizeText;
                vm.HasWarning = cfg.StorageLimit is int sl && percent >= sl;
                vm.WarningSeverity = percent >= 95 ? "danger" : "warning";
                break;

            case InfoItemType.NetworkInfo:
                var net = GetNetworkSummary();
                vm.Value = net.Name;
                // IPv4 goes on its own auto-shrinking second line so a full
                // 15-char address (255.255.255.255) is never truncated.
                vm.ValueSecondLine = net.IPv4;
                vm.ValueTooltip = net.Tooltip;
                // Network state is informational - offline is shown but never warns.
                break;

            case InfoItemType.EntraIdStatus:
                vm.Value = Strings.StatusDetecting;
                vm.ValueTooltip = string.Empty;
                _ = LoadEntraIdAsync(vm);
                break;

            case InfoItemType.SerialNumber:
                var serial = ReadSerialNumber();
                vm.Value = serial;
                vm.ValueTooltip = $"{serial} (click to copy)";
                vm.ClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(
                    () => CopyToClipboard(serial));
                break;

            case InfoItemType.IntuneSync:
                LoadIntuneSync(vm, _logger);
                break;

            default:
                vm.Value = "\u2014";
                break;
        }

        return vm;
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 2)  return Strings.Format("UptimeDaysHoursAgoFormat", (int)uptime.TotalDays, uptime.Hours);
        if (uptime.TotalDays >= 1)  return Strings.Format("UptimeOneDayHoursAgoFormat", uptime.Hours);
        if (uptime.TotalHours >= 1) return Strings.Format("UptimeHoursMinutesAgoFormat", uptime.Hours, uptime.Minutes);
        if (uptime.TotalMinutes >= 1) return Strings.Format("UptimeMinutesAgoFormat", (int)uptime.TotalMinutes);
        return Strings.RelativeJustNow;
    }

    private static void CopyToClipboard(string text)
    {
        try { System.Windows.Clipboard.SetText(text); }
        catch { /* clipboard transient failure - ignore */ }
    }

    private static string ReadSerialNumber()
    {
        // Try BIOS, then chassis, then motherboard. Hyper-V/VMware leave
        // BIOS.SerialNumber empty (or "None"), but the chassis or board
        // entries usually still carry a usable identifier.
        foreach (var (cls, prop) in new[]
        {
            ("Win32_BIOS",            "SerialNumber"),
            ("Win32_SystemEnclosure", "SerialNumber"),
            ("Win32_BaseBoard",       "SerialNumber"),
        })
        {
            var s = QueryWmiString(cls, prop);
            if (IsValidSerial(s)) return s!.Trim();
        }

        // No physical serial - distinguish VM ("N/A") from a true read failure.
        var model = QueryWmiString("Win32_ComputerSystem", "Model") ?? string.Empty;
        if (model.Contains("Virtual",  StringComparison.OrdinalIgnoreCase) ||
            model.Contains("VMware",   StringComparison.OrdinalIgnoreCase) ||
            model.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("KVM",      StringComparison.OrdinalIgnoreCase) ||
            model.Contains("Xen",      StringComparison.OrdinalIgnoreCase))
        {
            return Strings.StatusVirtualMachineSerial;
        }
        return Strings.StatusUnavailable;
    }

    private static bool IsValidSerial(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        return !t.Equals("None",                   StringComparison.OrdinalIgnoreCase) &&
               !t.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) &&
               !t.Equals("Default string",         StringComparison.OrdinalIgnoreCase) &&
               !t.Equals("System Serial Number",   StringComparison.OrdinalIgnoreCase) &&
               !t.Equals("0",                      StringComparison.OrdinalIgnoreCase);
    }

    private static string? QueryWmiString(string wmiClass, string property)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT {property} FROM {wmiClass}");
            foreach (var mo in searcher.Get())
            {
                using (mo)
                {
                    var val = mo[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }
        }
        catch { /* WMI unavailable - fall through */ }
        return null;
    }

    private static void LoadIntuneSync(InfoItemViewModel vm, ILogger? logger)
    {
        var enrollments = CollectIntuneEnrollments(logger);
        var enrolled = enrollments.Count > 0;
        var (lastSync, source) = ReadIntuneLastSync(enrollments, logger);

        logger?.LogInformation(LogEvents.InfoItemUpdated,
            "IntuneSync DECISION: enrolled={Enrolled}, finalTime={LastSync}, finalSource={Source}.",
            enrolled, lastSync?.ToString("o") ?? "(null)",
            string.IsNullOrEmpty(source) ? "(none - tile shows Unknown)" : source);

        if (!enrolled && lastSync is null)
        {
            vm.Value = Strings.StatusNotEnrolled;
            vm.ValueTooltip = "Device is not Intune managed.";
            vm.HasWarning = false;
            // No sync to trigger - make the tile non-interactive.
            vm.IsClickable = false;
            vm.ClickCommand = null;
            return;
        }

        if (lastSync is null)
        {
            // Enrolled but no readable timestamp (no PushLaunch task yet,
            // no IME logs, etc.). Don't lie with "just now".
            vm.Value = Strings.StatusUnknown;
            vm.ValueTooltip = "Device is Intune managed but the last sync time "
                + "could not be determined.\nClick to sync now.";
            vm.HasWarning = false;
            vm.IsClickable = true;
            vm.ClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(
                () => TriggerIntuneSync(vm, logger));
            return;
        }

        // Task Scheduler returns LastRunTime in local time (Kind=Unspecified).
        // Compare in local time to avoid a 1- to 2-hour offset that would
        // otherwise appear as "in the future" or "hours ago" on every popup.
        var age = DateTime.Now - lastSync.Value;
        vm.Value = FormatRelative(age);
        vm.ValueTooltip = $"Last Intune sync: {lastSync:dd.MM.yyyy HH:mm} ({source})\nClick to sync now.";
        // Stale Intune sync is informational only - not a warning.
        vm.HasWarning = false;
        vm.IsClickable = true;
        vm.ClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => TriggerIntuneSync(vm, logger));
    }

    /// <summary>
    /// Reads the current Intune enrollment + last-sync summary for reuse by the
    /// tray hover tooltip. Mirrors the Intune tile logic; the returned time is
    /// in local time (or <c>null</c> when unknown / not enrolled).
    /// </summary>
    public static (bool Enrolled, DateTime? LastSyncLocal) GetIntuneSummary(ILogger? logger = null)
    {
        var enrollments = CollectIntuneEnrollments(logger);
        var enrolled = enrollments.Count > 0;
        var (lastSync, _) = ReadIntuneLastSync(enrollments, logger);
        return (enrolled, lastSync);
    }

    /// </summary>
    private static bool IsIntuneEnrolled() => CollectIntuneEnrollments(null).Count > 0;

    /// <summary>
    /// Returns the list of MDM enrollment GUIDs whose ProviderID identifies
    /// them as Intune ("MS DM Server"). Logs every enrollment subkey it sees.
    /// </summary>
    private static List<string> CollectIntuneEnrollments(ILogger? logger)
    {
        var result = new List<string>();
        try
        {
            using var enrollments = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
            if (enrollments is null)
            {
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync enrollment scan: HKLM\\SOFTWARE\\Microsoft\\Enrollments key not present.");
                return result;
            }
            foreach (var name in enrollments.GetSubKeyNames())
            {
                using var sub = enrollments.OpenSubKey(name);
                var providerId = sub?.GetValue("ProviderID") as string ?? "(null)";
                var upn = sub?.GetValue("UPN") as string ?? "(null)";
                var isIntune = providerId.Contains("MS DM Server", StringComparison.OrdinalIgnoreCase);
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync enrollment scan: GUID={Guid}, ProviderID={ProviderId}, UPN={Upn}, isIntune={IsIntune}.",
                    name, providerId, upn, isIntune);
                if (isIntune) result.Add(name);
            }
        }
        catch (Exception ex)
        {
            logger?.LogInformation(LogEvents.InfoItemUpdated,
                "IntuneSync enrollment scan failed: {Error}.", ex.Message);
        }
        return result;
    }

    private static (DateTime? Time, string Source) ReadIntuneLastSync(
        List<string> enrollmentGuids, ILogger? logger = null)
    {
        var candidates = new List<(DateTime Time, string Source)>();

        // ---- Source 0 (PRIMARY): OMA-DM ServerLastSuccessTime ---------------
        // HKLM\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\{GUID}\Protected\
        //   ConnInfo\ServerLastSuccessTime  (REG_SZ, format yyyyMMddTHHmmssZ).
        // This is the same value Windows Settings > Accounts > Access work or
        // school > Info displays as "Last sync was successful". Despite the
        // trailing 'Z', empirically the time is stored in *local* time on
        // current Windows builds (verified against MDM event log entries with
        // matching TimeCreated). We therefore treat it as local.
        // The Protected subkey is normally readable by the local Users group;
        // if not, we silently fall through to other sources.
        foreach (var guid in enrollmentGuids)
        {
            try
            {
                using var conn = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\{guid}\Protected\ConnInfo");
                if (conn is null)
                {
                    logger?.LogInformation(LogEvents.InfoItemUpdated,
                        "IntuneSync source 0: OMADM\\Accounts\\{Guid}\\Protected\\ConnInfo not present.", guid);
                    continue;
                }
                var raw = conn.GetValue("ServerLastSuccessTime") as string;
                if (string.IsNullOrEmpty(raw))
                {
                    logger?.LogInformation(LogEvents.InfoItemUpdated,
                        "IntuneSync source 0: OMADM[{Guid}] ServerLastSuccessTime missing or empty.", guid);
                    continue;
                }
                // Despite the trailing 'Z', the value is stored in *local*
                // wall time (verified against MDM event log entries with
                // matching TimeCreated). DateTime.TryParseExact treats a
                // trailing 'Z' in the input as a UTC designator regardless of
                // the format string, so we strip it and parse the bare
                // yyyyMMddTHHmmss form, then pin the result to Local.
                var bare = raw!.EndsWith("Z", StringComparison.Ordinal) ? raw[..^1] : raw;
                if (DateTime.TryParseExact(
                        bare, "yyyyMMddTHHmmss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var parsed))
                {
                    parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
                    logger?.LogInformation(LogEvents.InfoItemUpdated,
                        "IntuneSync source 0: OMADM[{Guid}] ServerLastSuccessTime raw={Raw} parsed(local)={Parsed:o}.",
                        guid, raw, parsed);
                    candidates.Add((parsed, $"OMADM ServerLastSuccessTime [{guid}]"));
                }
                else
                {
                    logger?.LogInformation(LogEvents.InfoItemUpdated,
                        "IntuneSync source 0: OMADM[{Guid}] ServerLastSuccessTime '{Raw}' did not match yyyyMMddTHHmmss[Z].",
                        guid, raw);
                }
            }
            catch (Exception ex)
            {
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync source 0: OMADM[{Guid}] read failed: {Error}.", guid, ex.Message);
            }
        }

        // If source 0 produced a candidate we can return early — it is the
        // ground truth and matches what Windows Settings displays. The
        // remaining sources stay in place purely for diagnostic logging.
        if (candidates.Count > 0)
        {
            var best0 = candidates.OrderByDescending(c => c.Time).First();
            logger?.LogInformation(LogEvents.InfoItemUpdated,
                "IntuneSync DECISION (early): chose {Time:o} from {Source} (source 0 PRIMARY).",
                best0.Time, best0.Source);
            return (best0.Time, best0.Source);
        }

        // ---- Source A: Scheduled task "Schedule #3 created by {GUID}" -----
        // (root folder, used by IME for the IntuneManagementExtension cycle)
        // ---- Source B: "PushLaunch" under \Microsoft\Windows\EnterpriseMgmt\{GUID}
        try
        {
            var t = Type.GetTypeFromProgID("Schedule.Service");
            if (t is null)
            {
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync source A/B: Schedule.Service COM not available.");
            }
            else
            {
                dynamic service = Activator.CreateInstance(t)!;
                try
                {
                    service.Connect();

                    foreach (var guid in enrollmentGuids)
                    {
                        // Source A: \Schedule #3 created by {GUID}
                        var taskNameA = $"Schedule #3 created by {guid}";
                        try
                        {
                            dynamic root = service.GetFolder(@"\");
                            dynamic taskA = root.GetTask(taskNameA);
                            DateTime lastA = taskA.LastRunTime;
                            logger?.LogInformation(LogEvents.InfoItemUpdated,
                                "IntuneSync source A: \\{TaskName} LastRunTime={LastRun:o}.",
                                taskNameA, lastA);
                            if (lastA.Year > 1900) candidates.Add((lastA, $"Schedule #3 [{guid}]"));
                        }
                        catch (Exception ex)
                        {
                            logger?.LogInformation(LogEvents.InfoItemUpdated,
                                "IntuneSync source A: \\{TaskName} not readable: {Error}.",
                                taskNameA, ex.Message);
                        }

                        // Source B: enumerate EVERY task under
                        // \Microsoft\Windows\EnterpriseMgmt\{GUID}.
                        // The MDM sync activity that Windows Settings reports
                        // can be driven by any of several tasks here
                        // (PushLaunch, Schedule #1, Schedule #2, Schedule #3,
                        // Login script, etc.) depending on what triggered the
                        // last sync (manual, polling, push, login). Use the
                        // newest LastRunTime across all of them.
                        try
                        {
                            dynamic folder = service.GetFolder(
                                $@"\Microsoft\Windows\EnterpriseMgmt\{guid}");
                            dynamic tasks = folder.GetTasks(0); // 0 = exclude hidden
                            int taskCount = tasks.Count;
                            logger?.LogInformation(LogEvents.InfoItemUpdated,
                                "IntuneSync source B: EnterpriseMgmt\\{Guid} has {Count} task(s).",
                                guid, taskCount);
                            // Schedule.Service collections are 1-indexed.
                            for (int i = 1; i <= taskCount; i++)
                            {
                                dynamic taskB = tasks.Item[i];
                                string taskName = taskB.Name;
                                try
                                {
                                    DateTime lastB = taskB.LastRunTime;
                                    int lastResult = 0;
                                    try { lastResult = taskB.LastTaskResult; } catch { /* ignore */ }
                                    logger?.LogInformation(LogEvents.InfoItemUpdated,
                                        "IntuneSync source B: EnterpriseMgmt\\{Guid}\\{TaskName} LastRunTime={LastRun:o} LastTaskResult=0x{Result:X}.",
                                        guid, taskName, lastB, lastResult);
                                    if (lastB.Year > 1900) candidates.Add((lastB, $"EnterpriseMgmt\\{guid}\\{taskName}"));
                                }
                                catch (Exception innerEx)
                                {
                                    logger?.LogInformation(LogEvents.InfoItemUpdated,
                                        "IntuneSync source B: EnterpriseMgmt\\{Guid}\\{TaskName} LastRunTime not readable: {Error}.",
                                        guid, taskName, innerEx.Message);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogInformation(LogEvents.InfoItemUpdated,
                                "IntuneSync source B: EnterpriseMgmt\\{Guid} folder not readable: {Error}.",
                                guid, ex.Message);
                        }
                    }
                }
                finally
                {
                    if (Marshal.IsComObject(service)) Marshal.FinalReleaseComObject(service);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogInformation(LogEvents.InfoItemUpdated,
                "IntuneSync source A/B failed: {Error}.", ex.Message);
        }

        // ---- Source C: HKLM\SOFTWARE\Microsoft\IntuneManagementExtension ----
        // Log every value whose name hints at a timestamp.
        try
        {
            using var ime = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\IntuneManagementExtension");
            if (ime is null)
            {
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync source C: HKLM\\SOFTWARE\\Microsoft\\IntuneManagementExtension not present.");
            }
            else
            {
                LogTimestampValuesRecursive(ime, @"HKLM:\SOFTWARE\Microsoft\IntuneManagementExtension", logger, depth: 0);
            }
        }
        catch (Exception ex)
        {
            logger?.LogInformation(LogEvents.InfoItemUpdated,
                "IntuneSync source C failed: {Error}.", ex.Message);
        }

        // ---- Source D: HKLM\SOFTWARE\Microsoft\Enrollments\{GUID} ----
        try
        {
            foreach (var guid in enrollmentGuids)
            {
                using var sub = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey($@"SOFTWARE\Microsoft\Enrollments\{guid}");
                if (sub is null)
                {
                    logger?.LogInformation(LogEvents.InfoItemUpdated,
                        "IntuneSync source D: Enrollments\\{Guid} key vanished.", guid);
                    continue;
                }
                LogTimestampValuesRecursive(sub, $@"HKLM:\SOFTWARE\Microsoft\Enrollments\{guid}", logger, depth: 0);
            }
        }
        catch (Exception ex)
        {
            logger?.LogInformation(LogEvents.InfoItemUpdated,
                "IntuneSync source D failed: {Error}.", ex.Message);
        }

        // ---- Source D2: HKLM\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\{GUID} ----
        // The OMA-DM client persists per-session state (incl. last successful
        // session timestamp) here. Worth scanning even though the layout has
        // changed across Windows builds.
        try
        {
            using var accounts = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Provisioning\OMADM\Accounts");
            if (accounts is null)
            {
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync source D2: Provisioning\\OMADM\\Accounts not present.");
            }
            else
            {
                foreach (var name in accounts.GetSubKeyNames())
                {
                    using var sub = accounts.OpenSubKey(name);
                    if (sub is null) continue;
                    LogTimestampValuesRecursive(sub,
                        $@"HKLM:\SOFTWARE\Microsoft\Provisioning\OMADM\Accounts\{name}", logger, depth: 0);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogInformation(LogEvents.InfoItemUpdated,
                "IntuneSync source D2 failed: {Error}.", ex.Message);
        }

        // ---- Source E: DeviceManagement-Enterprise-Diagnostics-Provider/Admin -----
        // Event ID 209 = MDM session completed (the canonical "sync done"
        // marker that Windows Settings shows). 208 = sync started; 72/75 are
        // older session events. Newest of any of these approximates last sync.
        try
        {
            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(
                new System.Diagnostics.Eventing.Reader.EventLogQuery(
                    "Microsoft-Windows-DeviceManagement-Enterprise-Diagnostics-Provider/Admin",
                    System.Diagnostics.Eventing.Reader.PathType.LogName,
                    "*[System[(EventID=209 or EventID=208 or EventID=72 or EventID=75)]]"));

            System.Diagnostics.Eventing.Reader.EventRecord? newest = null;
            for (var ev = reader.ReadEvent(); ev is not null; ev = reader.ReadEvent())
            {
                if (newest is null || (ev.TimeCreated is { } t1 && newest.TimeCreated is { } t2 && t1 > t2))
                {
                    newest?.Dispose();
                    newest = ev;
                }
                else
                {
                    ev.Dispose();
                }
            }
            if (newest?.TimeCreated is { } when)
            {
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync source E (Event Log): id={EventId} at {When:o}.", newest.Id, when);
                candidates.Add((when, $"MDM event log #{newest.Id}"));
                newest.Dispose();
            }
            else
            {
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync source E (Event Log): no matching events (209/208/72/75) found.");
            }
        }
        catch (Exception ex)
        {
            logger?.LogInformation(LogEvents.InfoItemUpdated,
                "IntuneSync source E failed: {Error}.", ex.Message);
        }

        if (candidates.Count == 0)
        {
            logger?.LogInformation(LogEvents.InfoItemUpdated,
                "IntuneSync: no candidate timestamps from any source.");
            return (null, string.Empty);
        }

        var winner = candidates.OrderByDescending(c => c.Time).First();
        logger?.LogInformation(LogEvents.InfoItemUpdated,
            "IntuneSync: {Count} candidates, picked newest {Time:o} from {Source}.",
            candidates.Count, winner.Time, winner.Source);
        return (winner.Time, winner.Source);
    }

    /// <summary>
    /// Recursively logs registry values whose name suggests a timestamp
    /// (Time/Date/Sync/Run/Last/Stamp). Limits recursion to avoid log spam.
    /// </summary>
    private static void LogTimestampValuesRecursive(
        Microsoft.Win32.RegistryKey key, string path, ILogger? logger, int depth)
    {
        if (depth > 3) return;
        try
        {
            foreach (var valueName in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(valueName)) continue;
                if (valueName.IndexOf("Time", StringComparison.OrdinalIgnoreCase) < 0 &&
                    valueName.IndexOf("Date", StringComparison.OrdinalIgnoreCase) < 0 &&
                    valueName.IndexOf("Sync", StringComparison.OrdinalIgnoreCase) < 0 &&
                    valueName.IndexOf("Run", StringComparison.OrdinalIgnoreCase) < 0 &&
                    valueName.IndexOf("Last", StringComparison.OrdinalIgnoreCase) < 0 &&
                    valueName.IndexOf("Stamp", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                var raw = key.GetValue(valueName);
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync registry: {Path}\\{Value} = {Raw} ({Kind}).",
                    path, valueName, raw ?? "(null)", key.GetValueKind(valueName));
            }
            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                if (sub is not null)
                    LogTimestampValuesRecursive(sub, path + "\\" + subName, logger, depth + 1);
            }
        }
        catch (Exception ex)
        {
            logger?.LogInformation(LogEvents.InfoItemUpdated,
                "IntuneSync registry scan {Path} failed: {Error}.", path, ex.Message);
        }
    }

    private static void TriggerIntuneSync(InfoItemViewModel vm, ILogger? logger)
    {
        vm.Value = Strings.StatusSyncing;
        // Snapshot the current timestamp so the poll loop can detect the moment
        // the real MDM check-in updates ServerLastSuccessTime.
        var baseline = SafeReadIntuneLastSync(logger);
        _ = RunIntuneSyncAsync(vm, logger, baseline);
    }

    /// <summary>
    /// Fires a real OMA-DM/MDM check-in (the same action as the Windows
    /// Settings "Sync" button) via <c>Windows.Management.MdmSessionManager</c>,
    /// plus the Intune Management Extension sync as a secondary action, then
    /// polls <c>ServerLastSuccessTime</c> until it advances (or a 2-minute
    /// timeout elapses) and refreshes the tile.
    /// </summary>
    private static async Task RunIntuneSyncAsync(
        InfoItemViewModel vm, ILogger? logger, DateTime? baseline)
    {
        var triggered = false;

        // ---- PRIMARY: real MDM/OMA-DM check-in via WinRT --------------------
        // This is what actually advances ServerLastSuccessTime (the value the
        // tile displays). intunemanagementextension://syncapp only triggers the
        // IME (Win32 apps/scripts) and leaves that timestamp untouched.
        try
        {
            var session = Windows.Management.MdmSessionManager.TryCreateSession();
            if (session is not null)
            {
                await session.StartAsync().AsTask().ConfigureAwait(false);
                triggered = true;
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync: MdmSessionManager session started (real OMA-DM check-in).");
            }
            else
            {
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync: MdmSessionManager.TryCreateSession returned null (no MDM enrollment?).");
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "IntuneSync: MdmSessionManager sync failed; falling back to IME sync only.");
        }

        // ---- SECONDARY: Intune Management Extension sync --------------------
        // Still useful - kicks the IME to re-evaluate Win32 apps/scripts.
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "intunemanagementextension://syncapp")
            { UseShellExecute = true });
            triggered = true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "IntuneSync: IME syncapp launch failed.");
        }

        if (!triggered)
        {
            // Neither trigger worked - restore the tile to its real state.
            UiInvoke(() => LoadIntuneSync(vm, logger));
            return;
        }

        // ---- Poll for the timestamp to advance ------------------------------
        // Show "Syncing…" for a few seconds, then re-read ServerLastSuccessTime
        // every 10 seconds for up to 2 minutes; refresh the tile as soon as the
        // timestamp changes (or on timeout so it never sticks on "Syncing…").
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        while (DateTime.UtcNow < deadline)
        {
            var latest = SafeReadIntuneLastSync(logger);
            if (latest is { } t && (baseline is null || t > baseline))
            {
                logger?.LogInformation(LogEvents.InfoItemUpdated,
                    "IntuneSync: ServerLastSuccessTime advanced to {Time:o}; refreshing tile.", t);
                UiInvoke(() => LoadIntuneSync(vm, logger));
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        logger?.LogInformation(LogEvents.InfoItemUpdated,
            "IntuneSync: timestamp did not change within 2 minutes; refreshing tile with current value.");
        UiInvoke(() => LoadIntuneSync(vm, logger));
    }

    /// <summary>Reads the current Intune last-sync time, swallowing any error.</summary>
    private static DateTime? SafeReadIntuneLastSync(ILogger? logger)
    {
        try { return ReadIntuneLastSync(CollectIntuneEnrollments(logger), logger).Time; }
        catch { return null; }
    }

    /// <summary>Runs <paramref name="action"/> on the WPF UI thread.</summary>
    private static void UiInvoke(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    private static (string Display, string Detail) GetOsVersionDisplay(string fallback)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null) return (StripMicrosoft(fallback), fallback);

            var product       = key.GetValue("ProductName")    as string ?? "Windows";
            var displayVersion = key.GetValue("DisplayVersion") as string ?? string.Empty;
            var build         = key.GetValue("CurrentBuild")   as string ?? string.Empty;
            var ubr           = key.GetValue("UBR");
            var edition       = key.GetValue("EditionID")      as string ?? string.Empty;

            // ProductName on Win11 still says "Windows 10"; correct it from build #.
            var versionNumber = int.TryParse(build, out var b) && b >= 22000 ? "11" : "10";
            if (b >= 22000 && product.Contains("Windows 10"))
                product = product.Replace("Windows 10", "Windows 11");

            // Compact display: "Win 11 Pro 26H1".
            var display = $"Win {versionNumber} {EditionShort(edition)} {displayVersion}".Trim();
            display = System.Text.RegularExpressions.Regex.Replace(display, @"\s+", " ");
            var detail  = ubr is null ? $"Build {build}" : $"Build {build}.{ubr}";
            return (display, $"{StripMicrosoft(product)} {edition} {displayVersion} ({detail})".Trim());
        }
        catch
        {
            return (StripMicrosoft(fallback), fallback);
        }
    }

    private static string EditionShort(string edition) => edition switch
    {
        "Professional"   => "Pro",
        "ProfessionalN"  => "Pro N",
        "Enterprise"     => "Ent",
        "EnterpriseN"    => "Ent N",
        "Education"      => "Edu",
        "Core"           => "Home",
        "CoreN"          => "Home N",
        _                 => edition
    };

    private static string StripMicrosoft(string s) =>
        s.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) ? s["Microsoft ".Length..] : s;

    private static (string Name, string IPv4, string Tooltip, bool Online) GetNetworkSummary()
    {
        try
        {
            // Excludes virtual adapters (Hyper-V, VMware, …) and APIPA
            // addresses, preferring the physical adapter that owns the default
            // gateway - see NetworkAdapterSelector.
            var selection = TrayLight.Services.Providers.NetworkAdapterSelector.SelectBest(
                TrayLight.Services.Providers.NetworkAdapterSelector.EnumerateLiveAdapters());

            if (selection is null)
                return (Strings.StatusOffline, string.Empty, "No active network connection.", false);

            var ipv4 = selection.IPv4;
            var name = selection.Adapter.Type ==
                       System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211
                ? selection.Adapter.Name
                : Strings.NetworkEthernet;

            // The tooltip keeps the full IPv4 as a fallback in case the tile
            // ever clips the value on very narrow layouts.
            return (name, ipv4, $"{selection.Adapter.Description}\nIPv4: {ipv4}", true);
        }
        catch
        {
            return (Strings.StatusUnavailable, string.Empty, string.Empty, false);
        }
    }

    private static async Task LoadEntraIdAsync(InfoItemViewModel vm)
    {
        try
        {
            // Read join state from the registry directly — no dsregcmd process.
            var status = await Task.Run(
                TrayLight.Services.Providers.EntraIdStatusProvider.ReadFromRegistry)
                .ConfigureAwait(true);

            vm.Value = status.StateDisplay;
            vm.ValueTooltip = string.IsNullOrEmpty(status.TenantName)
                ? status.StateDisplay
                : $"{status.StateDisplay}\nTenant: {status.TenantName}";
            // Identity tile is informational - never a warning.
        }
        catch
        {
            vm.Value = Strings.StatusUnavailable;
        }
    }

    private void RebuildShortcuts(AppConfiguration config)
    {
        Shortcuts.Clear();

        var ordered = config.Shortcuts
            .Where(s => s.Position >= 0 && !string.IsNullOrWhiteSpace(s.Title))
            .Where(s => _actionExecutor.IsVisible(s))
            .OrderBy(s => s.Position)
            .Take(6)
            .ToList();

        // Provide a sensible default when the customer hasn't configured
        // any shortcuts via ADMX, so the Quick Actions row is never empty.
        if (ordered.Count == 0)
        {
            ordered.Add(new ShortcutConfig
            {
                Title      = Strings.DefaultShortcutTitle,
                Subtitle   = Strings.DefaultShortcutSubtitle,
                ActionType = ShortcutActionType.Url,
                Action     = "https://headsinthecloud.blog",
                Icon       = "Segoe Fluent Icons:E897",
                Position   = 0
            });
        }

        foreach (var s in ordered)
        {
            var vm = new ShortcutViewModel(s, _actionExecutor)
            {
                IconGlyph = ParseGlyph(s.Icon) ?? "\uE897"
            };
            Shortcuts.Add(vm);
        }
    }

    /// <summary>
    /// Parses an icon descriptor of the form "Segoe Fluent Icons:&amp;#xE946;"
    /// or "Segoe Fluent Icons:E946" into a single Unicode character. Returns
    /// null when the input cannot be interpreted.
    /// </summary>
    private static string? ParseGlyph(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var idx = raw.IndexOf(':');
        var token = (idx >= 0 ? raw[(idx + 1)..] : raw).Trim();

        // Strip XML char-ref wrapping: "&#xE946;" -> "E946".
        if (token.StartsWith("&#x", StringComparison.OrdinalIgnoreCase) && token.EndsWith(";"))
        {
            token = token[3..^1];
        }
        if (token.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }

        return int.TryParse(token, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var code)
            ? char.ConvertFromUtf32(code)
            : null;
    }

    private static string DefaultGlyphFor(InfoItemType type) => type switch
    {
        InfoItemType.ComputerName     => "\uE977", // Devices
        InfoItemType.OsVersion        => "\uE770", // Windows logo-ish
        InfoItemType.LastReboot       => "\uE823", // Clock
        InfoItemType.StorageUsed      => "\uEDA2", // Drive
        InfoItemType.NetworkInfo      => "\uE968", // Network
        InfoItemType.EntraIdStatus    => "\uE77B", // Contact
        InfoItemType.SerialNumber     => "\uE7BF", // Tag
        InfoItemType.IntuneSync       => "\uE895", // Sync
        _                             => "\uE946", // Info
    };

    private static string DefaultTitleFor(InfoItemType type) => type switch
    {
        InfoItemType.ComputerName     => Strings.TileComputerName,
        InfoItemType.OsVersion        => Strings.TileOsVersion,
        InfoItemType.LastReboot       => Strings.TileLastReboot,
        InfoItemType.StorageUsed      => Strings.TileStorage,
        InfoItemType.NetworkInfo      => Strings.TileNetwork,
        InfoItemType.EntraIdStatus    => Strings.TileEntraId,
        InfoItemType.SerialNumber     => Strings.TileSerialNumber,
        InfoItemType.IntuneSync       => Strings.TileIntuneSync,
        _                             => Strings.TileInfo,
    };

    private static Brush ParseAccent(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.DodgerBlue;
        }
    }

    private static (string Display, int Percent) GetSystemDriveUsage()
    {
        try
        {
            var sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(sysRoot);
            if (!drive.IsReady) return ("—", 0);
            var used = drive.TotalSize - drive.AvailableFreeSpace;
            var percent = drive.TotalSize == 0 ? 0 : (int)(100.0 * used / drive.TotalSize);
            var usedGb = used / 1024.0 / 1024.0 / 1024.0;
            var totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
            return ($"{usedGb:0.#} / {totalGb:0.#} GB ({percent}%)", percent);
        }
        catch
        {
            return ("—", 0);
        }
    }

    public void Dispose()
    {
        _ageTickTimer?.Stop();
        _configService.PropertyChanged -= OnConfigChanged;
        _logoService.PropertyChanged -= OnLogoChanged;
        if (_appRefresh is not null)
            _appRefresh.RefreshCompleted -= OnAppRefreshCompleted;
    }
}
