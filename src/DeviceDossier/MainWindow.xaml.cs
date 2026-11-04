using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using DeviceDossier.Core;
using DeviceDossier.Export;
using DeviceDossier.Util;
using DeviceDossier.Views;

namespace DeviceDossier
{
    public partial class MainWindow : Window
    {
        private const int WmDeviceChange = 0x0219;

        private readonly ObservableCollection<StepItem> _steps = new ObservableCollection<StepItem>();
        private readonly ObservableCollection<LogLine> _log = new ObservableCollection<LogLine>();
        private readonly DispatcherTimer _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _usbDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        private readonly bool _isAdmin;
        private AuditSettings _settings;
        private List<UsbDrive> _usbDrives = new List<UsbDrive>();
        private CancellationTokenSource _cts;
        private DateTime _started;
        private AuditResult _result;
        private bool _running;
        private bool _applying;
        private string _page = "home";

        public static readonly DependencyProperty SpecColumnsProperty =
            DependencyProperty.Register("SpecColumns", typeof(int), typeof(MainWindow), new PropertyMetadata(4));

        public int SpecColumns
        {
            get { return (int)GetValue(SpecColumnsProperty); }
            set { SetValue(SpecColumnsProperty, value); }
        }

        public MainWindow() : this(null)
        {
        }

        public MainWindow(AuditSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? AuditSettings.Load();
            _isAdmin = Elevation.IsAdministrator();

            StepsList.ItemsSource = _steps;
            LogList.ItemsSource = _log;
            _clock.Tick += (s, e) => ElapsedText.Text = "Elapsed " + FormatSpan(DateTime.Now - _started);
            _usbDebounce.Tick += async (s, e) =>
            {
                _usbDebounce.Stop();
                await RefreshUsbAsync();
            };

            string version = AppInfo.Version;
            VersionText.Text = "Version " + version;
            UserText.Text = Environment.UserDomainName + "\\" + Environment.UserName;
            AdminText.Text = _isAdmin ? "Administrator" : "Limited mode";
            AdminDot.SetResourceReference(Shape.FillProperty, _isAdmin ? "Success" : "Warning");
            LimitedBanner.Visibility = _isAdmin ? Visibility.Collapsed : Visibility.Visible;
            AboutText.Text = "DeviceDossier " + version + " - open-source software released under the MIT License. DeviceDossier only reads information; it never changes settings on the computer being audited.";
            CliText.Text =
                "DeviceDossier.exe --silent [--output \"D:\\Audits\"] [--formats xlsx,html,xml]\n" +
                "                  [--settings \"\\\\server\\share\\DeviceDossier.settings.xml\"]\n" +
                "                  [--skip drives,security,patches,software,extensions]\n" +
                "                  [--asset-tag IT-0042] [--assigned-to \"Finance\"] [--location \"HQ\"]\n" +
                "                  [--log \"C:\\Logs\\devicedossier.log\"]\n\n" +
                "Exit codes: 0 = reports saved, 1 = audit failed, 2 = invalid arguments.  DeviceDossier.exe --help lists every option.";

            ApplyOptions(_settings);
            FillSettingsForm(_settings);
            HomePage.SizeChanged += (s, e) => SpecColumns = e.NewSize.Width >= 1180 ? 4 : e.NewSize.Width >= 860 ? 3 : 2;
            ShowPage("home");

            SourceInitialized += (s, e) =>
            {
                ThemeManager.ApplyTitleBar(this);
                var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (source != null) source.AddHook(WndProc);
            };
            Loaded += async (s, e) =>
            {
                await RefreshUsbAsync();
                await LoadSnapshotAsync();
            };
            Closing += MainWindow_Closing;
            ThemeManager.Changed += OnThemeChanged;
            Closed += (s, e) => ThemeManager.Changed -= OnThemeChanged;
            UpdateThemeUi();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmDeviceChange)
            {
                _usbDebounce.Stop();
                _usbDebounce.Start();
            }
            return IntPtr.Zero;
        }

        private async Task LoadSnapshotAsync()
        {
            try
            {
                var snap = await Task.Run(() => SystemSnapshot.Load());
                SpecCards.ItemsSource = new List<SpecCard>
                {
                    new SpecCard("PC", "DEVICE", snap.DeviceName, snap.Model),
                    new SpecCard("USR", "SIGNED-IN USER", snap.ProfileName, snap.CurrentUser + (snap.IsAdmin ? " · admin" : "")),
                    new SpecCard("OS", "WINDOWS", snap.Os, snap.OsDetail),
                    new SpecCard("CPU", "PROCESSOR", snap.Cpu, snap.CpuDetail),
                    new SpecCard("GPU", "GRAPHICS", snap.Gpus.FirstOrDefault(), snap.Gpus.Count > 1 ? "+ " + string.Join(", ", snap.Gpus.Skip(1)) : "1 adapter"),
                    new SpecCard("RAM", "MEMORY", snap.Ram, snap.RamDetail),
                    new SpecCard("SSD", "STORAGE", snap.Storage, snap.StorageDetail),
                    new SpecCard("NET", "NETWORK", snap.Network, snap.NetworkDetail)
                };
                SnapshotLoading.Visibility = Visibility.Collapsed;
                Title = "DeviceDossier - " + snap.DeviceName;
            }
            catch (Exception ex)
            {
                SnapshotLoading.Text = "Could not read system information: " + ex.Message;
            }
        }

        private void ApplyOptions(AuditSettings s)
        {
            _applying = true;
            SaveDesktop.IsChecked = s.SaveLocation == SaveLocationMode.Desktop;
            SaveUsb.IsChecked = s.SaveLocation == SaveLocationMode.UsbDrive;
            SaveFolder.IsChecked = s.SaveLocation == SaveLocationMode.Folder;
            OutputFolderBox.Text = string.IsNullOrWhiteSpace(s.CustomFolder) ? Path.Combine(AppPaths.DesktopFolder, s.ReportFolderName) : s.CustomFolder;
            ChkExcel.IsChecked = s.ExportExcel;
            ChkHtml.IsChecked = s.ExportHtml;
            ChkXml.IsChecked = s.ExportXml;
            ChkDrives.IsChecked = s.IncludeMappedDrives;
            ChkSecurity.IsChecked = s.IncludeSecurity;
            ChkPatches.IsChecked = s.IncludePatchHistory;
            ChkSoftware.IsChecked = s.IncludeSoftware;
            ChkExtensions.IsChecked = s.IncludeBrowserExtensions;
            _applying = false;
            UpdateUi();
        }

        private SaveLocationMode CurrentMode
        {
            get
            {
                if (SaveUsb.IsChecked == true) return SaveLocationMode.UsbDrive;
                if (SaveFolder.IsChecked == true) return SaveLocationMode.Folder;
                return SaveLocationMode.Desktop;
            }
        }

        private string ResolvedFolder()
        {
            switch (CurrentMode)
            {
                case SaveLocationMode.UsbDrive:
                    var usb = UsbCombo.SelectedItem as UsbDrive;
                    return usb == null ? null : Path.Combine(usb.Root, _settings.ReportFolderName);
                case SaveLocationMode.Folder:
                    string folder = OutputFolderBox.Text.Trim();
                    return folder.Length == 0 ? null : Environment.ExpandEnvironmentVariables(folder);
                default:
                    return Path.Combine(AppPaths.DesktopFolder, _settings.ReportFolderName);
            }
        }

        private ReportFormats SelectedFormats()
        {
            var f = ReportFormats.None;
            if (ChkExcel.IsChecked == true) f |= ReportFormats.Excel;
            if (ChkHtml.IsChecked == true) f |= ReportFormats.Html;
            if (ChkXml.IsChecked == true) f |= ReportFormats.Xml;
            return f;
        }

        private AuditOptions ReadOptions()
        {
            return new AuditOptions
            {
                OutputFolder = ResolvedFolder(),
                Formats = SelectedFormats(),
                IncludeMappedDrives = ChkDrives.IsChecked == true,
                IncludeSecurity = ChkSecurity.IsChecked == true,
                IncludePatchHistory = ChkPatches.IsChecked == true && ChkSecurity.IsChecked == true,
                IncludeSoftware = ChkSoftware.IsChecked == true,
                IncludeBrowserExtensions = ChkExtensions.IsChecked == true,
                AssetTag = AssetTagBox.Text,
                AssignedTo = AssignedToBox.Text,
                Location = LocationBox.Text,
                Notes = NotesBox.Text
            };
        }

        private void UpdateUi()
        {
            if (_applying || RunButton == null) return;
            var mode = CurrentMode;
            UsbRow.Visibility = mode == SaveLocationMode.UsbDrive ? Visibility.Visible : Visibility.Collapsed;
            FolderRow.Visibility = mode == SaveLocationMode.Folder ? Visibility.Visible : Visibility.Collapsed;
            UsbChoiceHint.Text = _usbDrives.Count == 0 ? "No USB drive detected" : _usbDrives.Count == 1 ? "1 drive detected" : _usbDrives.Count + " drives detected";
            ChkPatches.IsEnabled = ChkSecurity.IsChecked == true;

            var o = ReadOptions();
            string extensions = string.Join(" / ", new[] { (o.Formats & ReportFormats.Excel) != 0 ? ".xlsx" : null, (o.Formats & ReportFormats.Html) != 0 ? ".html" : null, (o.Formats & ReportFormats.Xml) != 0 ? ".xml" : null }.Where(x => x != null));
            if (o.OutputFolder == null)
                OutputHint.Text = mode == SaveLocationMode.UsbDrive ? "Plug in a USB drive - it appears here automatically." : "Type or browse to the folder the reports should be saved in.";
            else
                OutputHint.Text = "Saved to " + o.OutputFolder + (extensions.Length > 0 ? "  as  " + _settings.FileNamePrefix + "_<device>_<date>" + extensions : "") + ". If the folder can't be written to, the Desktop is used instead.";

            CloudWarning.Visibility = AppPaths.IsCloudSynced(o.OutputFolder) ? Visibility.Visible : Visibility.Collapsed;
            RunButtonText.Text = o.IsFullAudit ? "Run full audit" : "Run custom audit";
            RunTitle.Text = o.Formats == ReportFormats.None || o.OutputFolder == null ? "Almost ready" : o.IsFullAudit ? "Ready for a full audit" : "Ready for a custom audit";
            if (o.Formats == ReportFormats.None) RunHint.Text = "Choose at least one report format.";
            else if (o.OutputFolder == null) RunHint.Text = "Choose where the reports should be saved.";
            else RunHint.Text = o.IsFullAudit ? "Everything will be collected. This usually takes 1-3 minutes." : "Sections you untick stay in the report, marked as skipped.";
            RunButton.IsEnabled = !_running && o.Formats != ReportFormats.None && o.OutputFolder != null;
        }

        private async Task RefreshUsbAsync()
        {
            string previous = UsbCombo.SelectedItem is UsbDrive ? ((UsbDrive)UsbCombo.SelectedItem).Root : null;
            List<UsbDrive> drives;
            try
            {
                drives = await Task.Run(() => UsbDrives.List());
            }
            catch
            {
                drives = new List<UsbDrive>();
            }
            _usbDrives = drives;
            UsbCombo.ItemsSource = drives;
            var keep = drives.FirstOrDefault(d => string.Equals(d.Root, previous, StringComparison.OrdinalIgnoreCase))
                       ?? drives.FirstOrDefault(d => d.IsToolDrive)
                       ?? drives.FirstOrDefault();
            UsbCombo.SelectedItem = keep;
            UsbCombo.IsEnabled = drives.Count > 0;
            UpdateUi();
        }

        private void SaveMode_Changed(object sender, RoutedEventArgs e) { UpdateUi(); }
        private void Option_Changed(object sender, RoutedEventArgs e) { UpdateUi(); }
        private void OutputFolderBox_TextChanged(object sender, TextChangedEventArgs e) { UpdateUi(); }
        private void UsbCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateUi(); }

        private async void RefreshUsb_Click(object sender, RoutedEventArgs e)
        {
            await RefreshUsbAsync();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var picked = FolderPicker.Pick(new WindowInteropHelper(this).Handle, "Choose where to save the audit reports", OutputFolderBox.Text);
            if (!string.IsNullOrEmpty(picked)) OutputFolderBox.Text = picked;
        }

        private void RestartAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (Elevation.TryRestartElevated(App.ElevatedFlag)) Application.Current.Shutdown();
        }

        private void ShowPage(string page)
        {
            _page = page;
            HomePage.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
            ProgressPage.Visibility = page == "progress" ? Visibility.Visible : Visibility.Collapsed;
            ResultPage.Visibility = page == "result" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
            NavHome.Tag = page == "home" ? "active" : null;
            NavProgress.Tag = page == "progress" ? "active" : null;
            NavReport.Tag = page == "result" ? "active" : null;
            NavSettings.Tag = page == "settings" ? "active" : null;
        }

        private void NavHome_Click(object sender, RoutedEventArgs e) { ShowPage("home"); }
        private void NavProgress_Click(object sender, RoutedEventArgs e) { ShowPage("progress"); }
        private void NavReport_Click(object sender, RoutedEventArgs e) { ShowPage("result"); }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_page != "settings") FillSettingsForm(_settings);
            SettingsStatusBar.Visibility = Visibility.Collapsed;
            ShowPage("settings");
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_running) return;
            var options = ReadOptions();
            if (options.OutputFolder == null || options.Formats == ReportFormats.None) return;

            _running = true;
            _result = null;
            _cts = new CancellationTokenSource();
            _steps.Clear();
            _log.Clear();
            foreach (var s in AuditEngine.Steps())
            {
                var item = new StepItem(s.Id, s.Label);
                if (s.Enabled != null && !s.Enabled(options))
                {
                    item.State = StepState.Skipped;
                    item.Status = "skipped";
                }
                _steps.Add(item);
            }
            _steps.Add(new StepItem("save", "Save reports"));

            ProgressTitle.Text = "Auditing " + Environment.MachineName;
            ProgressSubtitle.Text = (options.IsFullAudit ? "Full audit" : "Custom audit") + (_isAdmin ? " · running as administrator" : " · limited mode (no administrator rights)");
            Progress.Value = 0;
            PercentText.Text = "0%";
            StepText.Text = "Starting...";
            CancelButton.IsEnabled = true;
            CancelButton.Content = "Cancel audit";
            NavProgress.IsEnabled = true;
            NavReport.IsEnabled = false;
            NavOpen.IsEnabled = false;
            RunButton.IsEnabled = false;
            _started = DateTime.Now;
            ElapsedText.Text = "Elapsed 0s";
            _clock.Start();
            ShowPage("progress");

            try
            {
                _result = await AuditEngine.RunAsync(options, _settings, new Progress<AuditProgress>(OnProgress), _cts.Token);
                MarkRunning(StepState.Done, "");
                Progress.Value = 100;
                PercentText.Text = "100%";
                StepText.Text = "Done";
                CancelButton.IsEnabled = false;
                AssetTagBox.Text = "";
                AssignedToBox.Text = "";
                LocationBox.Text = "";
                NotesBox.Text = "";
                ShowResult(false);
            }
            catch (OperationCanceledException)
            {
                AddLog("Audit cancelled - no report was saved.", LogLevel.Warning);
                StepText.Text = "Cancelled";
                MarkRunning(StepState.Skipped, "cancelled");
                CancelButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                App.WriteCrashLog(ex);
                AddLog("Audit failed: " + ex.Message, LogLevel.Error);
                StepText.Text = "The audit stopped because of an error";
                MarkRunning(StepState.Failed, "failed");
                CancelButton.IsEnabled = false;
                MessageBox.Show(this, "The audit could not finish:\n\n" + ex.Message, "DeviceDossier", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _clock.Stop();
                _running = false;
                NavOpen.IsEnabled = true;
                UpdateUi();
            }
        }

        private void OnProgress(AuditProgress p)
        {
            AddLog(p.Message, p.Level);
            if (p.TotalSteps > 0)
            {
                double pct = Math.Min(100, Math.Max(0, (p.Step - 1) * 100.0 / p.TotalSteps));
                Progress.Value = pct;
                PercentText.Text = ((int)pct) + "%";
            }

            var step = _steps.FirstOrDefault(s => s.Id == p.StepId);
            if (step == null) return;
            foreach (var s in _steps.Where(s => s.State == StepState.Running && s != step))
            {
                s.State = StepState.Done;
                s.Status = "";
            }
            if (step.State == StepState.Skipped) return;
            StepText.Text = step.Label;
            switch (p.Level)
            {
                case LogLevel.Error:
                    step.State = StepState.Failed;
                    step.Status = "issue";
                    break;
                case LogLevel.Warning:
                    if (step.State != StepState.Failed)
                    {
                        step.State = StepState.Warning;
                        step.Status = "incomplete";
                    }
                    break;
                case LogLevel.Ok:
                    if (step.State == StepState.Running || step.State == StepState.Pending)
                    {
                        step.State = StepState.Done;
                        step.Status = "";
                    }
                    break;
                default:
                    if (step.State == StepState.Pending)
                    {
                        step.State = StepState.Running;
                        step.Status = "working";
                    }
                    break;
            }
        }

        private void MarkRunning(StepState state, string status)
        {
            foreach (var s in _steps.Where(s => s.State == StepState.Running || s.State == StepState.Pending))
            {
                s.State = state;
                s.Status = status;
            }
        }

        private void AddLog(string message, LogLevel level)
        {
            _log.Add(new LogLine(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), message, level.ToString()));
            LogList.ScrollIntoView(_log[_log.Count - 1]);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cts == null || !_running) return;
            _cts.Cancel();
            CancelButton.IsEnabled = false;
            CancelButton.Content = "Cancelling...";
            StepText.Text = "Cancelling after the current step...";
        }

        private void ShowResult(bool fromFile)
        {
            var report = _result.Report;
            int issues = report.Sections.Count(s => s.State == SectionState.Warning || s.State == SectionState.Error);
            int skipped = report.Sections.Count(s => s.State == SectionState.Skipped);

            string when = report.Generated.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
            if (fromFile)
            {
                ResultTitle.Text = "Saved report - " + report.DeviceName;
                ResultSummary.Text = "Audited " + when + " by " + report.RunAsUser + (report.RanAsAdmin ? "" : " (limited mode)") + " · " + report.ToolName + " " + report.ToolVersion +
                                     (issues > 0 ? " · " + issues + " section(s) need attention" : "");
            }
            else
            {
                ResultTitle.Text = issues == 0 ? "Audit complete" : "Audit complete - " + issues + " section(s) need attention";
                ResultSummary.Text = report.DeviceName + " · finished in " + FormatSpan(_result.Duration) + (skipped > 0 ? " · " + skipped + " section(s) skipped" : "");
            }
            ResultIcon.SetResourceReference(Border.BackgroundProperty, issues == 0 ? "SuccessSoft" : "WarningSoft");
            ResultIconText.Text = issues == 0 ? "✓" : "!";
            ResultIconText.SetResourceReference(TextBlock.ForegroundProperty, issues == 0 ? "Success" : "Warning");
            ResultNote.Text = _result.SaveNote ?? "";
            ResultNote.Visibility = string.IsNullOrEmpty(_result.SaveNote) ? Visibility.Collapsed : Visibility.Visible;
            UpdateResultFiles();

            ReportViewBuilder.Populate(ReportTabs, report);
            NavReport.IsEnabled = true;
            ShowPage("result");
        }

        private IEnumerable<string> ResultFiles()
        {
            if (_result == null) yield break;
            if (_result.ExcelPath != null) yield return _result.ExcelPath;
            if (_result.HtmlPath != null) yield return _result.HtmlPath;
            if (_result.XmlPath != null) yield return _result.XmlPath;
        }

        private void UpdateResultFiles()
        {
            ResultPath.Text = string.Join(Environment.NewLine, ResultFiles());
            OpenExcelButton.Visibility = _result.ExcelPath != null ? Visibility.Visible : Visibility.Collapsed;
            OpenHtmlButton.Visibility = _result.HtmlPath != null ? Visibility.Visible : Visibility.Collapsed;
            OpenFolderButton.Visibility = ResultFiles().Any() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OpenExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_result != null && _result.ExcelPath != null) Open(_result.ExcelPath, "open the Excel report. Is Microsoft Excel (or another spreadsheet app) installed?");
        }

        private void OpenHtml_Click(object sender, RoutedEventArgs e)
        {
            if (_result != null && _result.HtmlPath != null) Open(_result.HtmlPath, "open the HTML report in your browser.");
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string file = ResultFiles().FirstOrDefault();
            if (file == null) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true });
            }
            catch
            {
                Open(Path.GetDirectoryName(file), "open the folder.");
            }
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (_result == null) return;
            var report = _result.Report;
            var dialog = new SaveFileDialog
            {
                Title = "Save report as",
                Filter = "Excel workbook (*.xlsx)|*.xlsx|HTML report (*.html)|*.html|XML data (*.xml)|*.xml",
                FileName = AuditEngine.SafeFileName(_settings.FileNamePrefix) + "_" + AuditEngine.SafeFileName(report.DeviceName ?? "PC") + "_" +
                           report.Generated.ToString("ddMMyyyy_HHmm", CultureInfo.InvariantCulture),
                AddExtension = true,
                OverwritePrompt = true
            };
            string folder = _result.Folder ?? (ResultFiles().FirstOrDefault() != null ? Path.GetDirectoryName(ResultFiles().First()) : null);
            if (folder != null && Directory.Exists(folder)) dialog.InitialDirectory = folder;
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                if (ext == ".html" || ext == ".htm")
                {
                    HtmlReportExporter.Save(report, dialog.FileName);
                    _result.HtmlPath = dialog.FileName;
                }
                else if (ext == ".xml")
                {
                    XmlReportFile.Save(report, dialog.FileName);
                    _result.XmlPath = dialog.FileName;
                }
                else
                {
                    AuditExcelExporter.Save(report, _settings, dialog.FileName);
                    _result.ExcelPath = dialog.FileName;
                }
                UpdateResultFiles();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the report:\n\n" + ex.Message, "DeviceDossier", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenSaved_Click(object sender, RoutedEventArgs e)
        {
            if (_running) return;
            var dialog = new OpenFileDialog
            {
                Title = "Open a DeviceDossier report",
                Filter = "DeviceDossier XML report (*.xml)|*.xml|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var report = XmlReportFile.Load(dialog.FileName);
                _result = new AuditResult { Report = report, XmlPath = dialog.FileName, Folder = Path.GetDirectoryName(dialog.FileName) };
                ShowResult(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "This file could not be opened as a DeviceDossier report. Only XML reports saved by DeviceDossier 1.1 or later can be opened.\n\n" + ex.Message,
                    "DeviceDossier", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Open(string path, string what)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not " + what + "\n\n" + ex.Message, "DeviceDossier", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void NewAudit_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("home");
        }

        private void FillSettingsForm(AuditSettings s)
        {
            _applying = true;
            SetTheme.SelectedIndex = (int)s.Theme;
            _applying = false;
            SetSaveMode.SelectedIndex = (int)s.SaveLocation;
            SetFolder.Text = s.CustomFolder ?? "";
            SetSubfolder.Text = s.ReportFolderName ?? "";
            SetPrefix.Text = s.FileNamePrefix ?? "";
            SetExcel.IsChecked = s.ExportExcel;
            SetHtml.IsChecked = s.ExportHtml;
            SetXml.IsChecked = s.ExportXml;
            SetDrives.IsChecked = s.IncludeMappedDrives;
            SetSecurity.IsChecked = s.IncludeSecurity;
            SetPatches.IsChecked = s.IncludePatchHistory;
            SetSoftware.IsChecked = s.IncludeSoftware;
            SetExtensions.IsChecked = s.IncludeBrowserExtensions;
            SetMaskKey.IsChecked = s.MaskProductKey;
            SetExcludeTool.IsChecked = s.ExcludeToolDrive;
            SetAskAdmin.IsChecked = s.RequestAdminAtStartup;
            SetTimeout.Text = s.PatchTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            SetStepTimeout.Text = s.StepTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            SetDisks.Text = string.Join(Environment.NewLine, s.ExcludedDiskModels);
            SetServers.Text = string.Join(Environment.NewLine, s.KnownServers.Select(k => string.IsNullOrEmpty(k.Label) ? k.Address : k.Address + " = " + k.Label));
            SettingsPathText.Text = _settings.LoadedFrom != null
                ? "Loaded from " + _settings.LoadedFrom
                : "Not saved yet - built-in defaults are in use. Saving creates " + AuditSettings.PortablePath;
            SetFolderPanel.Visibility = SetSaveMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private AuditSettings ReadSettingsForm(out string error)
        {
            error = null;
            var s = new AuditSettings
            {
                SaveLocation = (SaveLocationMode)Math.Max(0, SetSaveMode.SelectedIndex),
                Theme = (AppTheme)Math.Max(0, SetTheme.SelectedIndex),
                CustomFolder = SetFolder.Text.Trim(),
                ReportFolderName = SetSubfolder.Text.Trim(),
                FileNamePrefix = SetPrefix.Text.Trim(),
                ExportExcel = SetExcel.IsChecked == true,
                ExportHtml = SetHtml.IsChecked == true,
                ExportXml = SetXml.IsChecked == true,
                IncludeMappedDrives = SetDrives.IsChecked == true,
                IncludeSecurity = SetSecurity.IsChecked == true,
                IncludePatchHistory = SetPatches.IsChecked == true,
                IncludeSoftware = SetSoftware.IsChecked == true,
                IncludeBrowserExtensions = SetExtensions.IsChecked == true,
                MaskProductKey = SetMaskKey.IsChecked == true,
                ExcludeToolDrive = SetExcludeTool.IsChecked == true,
                RequestAdminAtStartup = SetAskAdmin.IsChecked == true
            };

            int timeout;
            if (!int.TryParse(SetTimeout.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out timeout) || timeout < 10 || timeout > 600)
                error = "The Windows Update timeout must be a whole number of seconds between 10 and 600.";
            else s.PatchTimeoutSeconds = timeout;

            int stepTimeout;
            if (!int.TryParse(SetStepTimeout.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stepTimeout) || stepTimeout < 30 || stepTimeout > 1800)
                error = error ?? "The section time limit must be a whole number of seconds between 30 and 1800.";
            else s.StepTimeoutSeconds = stepTimeout;

            if (s.SaveLocation == SaveLocationMode.Folder && s.CustomFolder.Length == 0)
                error = error ?? "Choose the folder reports should be saved in, or pick another default location.";
            if (!s.ExportExcel && !s.ExportHtml && !s.ExportXml)
                error = error ?? "Choose at least one default report format.";

            foreach (var raw in Lines(SetServers.Text))
            {
                int eq = raw.IndexOf('=');
                string address = (eq >= 0 ? raw.Substring(0, eq) : raw).Trim().TrimStart('\\');
                int slash = address.IndexOf('\\');
                if (slash >= 0) address = address.Substring(0, slash);
                string label = eq >= 0 ? raw.Substring(eq + 1).Trim() : "";
                if (address.Length == 0 || address.Any(char.IsWhiteSpace))
                {
                    error = error ?? "Known servers: \"" + raw + "\" is not a valid server name or IP address.";
                    continue;
                }
                s.KnownServers.Add(new KnownServer(address, label));
            }
            s.ExcludedDiskModels.AddRange(Lines(SetDisks.Text));
            s.Normalize();
            return s;
        }

        private static IEnumerable<string> Lines(string text)
        {
            return (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith(";"));
        }

        private void SetStatus(string text, LogLevel level)
        {
            SettingsStatus.Text = text;
            SettingsStatus.SetResourceReference(TextBlock.ForegroundProperty, level == LogLevel.Error ? "Danger" : level == LogLevel.Warning ? "Warning" : "Success");
            SettingsStatusBar.SetResourceReference(Border.BackgroundProperty, level == LogLevel.Error ? "DangerSoft" : level == LogLevel.Warning ? "WarningSoft" : "SuccessSoft");
            SettingsStatusBar.Visibility = Visibility.Visible;
        }

        private void ApplySaved(AuditSettings s)
        {
            _settings = s;
            if (!_running) ApplyOptions(_settings);
            FillSettingsForm(_settings);
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string error;
            var s = ReadSettingsForm(out error);
            if (error != null)
            {
                SetStatus(error, LogLevel.Error);
                return;
            }
            try
            {
                string path = s.Save();
                ApplySaved(s);
                SetStatus("Settings saved to " + path + ". They are used for every new audit.", LogLevel.Ok);
            }
            catch (Exception ex)
            {
                SetStatus("Could not save settings: " + ex.Message, LogLevel.Error);
            }
        }

        private void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import DeviceDossier settings",
                Filter = "DeviceDossier settings (*.xml;*.ini)|*.xml;*.ini|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;
            AuditSettings s;
            try
            {
                s = AuditSettings.Import(dialog.FileName);
            }
            catch (Exception ex)
            {
                SetStatus("This file is not a valid DeviceDossier settings file: " + (ex.InnerException ?? ex).Message, LogLevel.Error);
                return;
            }
            try
            {
                string path = s.Save();
                ApplySaved(s);
                SetStatus("Imported " + Path.GetFileName(dialog.FileName) + " and saved it to " + path + ".", LogLevel.Ok);
            }
            catch (Exception ex)
            {
                FillSettingsForm(s);
                SetStatus("Imported, but the settings could not be saved (" + ex.Message + "). They apply until DeviceDossier is closed.", LogLevel.Warning);
                _settings = s;
                if (!_running) ApplyOptions(_settings);
            }
        }

        private void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            string error;
            var s = ReadSettingsForm(out error);
            if (error != null)
            {
                SetStatus(error, LogLevel.Error);
                return;
            }
            var dialog = new SaveFileDialog
            {
                Title = "Export DeviceDossier settings",
                Filter = "DeviceDossier settings (*.xml)|*.xml",
                FileName = AuditSettings.FileName,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                s.Export(dialog.FileName);
                SetStatus("Exported to " + dialog.FileName + ". Put it next to DeviceDossier.exe on another USB drive, or use Import on another PC.", LogLevel.Ok);
            }
            catch (Exception ex)
            {
                SetStatus("Could not export settings: " + ex.Message, LogLevel.Error);
            }
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            FillSettingsForm(new AuditSettings());
            SetStatus("Defaults restored in the form. Click Save settings to keep them.", LogLevel.Warning);
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ChangeTheme(ThemeManager.Next(ThemeManager.Mode));
        }

        private void SetTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_applying || SetTheme.SelectedIndex < 0) return;
            ChangeTheme((AppTheme)SetTheme.SelectedIndex);
        }

        private void ChangeTheme(AppTheme theme)
        {
            if (_settings.Theme != theme)
            {
                _settings.Theme = theme;
                try
                {
                    _settings.Save();
                }
                catch
                {
                }
            }
            ThemeManager.Apply(theme);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            UpdateThemeUi();
        }

        private void UpdateThemeUi()
        {
            var mode = ThemeManager.Mode;
            ThemeText.Text = "Theme: " + ThemeManager.Describe(mode);
            ThemeIcon.Text = mode == AppTheme.Light ? "\uE706" : mode == AppTheme.Dark ? "\uE708" : "\uE770";
            if (SetTheme.SelectedIndex != (int)mode)
            {
                _applying = true;
                SetTheme.SelectedIndex = (int)mode;
                _applying = false;
            }
        }

        private async void RefreshSnapshot_Click(object sender, RoutedEventArgs e)
        {
            SnapshotLoading.Text = "Reading system information...";
            SnapshotLoading.Visibility = Visibility.Visible;
            await RefreshUsbAsync();
            await LoadSnapshotAsync();
        }

        private void SetSaveMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SetFolderPanel != null) SetFolderPanel.Visibility = SetSaveMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetBrowse_Click(object sender, RoutedEventArgs e)
        {
            var picked = FolderPicker.Pick(new WindowInteropHelper(this).Handle, "Choose the default folder for audit reports", SetFolder.Text);
            if (!string.IsNullOrEmpty(picked)) SetFolder.Text = picked;
        }

        private void SetPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SetPrefixHint == null) return;
            string prefix = SetPrefix.Text.Trim();
            SetPrefixHint.Text = "Example: " + (prefix.Length == 0 ? "IT_Audit" : AuditEngine.SafeFileName(prefix)) + "_" + Environment.MachineName + "_" +
                                 DateTime.Now.ToString("ddMMyyyy_HHmm", CultureInfo.InvariantCulture) + ".xlsx";
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_running) return;
            var answer = MessageBox.Show(this, "An audit is still running. Stop it and close DeviceDossier?", "DeviceDossier", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            if (_cts != null) _cts.Cancel();
        }

        private static string FormatSpan(TimeSpan t)
        {
            return t.TotalMinutes >= 1 ? string.Format(CultureInfo.InvariantCulture, "{0}m {1:00}s", (int)t.TotalMinutes, t.Seconds) : string.Format(CultureInfo.InvariantCulture, "{0}s", (int)t.TotalSeconds);
        }
    }
}
