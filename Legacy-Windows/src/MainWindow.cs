using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace RoMulti
{
    public sealed class MainWindow : Window
    {
        internal const string AppVersion = "v1.4.4";
        private const int PeriodicTrimIntervalSeconds = 45;
        private const int AutomaticFocusIdleMs = 5000;

        // Color palette — refined Obsidian Slate dark engineering UI
        private static readonly SolidColorBrush BgCanvas = BrushFromHex("#0D1017");
        private static readonly SolidColorBrush BgSurface = BrushFromHex("#141923");
        private static readonly SolidColorBrush BgElevated = BrushFromHex("#1B2230");
        private static readonly SolidColorBrush BgHover = BrushFromHex("#232C3D");
        private static readonly SolidColorBrush BorderSubtle = BrushFromHex("#252E40");
        private static readonly SolidColorBrush TextPrimary = BrushFromHex("#E6EDF3");
        private static readonly SolidColorBrush TextSecondary = BrushFromHex("#8B96A8");
        private static readonly SolidColorBrush AccentBlue = BrushFromHex("#3B82F6");
        private static readonly SolidColorBrush AccentBlueBg = BrushFromHex("#13233D");
        private static readonly SolidColorBrush TealAccent = BrushFromHex("#14B8A6");
        private static readonly SolidColorBrush TealBg = BrushFromHex("#0F2928");
        private static readonly SolidColorBrush PurpleAccent = BrushFromHex("#C084FC");
        private static readonly SolidColorBrush PurpleBg = BrushFromHex("#28163B");
        private static readonly SolidColorBrush StatusGreen = BrushFromHex("#22C55E");
        private static readonly SolidColorBrush StatusGreenBg = BrushFromHex("#112A1D");
        private static readonly SolidColorBrush StatusAmber = BrushFromHex("#F59E0B");
        private static readonly SolidColorBrush StatusAmberBg = BrushFromHex("#2D220F");
        private static readonly SolidColorBrush StatusRed = BrushFromHex("#EF4444");
        private static readonly SolidColorBrush StatusRedBg = BrushFromHex("#2D1418");

        private readonly RobloxHandleEngine _engine = new RobloxHandleEngine();
        private readonly RobloxLogTracker _logTracker = new RobloxLogTracker();
        private readonly DiscordNotifier _discord = new DiscordNotifier();
        private readonly RoMultiConfig _config;

        private readonly Dictionary<int, string> _customLabels = new Dictionary<int, string>();
        private readonly Dictionary<int, InstanceRole> _instanceRoles = new Dictionary<int, InstanceRole>();
        private readonly HashSet<int> _appliedAfkPids = new HashSet<int>();
        private readonly HashSet<int> _disconnectedScoutPids = new HashSet<int>();
        private readonly Dictionary<int, PassiveFrameHistory> _passiveFrames = new Dictionary<int, PassiveFrameHistory>();
        private readonly ObservableCollection<RobloxInstanceInfo> _instances = new ObservableCollection<RobloxInstanceInfo>();
        private readonly ObservableCollection<RobloxHandleInfo> _selectedHandles = new ObservableCollection<RobloxHandleInfo>();
        private readonly ObservableCollection<string> _activityLog = new ObservableCollection<string>();
        private readonly ObservableCollection<EventRuleRowViewModel> _ruleRows = new ObservableCollection<EventRuleRowViewModel>();

        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _watcherTimer;
        private DateTime _lastPeriodicTrimUtc = DateTime.UtcNow;
        private DateTime _nextScoutAntiAfkUtc = DateTime.UtcNow.AddSeconds(20);
        private DateTime _nextMainAntiAfkUtc = DateTime.UtcNow.AddSeconds(20);
        private DateTime _nextAutoPeekUtc = DateTime.UtcNow.AddSeconds(15);
        private DateTime _lastBlackFrameLogUtc = DateTime.MinValue;
        private Task _watcherDelivery = Task.FromResult(0);

        private bool _isScanning;
        private bool _isWatching;
        private bool _isChangingFocus;
        private bool _isExiting;
        private int _selectedPid = -1;

        // UI Controls — Header & KPIs
        private ToggleButton _autoUnlockToggle;
        private CheckBox _includeMutexCheck;
        private CheckBox _autoAfkCheck;
        private TextBlock _kpiCountValue;
        private TextBlock _kpiCountSub;
        private TextBlock _kpiStatusValue;
        private TextBlock _kpiStatusSub;
        private TextBlock _kpiRamValue;
        private TextBlock _kpiRamSub;

        // UI Controls — Tab 1 (Instances & Handles)
        private ListView _instancesList;
        private ListView _handlesList;
        private TextBlock _handlesHeaderSub;
        private Button _toggleAfkBtn;

        // UI Controls — Tab 2 (Event Watcher & Anti-AFK)
        private TextBox _webhookBox;
        private TextBox _pingTextBox;
        private TextBox _privateServerBox;
        private TextBlock _privateServerPlaceholder;
        private TextBlock _privateServerStatusLabel;
        private CheckBox _attachScreenshotCheck;
        private CheckBox _alertMapChangeCheck;
        private CheckBox _bgHoverCheck;
        private CheckBox _wakeChatPulseCheck;
        private CheckBox _watchMainTooCheck;
        private CheckBox _resetCharAfkCheck;
        private CheckBox _antiAfkMainIdleCheck;
        private ToggleButton _watcherToggle;
        private ToggleButton _autoPeekToggle;
        private ComboBox _autoPeekIntervalCombo;
        private TextBlock _watcherStatusLabel;
        private ToggleButton _antiAfkToggle;
        private ComboBox _antiAfkIntervalCombo;
        private TextBlock _antiAfkCountdownLabel;
        private System.Windows.Controls.Image _previewImage;
        private TextBlock _previewDiagText;
        private TextBox _previewOcrBox;

        // UI Controls — Footer & Tray
        private ListBox _logList;
        private Forms.NotifyIcon _trayIcon;
        private Forms.ToolStripMenuItem _trayAutoUnlockItem;
        private Forms.ToolStripMenuItem _trayWatcherItem;
        private Forms.ToolStripMenuItem _trayAutoPeekItem;
        private Forms.ToolStripMenuItem _trayAntiAfkItem;
        private Forms.ToolStripMenuItem _trayMainAntiAfkItem;

        public MainWindow()
        {
            _config = RoMultiConfig.Load();

            Title = "RoMulti " + AppVersion + " — Roblox Multi-Instance, AFK Saver & Event Watcher";
            Icon = CreateWindowIconSource();
            Width = 1180;
            Height = 810;
            MinWidth = 980;
            MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BgCanvas;
            Foreground = TextPrimary;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

            SourceInitialized += (s, e) =>
            {
                var helper = new WindowInteropHelper(this);
                NativeMethods.EnableDarkTitleBar(helper.Handle);
            };

            Closing += OnWindowClosing;

            PopulateRuleRows();
            BuildUi();
            InitTrayIcon();

            string ocrLang = ChatOcrEngine.GetAvailableOcrLanguage();
            Log("INFO", "RoMulti Event Detector " + AppVersion + " ready. Mode: " + (RobloxHandleEngine.IsRunningAsAdmin() ? "Administrator" : "Standard User") +
                " | WinRT OCR: " + (string.IsNullOrEmpty(ocrLang) ? "Unavailable" : ocrLang));
            Log("INFO", "Priority alerts armed: Devil Rift ('Something feels off...') & Spirit Rift ('strange energy... kingdom').");

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _refreshTimer.Tick += async (s, e) => await RefreshAsync(allowAutoActions: true);
            _refreshTimer.Start();

            _watcherTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(2, _config.ScanIntervalSeconds))
            };
            _watcherTimer.Tick += async (s, e) => await RunWatcherTickAsync();
            _watcherTimer.Start();

            Loaded += async (s, e) =>
            {
                await RefreshAsync(allowAutoActions: false);
                AfkOptimizer.TrimSelfWorkingSet();
            };
        }

        private void PopulateRuleRows()
        {
            _ruleRows.Clear();
            foreach (var rule in EventCatalog.Rules)
            {
                _ruleRows.Add(new EventRuleRowViewModel
                {
                    Rule = rule,
                    Level = _config.GetEffectiveLevel(rule)
                });
            }
        }

        private void BuildUi()
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // KPI Strip
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Main Tabs
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(108) }); // Activity Log

            var header = BuildHeader();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var kpis = BuildKpiStrip();
            Grid.SetRow(kpis, 1);
            root.Children.Add(kpis);

            var tabs = BuildMainTabs();
            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            var logCard = BuildLogCard();
            Grid.SetRow(logCard, 3);
            root.Children.Add(logCard);

            Content = root;
        }

        private UIElement BuildHeader()
        {
            var bar = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 11, 16, 11),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var logoBadge = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = AccentBlue,
                Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock
                {
                    Text = "R+",
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            leftStack.Children.Add(logoBadge);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(new TextBlock
            {
                Text = "RoMulti",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary
            });

            var versionPill = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Background = AccentBlueBg,
                BorderBrush = AccentBlue,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = AppVersion,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = AccentBlue
                }
            };
            titleRow.Children.Add(versionPill);

            bool isAdmin = RobloxHandleEngine.IsRunningAsAdmin();
            var rolePill = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(10, 0, 0, 0),
                Background = isAdmin ? StatusGreenBg : BgElevated,
                BorderBrush = isAdmin ? StatusGreen : BorderSubtle,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = isAdmin ? "ADMIN" : "STANDARD",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isAdmin ? StatusGreen : TextSecondary
                }
            };
            titleRow.Children.Add(rolePill);

            if (!isAdmin)
            {
                var elevateBtn = CreateCompactButton("Run as Admin", BgElevated, TextPrimary, BorderSubtle);
                elevateBtn.Margin = new Thickness(8, 0, 0, 0);
                elevateBtn.Padding = new Thickness(8, 2, 8, 2);
                elevateBtn.FontSize = 11;
                elevateBtn.ToolTip = "Restart RoMulti as Administrator (only needed if Roblox runs elevated)";
                elevateBtn.Click += (s, e) => RestartAsAdmin();
                titleRow.Children.Add(elevateBtn);
            }

            titleStack.Children.Add(titleRow);
            titleStack.Children.Add(new TextBlock
            {
                Text = "Roblox Multi-Instance · AFK Saver · Clover Retribution Event Watcher",
                FontSize = 12,
                Foreground = TextSecondary,
                Margin = new Thickness(0, 2, 0, 0)
            });
            leftStack.Children.Add(titleStack);
            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _includeMutexCheck = new CheckBox
            {
                Content = "Mutex",
                IsChecked = true,
                Foreground = TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                FontSize = 12,
                ToolTip = "Also close ROBLOX_singletonMutex alongside ROBLOX_singletonEvent"
            };
            _includeMutexCheck.Click += async (s, e) => await RefreshAsync(allowAutoActions: false);
            rightStack.Children.Add(_includeMutexCheck);

            _autoAfkCheck = new CheckBox
            {
                Content = "Auto-AFK new",
                IsChecked = false,
                Foreground = TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                FontSize = 12,
                ToolTip = "Automatically tag newly launched Roblox clients (after Main) as AFK Saver"
            };
            rightStack.Children.Add(_autoAfkCheck);

            _autoUnlockToggle = new ToggleButton
            {
                Content = "Auto-Unlock: OFF",
                IsChecked = false,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                Background = BgElevated,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(_autoUnlockToggle);
            _autoUnlockToggle.Checked += async (s, e) =>
            {
                UpdateAutoUnlockVisual(true);
                Log("INFO", "Auto-Unlock enabled — new Roblox instances will be unlocked automatically.");
                await RefreshAsync(allowAutoActions: true);
            };
            _autoUnlockToggle.Unchecked += (s, e) =>
            {
                UpdateAutoUnlockVisual(false);
                Log("INFO", "Auto-Unlock disabled.");
            };
            rightStack.Children.Add(_autoUnlockToggle);

            var unlockAllBtn = CreateActionButton("Unlock All", AccentBlue, System.Windows.Media.Brushes.White, AccentBlue);
            unlockAllBtn.Margin = new Thickness(0, 0, 8, 0);
            unlockAllBtn.Click += async (s, e) => await UnlockAllAsync();
            rightStack.Children.Add(unlockAllBtn);

            var launchBtn = CreateActionButton("Launch Roblox", BgElevated, TextPrimary, BorderSubtle);
            launchBtn.Margin = new Thickness(0, 0, 8, 0);
            launchBtn.Click += (s, e) => LaunchNewRoblox();
            rightStack.Children.Add(launchBtn);

            var trayBtn = CreateActionButton("Tray", BgElevated, TextSecondary, BorderSubtle);
            trayBtn.ToolTip = "Minimize RoMulti to System Tray (and trim RoMulti RAM)";
            trayBtn.Click += (s, e) => HideToTray();
            rightStack.Children.Add(trayBtn);

            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(rightStack);

            bar.Child = grid;
            return bar;
        }

        private UIElement BuildKpiStrip()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });

            var c1 = BuildKpiCard("ACTIVE INSTANCES", "0", "Waiting for RobloxPlayerBeta.exe", out _kpiCountValue, out _kpiCountSub);
            Grid.SetColumn(c1, 0);
            grid.Children.Add(c1);

            var c2 = BuildKpiCard("MULTI-INSTANCE STATE", "Idle", "No active singleton lock", out _kpiStatusValue, out _kpiStatusSub);
            Grid.SetColumn(c2, 2);
            grid.Children.Add(c2);

            var c3 = BuildKpiCard("MEMORY & SYSTEM RAM", "Checking...", "Total RAM", out _kpiRamValue, out _kpiRamSub);
            Grid.SetColumn(c3, 4);
            grid.Children.Add(c3);

            return grid;
        }

        private Border BuildKpiCard(string label, string val, string sub, out TextBlock valBlock, out TextBlock subBlock)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextSecondary
            });

            valBlock = new TextBlock
            {
                Text = val,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = TextPrimary,
                Margin = new Thickness(0, 3, 0, 2)
            };
            stack.Children.Add(valBlock);

            subBlock = new TextBlock
            {
                Text = sub,
                FontSize = 11.5,
                Foreground = TextSecondary
            };
            stack.Children.Add(subBlock);

            return new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 9, 14, 9),
                Child = stack
            };
        }

        private UIElement BuildMainTabs()
        {
            var tabs = new TabControl
            {
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 10)
            };

            // Custom dark TabItem style
            var tabStyle = new Style(typeof(TabItem));
            var tpl = new ControlTemplate(typeof(TabItem));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "TabBd";
            bd.SetValue(Border.BackgroundProperty, BgSurface);
            bd.SetValue(Border.BorderBrushProperty, BorderSubtle);
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 0));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8, 8, 0, 0));
            bd.SetValue(Border.PaddingProperty, new Thickness(16, 7, 16, 7));
            bd.SetValue(Border.MarginProperty, new Thickness(0, 0, 6, 0));
            bd.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            var selTrig = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selTrig.Setters.Add(new Setter(Border.BackgroundProperty, AccentBlueBg, "TabBd"));
            selTrig.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBlue, "TabBd"));
            selTrig.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            tpl.Triggers.Add(selTrig);

            tabStyle.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            tabStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextSecondary));
            tabStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            tabStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            tabs.Resources.Add(typeof(TabItem), tabStyle);

            // Tab 1: Instances & Handles
            var tab1Grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            tab1Grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star) });
            tab1Grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.85, GridUnitType.Star) });

            var instCard = BuildInstancesCard();
            Grid.SetRow(instCard, 0);
            tab1Grid.Children.Add(instCard);

            var handlesCard = BuildHandlesCard();
            Grid.SetRow(handlesCard, 1);
            tab1Grid.Children.Add(handlesCard);

            tabs.Items.Add(new TabItem { Header = "Instances & Handles", Content = tab1Grid });

            // Tab 2: Event Watcher & Anti-AFK
            var tab2Content = BuildEventWatcherTab();
            tabs.Items.Add(new TabItem { Header = "Event Watcher & Anti-AFK", Content = tab2Content });

            return tabs;
        }

        private UIElement BuildInstancesCard()
        {
            var card = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(14, 10, 14, 10)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Roblox Instances",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Tag scout accounts as AFK Saver to trim RAM, enable EcoQoS, mute audio, resize 520x320 & watch events.",
                FontSize = 11.5,
                Foreground = TextSecondary
            });
            Grid.SetColumn(titleStack, 0);
            topGrid.Children.Add(titleStack);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var unlockSelBtn = CreateCompactButton("Unlock", StatusGreenBg, StatusGreen, StatusGreen);
            unlockSelBtn.Margin = new Thickness(0, 0, 6, 0);
            unlockSelBtn.Click += async (s, e) => await UnlockSelectedAsync();
            btnRow.Children.Add(unlockSelBtn);

            _toggleAfkBtn = CreateCompactButton("Set AFK Saver", TealBg, TealAccent, TealAccent);
            _toggleAfkBtn.Margin = new Thickness(0, 0, 6, 0);
            _toggleAfkBtn.Click += async (s, e) => await ToggleSelectedRoleAsync();
            btnRow.Children.Add(_toggleAfkBtn);

            var trimNowBtn = CreateCompactButton("Trim AFK RAM", BgElevated, TealAccent, BorderSubtle);
            trimNowBtn.Margin = new Thickness(0, 0, 6, 0);
            trimNowBtn.Click += async (s, e) => await TrimAllAfkRamAsync(isManual: true);
            btnRow.Children.Add(trimNowBtn);

            var focusBtn = CreateCompactButton("Focus", BgElevated, TextPrimary, BorderSubtle);
            focusBtn.Margin = new Thickness(0, 0, 6, 0);
            focusBtn.Click += (s, e) => FocusSelected();
            btnRow.Children.Add(focusBtn);

            var renameBtn = CreateCompactButton("Rename", BgElevated, TextPrimary, BorderSubtle);
            renameBtn.Margin = new Thickness(0, 0, 6, 0);
            renameBtn.Click += (s, e) => RenameSelected();
            btnRow.Children.Add(renameBtn);

            var tileGridBtn = CreateCompactButton("Tile Grid", BgElevated, TextPrimary, BorderSubtle);
            tileGridBtn.Margin = new Thickness(0, 0, 6, 0);
            tileGridBtn.Click += (s, e) => TileAllWindows(TileLayout.AutoGrid);
            btnRow.Children.Add(tileGridBtn);

            var tileSideBtn = CreateCompactButton("Side-by-Side", BgElevated, TextPrimary, BorderSubtle);
            tileSideBtn.Margin = new Thickness(0, 0, 6, 0);
            tileSideBtn.Click += (s, e) => TileAllWindows(TileLayout.SideBySide);
            btnRow.Children.Add(tileSideBtn);

            var killBtn = CreateCompactButton("Kill", StatusRedBg, StatusRed, StatusRed);
            killBtn.Margin = new Thickness(0, 0, 6, 0);
            killBtn.Click += async (s, e) => await KillSelectedAsync();
            btnRow.Children.Add(killBtn);

            var killAllBtn = CreateCompactButton("Kill All", BgElevated, StatusRed, BorderSubtle);
            killAllBtn.Click += async (s, e) => await KillAllAsync();
            btnRow.Children.Add(killAllBtn);

            Grid.SetColumn(btnRow, 1);
            topGrid.Children.Add(btnRow);

            Grid.SetRow(topGrid, 0);
            grid.Children.Add(topGrid);

            _instancesList = CreateDarkListView();
            _instancesList.ItemsSource = _instances;
            _instancesList.SelectionChanged += (s, e) => UpdateHandlesPaneFromSelection();
            _instancesList.MouseDoubleClick += (s, e) => FocusSelected();

            var gv = new GridView();
            gv.Columns.Add(CreateTextColumn("Account Label", "Label", 120, bold: true, mono: false));
            gv.Columns.Add(CreateTextColumn("PID", "ProcessId", 66, bold: false, mono: true));
            gv.Columns.Add(CreateRoleColumn("Role", 100));
            gv.Columns.Add(CreateStatusColumn("Singleton", 104));
            gv.Columns.Add(CreateTextColumn("Map / PlaceId", "PlaceDisplay", 168, bold: false, mono: true));
            gv.Columns.Add(CreateTextColumn("RAM (WS)", "MemoryDisplay", 80, bold: true, mono: true));
            gv.Columns.Add(CreateAudioColumn("Audio", 62));
            gv.Columns.Add(CreateTextColumn("Window Title", "WindowDisplay", 185, bold: false, mono: false));
            gv.Columns.Add(CreateTextColumn("Uptime", "UptimeDisplay", 80, bold: false, mono: true));
            _instancesList.View = gv;

            Grid.SetRow(_instancesList, 1);
            grid.Children.Add(_instancesList);

            card.Child = grid;
            return card;
        }

        private UIElement BuildHandlesCard()
        {
            var card = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Roblox Handles Inspector",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary
            });

            _handlesHeaderSub = new TextBlock
            {
                Text = "Filtered strictly to Event / Mutant handles belonging to the selected Roblox process.",
                FontSize = 11.5,
                Foreground = TextSecondary
            };
            titleStack.Children.Add(_handlesHeaderSub);
            Grid.SetColumn(titleStack, 0);
            topGrid.Children.Add(titleStack);

            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var closeHandleBtn = CreateCompactButton("Close Selected Handle", StatusAmberBg, StatusAmber, StatusAmber);
            closeHandleBtn.Margin = new Thickness(0, 0, 6, 0);
            closeHandleBtn.Click += async (s, e) => await CloseSelectedHandleAsync();
            rightBtns.Children.Add(closeHandleBtn);

            var refreshBtn = CreateCompactButton("Refresh Now", BgElevated, TextPrimary, BorderSubtle);
            refreshBtn.Click += async (s, e) => await RefreshAsync(allowAutoActions: false);
            rightBtns.Children.Add(refreshBtn);

            Grid.SetColumn(rightBtns, 1);
            topGrid.Children.Add(rightBtns);

            Grid.SetRow(topGrid, 0);
            grid.Children.Add(topGrid);

            _handlesList = CreateDarkListView();
            _handlesList.ItemsSource = _selectedHandles;

            var gv = new GridView();
            gv.Columns.Add(CreateTextColumn("Handle", "HandleHex", 85, bold: true, mono: true));
            gv.Columns.Add(CreateTextColumn("Type", "TypeName", 75, bold: false, mono: false));
            gv.Columns.Add(CreateHandleRoleColumn("Lock Type", 115));
            gv.Columns.Add(CreateTextColumn("Object Name", "ShortName", 250, bold: true, mono: true));
            gv.Columns.Add(CreateTextColumn("Full NT Path", "FullObjectName", 430, bold: false, mono: true));
            gv.Columns.Add(CreateTextColumn("Access", "AccessHex", 90, bold: false, mono: true));
            _handlesList.View = gv;

            Grid.SetRow(_handlesList, 1);
            grid.Children.Add(_handlesList);

            card.Child = grid;
            return card;
        }

        private UIElement BuildEventWatcherTab()
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            // --- LEFT COLUMN: Discord Webhook + Anti-AFK + Live OCR Capture Preview ---
            var leftGrid = new Grid();
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Discord & Watcher
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Smart Anti-AFK
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Live Preview

            // Card A: Discord Webhook & Watcher
            var discordCard = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var dStack = new StackPanel();

            var dHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            dHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dTitle = new StackPanel();
            dTitle.Children.Add(new TextBlock
            {
                Text = "Discord Event Watcher (Windows OCR)",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary
            });
            _watcherStatusLabel = new TextBlock
            {
                Text = "Scans AFK Saver chat boxes every " + _config.ScanIntervalSeconds + "s via offline WinRT OCR (2-pass color isolation).",
                FontSize = 11,
                Foreground = TextSecondary
            };
            dTitle.Children.Add(_watcherStatusLabel);
            Grid.SetColumn(dTitle, 0);
            dHeader.Children.Add(dTitle);

            _watcherToggle = new ToggleButton
            {
                Content = "Event Watcher: OFF",
                IsChecked = false,
                Padding = new Thickness(12, 5, 12, 5),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                Background = BgElevated,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(_watcherToggle);
            _watcherToggle.Checked += (s, e) =>
            {
                _discord.ResetBaselines();
                _disconnectedScoutPids.Clear();
                _passiveFrames.Clear();
                if (_autoPeekToggle != null && _autoPeekToggle.IsChecked == true)
                    _nextAutoPeekUtc = DateTime.UtcNow.AddSeconds(5);
                UpdateWatcherVisual(true);
                Log("OK", "Event Watcher started — syncing chat baseline first, then alerting on NEW spawns every " + _config.ScanIntervalSeconds + "s.");
            };
            _watcherToggle.Unchecked += (s, e) =>
            {
                _discord.ResetBaselines();
                UpdateWatcherVisual(false);
                Log("INFO", "Event Watcher paused.");
            };
            Grid.SetColumn(_watcherToggle, 1);
            dHeader.Children.Add(_watcherToggle);
            dStack.Children.Add(dHeader);

            // Webhook URL Row
            var urlRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            urlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            urlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            urlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            _webhookBox = CreateDarkTextBox(_config.WebhookUrl, "Paste Discord Webhook URL (https://discord.com/api/webhooks/...)");
            Grid.SetColumn(_webhookBox, 0);
            urlRow.Children.Add(_webhookBox);

            _pingTextBox = CreateDarkTextBox(_config.PingText, "@everyone");
            _pingTextBox.ToolTip = "Mention text used for PING priority events (e.g. @everyone or <@USER_ID>)";
            Grid.SetColumn(_pingTextBox, 2);
            urlRow.Children.Add(_pingTextBox);
            dStack.Children.Add(urlRow);

            // Private Server Code Row (prominent Add/Save + Clear + live status badge)
            var serverPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var serverRow = new Grid();
            serverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            serverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            serverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            serverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var serverLabel = new TextBlock
            {
                Text = "Private Server:",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(serverLabel, 0);
            serverRow.Children.Add(serverLabel);

            var serverInputHost = new Grid { Margin = new Thickness(0, 0, 6, 0) };
            _privateServerBox = CreateDarkTextBox(_config.PrivateServerCode ?? "",
                "Optional private server code shown in Discord webhook alerts (letters, digits, hyphens, underscores).");
            _privateServerBox.MaxLength = 128;
            System.Windows.Automation.AutomationProperties.SetName(_privateServerBox, "Private server code");
            _privateServerPlaceholder = new TextBlock
            {
                Text = "Add private server code (e.g. 57859146) — shown in Discord webhook",
                FontSize = 11.5,
                Foreground = TextSecondary,
                Opacity = 0.65,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(9, 0, 8, 0),
                IsHitTestVisible = false,
                Visibility = string.IsNullOrEmpty(_privateServerBox.Text) ? Visibility.Visible : Visibility.Collapsed
            };
            _privateServerBox.TextChanged += (s, e) =>
            {
                _privateServerPlaceholder.Visibility = string.IsNullOrEmpty(_privateServerBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            };
            _privateServerBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyPrivateServerCode(showLog: true);
                    e.Handled = true;
                }
            };
            _privateServerBox.LostFocus += (s, e) => ApplyPrivateServerCode(showLog: false);
            serverInputHost.Children.Add(_privateServerBox);
            serverInputHost.Children.Add(_privateServerPlaceholder);
            Grid.SetColumn(serverInputHost, 1);
            serverRow.Children.Add(serverInputHost);

            var saveServerBtn = CreateCompactButton("Add / Save", AccentBlue, System.Windows.Media.Brushes.White, AccentBlue);
            saveServerBtn.Margin = new Thickness(0, 0, 6, 0);
            saveServerBtn.ToolTip = "Save private server code so it appears immediately in Discord webhook alerts";
            saveServerBtn.Click += (s, e) => ApplyPrivateServerCode(showLog: true);
            Grid.SetColumn(saveServerBtn, 2);
            serverRow.Children.Add(saveServerBtn);

            var clearServerBtn = CreateCompactButton("Clear", BgElevated, TextSecondary, BorderSubtle);
            clearServerBtn.ToolTip = "Remove private server code from Discord webhook alerts";
            clearServerBtn.Click += (s, e) =>
            {
                _privateServerBox.Text = "";
                ApplyPrivateServerCode(showLog: true);
            };
            Grid.SetColumn(clearServerBtn, 3);
            serverRow.Children.Add(clearServerBtn);

            serverPanel.Children.Add(serverRow);

            _privateServerStatusLabel = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            };
            UpdatePrivateServerStatusLabel(isError: false);
            serverPanel.Children.Add(_privateServerStatusLabel);
            dStack.Children.Add(serverPanel);

            // Options + Save/Test buttons
            var dOptRow = new Grid();
            dOptRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dOptRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var checks = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _attachScreenshotCheck = new CheckBox
            {
                Content = "Attach screenshot",
                IsChecked = _config.AttachScreenshot,
                Foreground = TextSecondary,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 12, 2)
            };
            _attachScreenshotCheck.Click += (s, e) => SaveWatcherConfig(silent: true);
            checks.Children.Add(_attachScreenshotCheck);

            _alertMapChangeCheck = new CheckBox
            {
                Content = "Alert Clover Library",
                IsChecked = _config.AlertOnMapChange,
                Foreground = TextSecondary,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 12, 2),
                ToolTip = "Send Discord alert if an AFK Scout changes PlaceId (e.g. teleported to AFK World)"
            };
            _alertMapChangeCheck.Click += (s, e) => SaveWatcherConfig(silent: true);
            checks.Children.Add(_alertMapChangeCheck);

            _bgHoverCheck = new CheckBox
            {
                Content = "BG chat hover",
                IsChecked = _config.BackgroundChatHover,
                Foreground = TextSecondary,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 12, 2),
                ToolTip = "Send silent WM_MOUSEMOVE over chat box before each capture (no focus steal)"
            };
            _bgHoverCheck.Click += (s, e) => SaveWatcherConfig(silent: true);
            checks.Children.Add(_bgHoverCheck);

            _wakeChatPulseCheck = new CheckBox
            {
                Content = "Wake chat (/+Enter)",
                IsChecked = _config.WakeChatOnAntiAfk,
                Foreground = TextSecondary,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 12, 2),
                ToolTip = "Tap / then Enter after Anti-AFK pulse to un-fade dark chat history without sending any message"
            };
            _wakeChatPulseCheck.Click += (s, e) => SaveWatcherConfig(silent: true);
            checks.Children.Add(_wakeChatPulseCheck);

            _watchMainTooCheck = new CheckBox
            {
                Content = "Also watch MAIN",
                IsChecked = _config.WatchMainToo,
                Foreground = TextSecondary,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 8, 2),
                ToolTip = "Scan chat on MAIN as well as AFK Saver instances."
            };
            _watchMainTooCheck.Click += (s, e) => SaveWatcherConfig(silent: true);
            checks.Children.Add(_watchMainTooCheck);
            Grid.SetColumn(checks, 0);
            dOptRow.Children.Add(checks);

            var dBtns = new StackPanel { Orientation = Orientation.Horizontal };
            var saveCfgBtn = CreateCompactButton("Save Config", BgElevated, TextPrimary, BorderSubtle);
            saveCfgBtn.Margin = new Thickness(0, 0, 6, 0);
            saveCfgBtn.Click += (s, e) => SaveWatcherConfig(silent: false);
            dBtns.Children.Add(saveCfgBtn);

            var testHookBtn = CreateCompactButton("Test Webhook", AccentBlue, System.Windows.Media.Brushes.White, AccentBlue);
            testHookBtn.Click += async (s, e) => await TestWebhookAsync();
            dBtns.Children.Add(testHookBtn);

            Grid.SetColumn(dBtns, 1);
            dOptRow.Children.Add(dBtns);
            dStack.Children.Add(dOptRow);

            // Smart Auto-Peek row (defeats Roblox minimize / background frame freeze & chat fade-out!)
            var peekRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            peekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            peekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var peekNote = new TextBlock
            {
                Text = "Smart Auto-Peek helps recover unreadable scout chat after 5 seconds of PC inactivity.",
                FontSize = 11,
                Foreground = TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(peekNote, 0);
            peekRow.Children.Add(peekNote);

            var peekControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _autoPeekIntervalCombo = new ComboBox
            {
                Width = 66,
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 11.5,
                ToolTip = "Minimum interval between Auto-Peek attempts on unreadable scouts"
            };
            int[] peekSecs = new[] { 30, 45, 60, 90 };
            foreach (int sec in peekSecs)
                _autoPeekIntervalCombo.Items.Add(sec + "s");
            int pIdx = Array.IndexOf(peekSecs, _config.AutoPeekIntervalSeconds);
            _autoPeekIntervalCombo.SelectedIndex = pIdx >= 0 ? pIdx : 1;
            _autoPeekIntervalCombo.SelectionChanged += (s, e) =>
            {
                _config.AutoPeekIntervalSeconds = ParseSelectedPeekSeconds();
                _nextAutoPeekUtc = DateTime.UtcNow.AddSeconds(10);
                SaveWatcherConfig(silent: true);
            };
            peekControls.Children.Add(_autoPeekIntervalCombo);

            _autoPeekToggle = new ToggleButton
            {
                Content = _config.AutoPeekEnabled ? "Auto-Peek: ON" : "Auto-Peek: OFF",
                IsChecked = _config.AutoPeekEnabled,
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                Background = BgElevated,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(_autoPeekToggle);
            UpdateAutoPeekVisual(_config.AutoPeekEnabled);
            _autoPeekToggle.Checked += (s, e) =>
            {
                _config.AutoPeekEnabled = true;
                _nextAutoPeekUtc = DateTime.UtcNow.AddSeconds(8);
                UpdateAutoPeekVisual(true);
                SaveWatcherConfig(silent: true);
                Log("OK", "Smart Auto-Peek enabled (every " + _config.AutoPeekIntervalSeconds + "s for AFK scouts when you pause input).");
            };
            _autoPeekToggle.Unchecked += (s, e) =>
            {
                _config.AutoPeekEnabled = false;
                UpdateAutoPeekVisual(false);
                SaveWatcherConfig(silent: true);
                Log("INFO", "Smart Auto-Peek disabled.");
            };
            peekControls.Children.Add(_autoPeekToggle);

            Grid.SetColumn(peekControls, 1);
            peekRow.Children.Add(peekControls);
            dStack.Children.Add(peekRow);

            discordCard.Child = dStack;
            Grid.SetRow(discordCard, 0);
            leftGrid.Children.Add(discordCard);

            // Card B: Smart Anti-AFK Keep-Alive
            var afkCard = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var aGrid = new Grid();
            aGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            aGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var aInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            aInfo.Children.Add(new TextBlock
            {
                Text = "Smart Anti-AFK Keep-Alive (Clover Library + 20m Kick)",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary
            });
            _antiAfkCountdownLabel = new TextBlock
            {
                Text = "Automatic pulses follow the settings for each role.",
                FontSize = 11,
                Foreground = TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            aInfo.Children.Add(_antiAfkCountdownLabel);

            var afkExtraWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _resetCharAfkCheck = new CheckBox
            {
                Content = "Reset scout character (Esc+R+Enter)",
                IsChecked = _config.ResetCharOnAntiAfk,
                Foreground = TextSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 12, 2),
                ToolTip = "Optional: also triggers character reset on pulse (use only if scout stands in a safezone)"
            };
            _resetCharAfkCheck.Click += (s, e) => SaveWatcherConfig(silent: true);
            afkExtraWrap.Children.Add(_resetCharAfkCheck);

            _antiAfkMainIdleCheck = new CheckBox
            {
                Content = "MAIN Anti-AFK (PC idle ≥ 2m)",
                IsChecked = _config.AntiAfkMainWhenIdle,
                Foreground = TextSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 8, 2),
                ToolTip = "Run Anti-AFK for MAIN after 2 minutes of PC inactivity. Works independently of Scout Anti-AFK."
            };
            _antiAfkMainIdleCheck.Checked += (s, e) => SetMainAntiAfk(true);
            _antiAfkMainIdleCheck.Unchecked += (s, e) => SetMainAntiAfk(false);
            afkExtraWrap.Children.Add(_antiAfkMainIdleCheck);
            aInfo.Children.Add(afkExtraWrap);
            Grid.SetColumn(aInfo, 0);
            aGrid.Children.Add(aInfo);

            var aControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _antiAfkIntervalCombo = new ComboBox
            {
                Width = 74,
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 11.5,
                ToolTip = "Pulse interval for MAIN and Scout Anti-AFK"
            };
            int[] intervalOptions = new[] { 5, 7, 8, 10, 12 };
            foreach (int mins in intervalOptions)
                _antiAfkIntervalCombo.Items.Add(mins + " min");
            int selIdx = Array.IndexOf(intervalOptions, _config.AntiAfkIntervalMinutes);
            _antiAfkIntervalCombo.SelectedIndex = selIdx >= 0 ? selIdx : 1;
            _antiAfkIntervalCombo.SelectionChanged += (s, e) =>
            {
                int mins = ParseSelectedAntiAfkMinutes();
                _config.AntiAfkIntervalMinutes = mins;
                _nextScoutAntiAfkUtc = _nextMainAntiAfkUtc = DateTime.UtcNow.AddSeconds(20);
                SaveWatcherConfig(silent: true);
            };
            aControls.Children.Add(_antiAfkIntervalCombo);

            _antiAfkToggle = new ToggleButton
            {
                Content = _config.AntiAfkEnabled ? "Scout Anti-AFK: ON" : "Scout Anti-AFK: OFF",
                IsChecked = _config.AntiAfkEnabled,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                Background = BgElevated,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(_antiAfkToggle);
            UpdateAntiAfkVisual(_config.AntiAfkEnabled);
            _antiAfkToggle.Checked += (s, e) =>
            {
                _config.AntiAfkEnabled = true;
                // First pulse fires in 15s (not 7-10m later!) so already-idle accounts don't get sent to Clover Library
                _nextScoutAntiAfkUtc = DateTime.UtcNow.AddSeconds(15);
                UpdateAntiAfkVisual(true);
                SaveWatcherConfig(silent: true);
                Log("OK", "Scout Anti-AFK enabled (first pulse due in 15s, then every " + _config.AntiAfkIntervalMinutes + " min).");
            };
            _antiAfkToggle.Unchecked += (s, e) =>
            {
                _config.AntiAfkEnabled = false;
                UpdateAntiAfkVisual(false);
                SaveWatcherConfig(silent: true);
                Log("INFO", "Scout Anti-AFK disabled.");
            };
            aControls.Children.Add(_antiAfkToggle);

            var pulseNowBtn = CreateCompactButton("Test Pulse", BgElevated, TextPrimary, BorderSubtle);
            pulseNowBtn.ToolTip = "Test a quick W/S + Space keep-alive pulse on AFK Saver instances right now";
            pulseNowBtn.Click += async (s, e) => await RunAntiAfkPulseAsync(respectUserBusy: false, includeScouts: true, includeMain: false);
            aControls.Children.Add(pulseNowBtn);

            Grid.SetColumn(aControls, 1);
            aGrid.Children.Add(aControls);

            afkCard.Child = aGrid;
            Grid.SetRow(afkCard, 1);
            leftGrid.Children.Add(afkCard);

            // Card C: Live Chat Capture & OCR Diagnostic Preview
            var previewCard = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                MinHeight = 195
            };
            var pGrid = new Grid();
            pGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var pHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            pHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pTitles = new StackPanel();
            pTitles.Children.Add(new TextBlock
            {
                Text = "Live Chat Capture & OCR Test",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary
            });
            _previewDiagText = new TextBlock
            {
                Text = "Click 'Test Capture' to verify PrintWindow + WinRT OCR on your selected Roblox window.",
                FontSize = 11,
                Foreground = TextSecondary,
                TextWrapping = TextWrapping.Wrap
            };
            pTitles.Children.Add(_previewDiagText);
            Grid.SetColumn(pTitles, 0);
            pHeader.Children.Add(pTitles);

            var capBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var wakeCapBtn = CreateCompactButton("Wake & Capture", PurpleBg, PurpleAccent, PurpleAccent);
            wakeCapBtn.Margin = new Thickness(0, 0, 6, 0);
            wakeCapBtn.ToolTip = "Briefly taps / -> Enter to wake faded Roblox chat box, then captures & runs OCR";
            wakeCapBtn.Click += async (s, e) => await TestCaptureSelectedAsync(wakeChatFirst: true);
            capBtns.Children.Add(wakeCapBtn);

            var testCapBtn = CreateCompactButton("Test Capture", TealBg, TealAccent, TealAccent);
            testCapBtn.Click += async (s, e) => await TestCaptureSelectedAsync(wakeChatFirst: false);
            capBtns.Children.Add(testCapBtn);

            Grid.SetColumn(capBtns, 1);
            pHeader.Children.Add(capBtns);

            Grid.SetRow(pHeader, 0);
            pGrid.Children.Add(pHeader);

            var pSplit = new Grid();
            pSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            pSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            pSplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            var imgBox = new Border
            {
                Background = BgCanvas,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                MinHeight = 120
            };
            _previewImage = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform
            };
            imgBox.Child = _previewImage;
            Grid.SetColumn(imgBox, 0);
            pSplit.Children.Add(imgBox);

            _previewOcrBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 120,
                MaxHeight = 240,
                Background = BgCanvas,
                Foreground = TextPrimary,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11.5,
                Padding = new Thickness(8),
                Text = "(OCR output will appear here after Test Capture)"
            };
            Grid.SetColumn(_previewOcrBox, 2);
            pSplit.Children.Add(_previewOcrBox);

            Grid.SetRow(pSplit, 1);
            pGrid.Children.Add(pSplit);

            previewCard.Child = pGrid;
            Grid.SetRow(previewCard, 2);
            leftGrid.Children.Add(previewCard);

            var leftScroll = new ScrollViewer
            {
                Content = leftGrid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Padding = new Thickness(0, 0, 4, 0)
            };
            Grid.SetColumn(leftScroll, 0);
            grid.Children.Add(leftScroll);

            // --- RIGHT COLUMN: Clover Retribution 18-Rule Priority Matrix ---
            var rightCard = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10)
            };
            var rGrid = new Grid();
            rGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var rHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            rHeader.Children.Add(new TextBlock
            {
                Text = "Clover Retribution Event Rules (Click Alert Level to Cycle)",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary
            });
            rHeader.Children.Add(new TextBlock
            {
                Text = "PING = Discord mention + embed · NORMAL = embed only · IGNORE = skip",
                FontSize = 11,
                Foreground = TextSecondary
            });
            Grid.SetRow(rHeader, 0);
            rGrid.Children.Add(rHeader);

            var rulesList = CreateDarkListView();
            rulesList.ItemsSource = _ruleRows;
            rulesList.MouseLeftButtonUp += (s, e) =>
            {
                var row = rulesList.SelectedItem as EventRuleRowViewModel;
                if (row == null) return;
                CycleRuleLevel(row);
                rulesList.Items.Refresh();
            };

            var rgv = new GridView();
            rgv.Columns.Add(CreateAlertLevelColumn("Alert", 86));
            rgv.Columns.Add(CreateTextColumn("Event Name", "DisplayName", 185, bold: true, mono: false));
            rgv.Columns.Add(CreateTextColumn("Trigger / Chat Hint", "Hint", 255, bold: false, mono: false));
            rulesList.View = rgv;

            Grid.SetRow(rulesList, 1);
            rGrid.Children.Add(rulesList);

            rightCard.Child = rGrid;
            Grid.SetColumn(rightCard, 2);
            grid.Children.Add(rightCard);

            return grid;
        }

        private UIElement BuildLogCard()
        {
            var card = new Border
            {
                Background = BgSurface,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 7, 12, 7)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new TextBlock
            {
                Text = "ACTIVITY LOG",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextSecondary,
                Margin = new Thickness(0, 0, 0, 3)
            };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            _logList = new ListBox
            {
                ItemsSource = _activityLog,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = TextSecondary,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11.5
            };
            Grid.SetRow(_logList, 1);
            grid.Children.Add(_logList);

            card.Child = grid;
            return card;
        }

        private async Task RefreshAsync(bool allowAutoActions)
        {
            if (_isScanning) return;
            _isScanning = true;

            try
            {
                bool includeMutex = _includeMutexCheck.IsChecked == true;
                bool autoUnlock = allowAutoActions && (_autoUnlockToggle.IsChecked == true);
                bool autoAfkNew = allowAutoActions && (_autoAfkCheck.IsChecked == true);

                var labelCopy = new Dictionary<int, string>(_customLabels);
                var roleCopy = new Dictionary<int, InstanceRole>(_instanceRoles);

                var scanned = await Task.Run(() => _engine.ScanRobloxInstances(labelCopy, roleCopy, includeMutex));
                bool needsRescan = false;

                bool hasMain = scanned.Any(x => _instanceRoles.ContainsKey(x.ProcessId) && _instanceRoles[x.ProcessId] == InstanceRole.Main);
                foreach (var inst in scanned)
                {
                    if (!_instanceRoles.ContainsKey(inst.ProcessId))
                    {
                        if (!hasMain)
                        {
                            _instanceRoles[inst.ProcessId] = InstanceRole.Main;
                            inst.Role = InstanceRole.Main;
                            hasMain = true;
                        }
                        else if (autoAfkNew)
                        {
                            _instanceRoles[inst.ProcessId] = InstanceRole.AfkSaver;
                            inst.Role = InstanceRole.AfkSaver;
                        }
                        else
                        {
                            _instanceRoles[inst.ProcessId] = InstanceRole.Main;
                            inst.Role = InstanceRole.Main;
                        }
                    }
                    else
                    {
                        inst.Role = _instanceRoles[inst.ProcessId];
                    }
                }

                foreach (var inst in scanned.Where(x => x.Role == InstanceRole.AfkSaver).ToList())
                {
                    if (!_appliedAfkPids.Contains(inst.ProcessId))
                    {
                        var applyRes = await Task.Run(() =>
                            AfkOptimizer.ApplyRoleProfile(inst.ProcessId, inst.WindowHandle, InstanceRole.AfkSaver, resizeWindow: true));
                        _appliedAfkPids.Add(inst.ProcessId);
                        needsRescan = true;
                        LogAfkApplyResult(inst.Label, applyRes);
                    }
                    else if (!inst.IsAudioMuted)
                    {
                        string muteErr = null;
                        bool mutedNow = await Task.Run(() => AfkOptimizer.SetProcessAudioMute(inst.ProcessId, true, out muteErr));
                        if (mutedNow)
                        {
                            inst.IsAudioMuted = true;
                            Log("OK", "Muted late audio session for AFK instance PID " + inst.ProcessId + ".");
                        }
                    }
                }

                if (autoUnlock)
                {
                    var lockedPids = scanned
                        .Where(i => i.State == SingletonState.Locked)
                        .Select(i => i.ProcessId)
                        .ToList();

                    foreach (int pid in lockedPids)
                    {
                        UnlockResult res = await Task.Run(() => _engine.UnlockInstance(pid, true, includeMutex));
                        if (res.ClosedCount > 0)
                        {
                            needsRescan = true;
                            Log("OK", string.Format("Auto-unlocked PID {0}: closed {1}", pid, string.Join(", ", res.ClosedHandleNames)));
                            if (!IsVisible && _trayIcon != null)
                            {
                                _trayIcon.ShowBalloonTip(2000, "RoMulti — Unlocked", "Unlocked Roblox PID " + pid + " for multi-instance.", Forms.ToolTipIcon.Info);
                            }
                        }
                        else if (!string.IsNullOrEmpty(res.ErrorMessage))
                        {
                            Log("WARN", res.ErrorMessage);
                        }
                    }
                }

                if (allowAutoActions && (DateTime.UtcNow - _lastPeriodicTrimUtc).TotalSeconds >= PeriodicTrimIntervalSeconds)
                {
                    _lastPeriodicTrimUtc = DateTime.UtcNow;
                    var afkTargets = scanned.Where(x => x.Role == InstanceRole.AfkSaver).ToList();
                    if (afkTargets.Count > 0)
                    {
                        double savedMb = 0;
                        int trimmedCount = 0;
                        foreach (var inst in afkTargets)
                        {
                            double b = 0, a = 0;
                            string err = null;
                            bool ok = await Task.Run(() => AfkOptimizer.TrimWorkingSet(inst.ProcessId, out b, out a, out err));
                            if (ok)
                            {
                                trimmedCount++;
                                savedMb += Math.Max(0, b - a);
                            }
                        }
                        if (trimmedCount > 0)
                        {
                            needsRescan = true;
                            Log("INFO", string.Format("Periodic AFK RAM trim: {0} scout(s) trimmed ({1:0} MB freed).", trimmedCount, savedMb));
                        }
                    }

                    if (!IsVisible)
                        AfkOptimizer.TrimSelfWorkingSet();
                }

                if (needsRescan)
                {
                    var updatedRoles = new Dictionary<int, InstanceRole>(_instanceRoles);
                    scanned = await Task.Run(() => _engine.ScanRobloxInstances(labelCopy, updatedRoles, includeMutex));
                }

                // Populate live PlaceId from Roblox logs for display (without consuming watcher's map-change tracker)
                await Task.Run(() =>
                {
                    foreach (var inst in scanned)
                    {
                        string unusedPrev;
                        var srv = _logTracker.GetServerInfoForInstance(inst.ProcessId, inst.StartTime, out unusedPrev, updateTrackedPlace: false);
                        if (srv != null && !string.IsNullOrEmpty(srv.PlaceId))
                            inst.PlaceId = srv.PlaceId;
                    }
                });

                var sysMem = await Task.Run(() => AfkOptimizer.GetSystemMemoryStats());
                ApplyScanToUi(scanned, sysMem);

                // Update Anti-AFK countdown & trigger if due
                await CheckAndRunAntiAfkIfDueAsync(allowAutoActions);
            }
            catch (Exception ex)
            {
                Log("ERR", "Scan error: " + ex.Message);
            }
            finally
            {
                _isScanning = false;
            }
        }

        private async Task RunWatcherTickAsync()
        {
            if (_isWatching || _watcherToggle.IsChecked != true) return;
            _isWatching = true;

            try
            {
                bool watchMainToo = _watchMainTooCheck.IsChecked == true;
                var watchTargets = _instances
                    .Where(x => x.WindowHandle != IntPtr.Zero && (watchMainToo || x.Role == InstanceRole.AfkSaver))
                    .ToList();
                if (watchTargets.Count == 0)
                {
                    _watcherStatusLabel.Text = "Watcher ON — waiting for a visible Roblox instance (tag as AFK SAVER or check 'Also watch MAIN').";
                    return;
                }

                string webhookUrl = _webhookBox.Text.Trim();
                string pingText = _pingTextBox.Text.Trim();
                string privateServerCode = GetActivePrivateServerCode();
                bool attachImg = _attachScreenshotCheck.IsChecked == true;
                bool alertMap = _alertMapChangeCheck.IsChecked == true;

                int scannedCount = 0;
                int blackFrames = 0;
                int unavailable = 0;
                int unreadable = 0;
                int unchangedFrames = 0;
                int disconnected = 0;
                int inLibrary = 0;
                bool autoPeekEnabled = _autoPeekToggle.IsChecked == true;
                var needsPeek = new List<RobloxInstanceInfo>();
                var sessions = new Dictionary<int, RobloxServerInfo>();

                foreach (var scout in watchTargets)
                {
                    // 1. Check Roblox log for Server PlaceId / JobId & Map Change (Clover Library teleport)
                    string prevPlace = null;
                    var serverInfo = await Task.Run(() => _logTracker.GetServerInfoForInstance(scout.ProcessId, scout.StartTime, out prevPlace));
                    sessions[scout.ProcessId] = serverInfo;
                    if (serverInfo != null && serverInfo.IsDisconnected)
                    {
                        disconnected++;
                        _passiveFrames.Remove(scout.ProcessId);
                        if (_disconnectedScoutPids.Add(scout.ProcessId))
                            Log("WARN", scout.Label + " disconnected. Rejoin to resume event monitoring.");
                        continue;
                    }
                    if (serverInfo != null && serverInfo.HasServer)
                        _disconnectedScoutPids.Remove(scout.ProcessId);

                    bool isInLibrary = serverInfo != null && serverInfo.PlaceId == RobloxServerInfo.CloverLibraryPlaceId;
                    if (alertMap && scout.Role == InstanceRole.AfkSaver && !string.IsNullOrEmpty(prevPlace) && serverInfo != null)
                    {
                        Log("WARN", string.Format("Map change on {0} (PID {1}): Place {2} -> {3}.",
                            scout.Label, scout.ProcessId, prevPlace, serverInfo.PlaceId));
                        if (!string.IsNullOrWhiteSpace(webhookUrl))
                        {
                            QueueWatcherDelivery(async () =>
                            {
                                var result = await _discord.SendMapChangeAlertAsync(webhookUrl, pingText, scout, prevPlace,
                                    serverInfo, privateServerCode);
                                if (!result.Sent && !result.SkippedAsDuplicate)
                                    Log("ERR", "Discord map alert failed: " + result.Message);
                            });
                        }
                    }
                    if (isInLibrary)
                    {
                        inLibrary++;
                        _passiveFrames.Remove(scout.ProcessId);
                        continue;
                    }

                    // 2. Capture & OCR chat region on background thread (PlaceId-aware: captures ONLY the map's chat box!)
                    IntPtr scoutHwnd = scout.WindowHandle;
                    bool isCloaked = NativeMethods.IsWindowCloaked(scoutHwnd);
                    bool canPeek = scout.Role == InstanceRole.AfkSaver && autoPeekEnabled && !isCloaked;
                    bool bgHover = !isCloaked && _bgHoverCheck.IsChecked == true;
                    string activePlaceId = (serverInfo != null && !string.IsNullOrEmpty(serverInfo.PlaceId)) ? serverInfo.PlaceId : scout.PlaceId;
                    var cap = await Task.Run(() => ChatOcrEngine.CaptureAndReadWindowAsync(scoutHwnd, bgHover, activePlaceId));
                    if (!cap.Captured)
                    {
                        unavailable++;
                        if (canPeek) needsPeek.Add(scout);
                        continue;
                    }
                    if (cap.LooksBlack)
                    {
                        blackFrames++;
                        if (canPeek) needsPeek.Add(scout);
                        if ((DateTime.UtcNow - _lastBlackFrameLogUtc).TotalSeconds >= 60)
                        {
                            _lastBlackFrameLogUtc = DateTime.UtcNow;
                            Log("WARN", string.Format("Black frame captured on {0} (PID {1})! Uncover part of its window on your monitor so OCR can read chat.",
                                scout.Label, scout.ProcessId));
                        }
                        continue;
                    }

                    PassiveFrameHistory frameHistory;
                    if (!_passiveFrames.TryGetValue(scout.ProcessId, out frameHistory) ||
                        !string.IsNullOrEmpty(prevPlace))
                    {
                        frameHistory = new PassiveFrameHistory();
                        _passiveFrames[scout.ProcessId] = frameHistory;
                    }
                    if (frameHistory.IsUnchanged(cap.RawCropPng, DateTime.UtcNow))
                    {
                        unchangedFrames++;
                        if (canPeek) needsPeek.Add(scout);
                        continue;
                    }

                    if (ProcessWatcherCapture(cap, scout, serverInfo, canPeek, webhookUrl, pingText, attachImg))
                        scannedCount++;
                    else
                    {
                        unreadable++;
                        if (canPeek) needsPeek.Add(scout);
                    }
                }

                // 3. Only recover unreadable chat when the desktop is idle.
                if (_watcherToggle.IsChecked == true && _autoPeekToggle.IsChecked == true && needsPeek.Count > 0 &&
                    CanStartScheduledFocus(DateTime.UtcNow, _nextAutoPeekUtc, _isChangingFocus,
                        AntiAfkKeeper.IsUserActivelyHoldingInput(AutomaticFocusIdleMs)))
                {
                    SmartPeekBatchResult peekBatch;
                    _isChangingFocus = true;
                    try
                    {
                        peekBatch = await Task.Run(() => AntiAfkKeeper.PeekAndCaptureScouts(
                            needsPeek.Where(x => !NativeMethods.IsWindowCloaked(x.WindowHandle)).ToList(),
                            respectActiveUser: true));
                    }
                    finally
                    {
                        _isChangingFocus = false;
                    }
                    using (var batch = peekBatch)
                    {
                        if (!batch.DeferredBecauseUserBusy)
                        {
                            _nextAutoPeekUtc = DateTime.UtcNow.AddSeconds(ParseSelectedPeekSeconds());

                            foreach (var item in batch.Items)
                            {
                                var peekCap = await ChatOcrEngine.RecognizeFromRawCropsAsync(item.RawCrop);
                                if (!peekCap.Captured || peekCap.LooksBlack) continue;
                                if (ProcessWatcherCapture(peekCap, item.Instance, sessions[item.Instance.ProcessId],
                                    true, webhookUrl, pingText, attachImg))
                                {
                                    scannedCount++;
                                    Log("INFO", "Auto-Peek recovered readable chat for " + item.Instance.Label + ".");
                                }
                            }
                        }
                    }
                }

                var status = new List<string> { scannedCount + " readable" };
                if (blackFrames > 0) status.Add(blackFrames + " black frame(s)");
                if (unavailable > 0) status.Add(unavailable + " capture unavailable");
                if (unreadable > 0) status.Add(unreadable + " passive chat unreadable");
                if (unchangedFrames > 0) status.Add(unchangedFrames + " unchanged frame(s)");
                if (disconnected > 0) status.Add(disconnected + " disconnected");
                if (inLibrary > 0) status.Add(inLibrary + " in Clover Library");
                if (needsPeek.Count > 0 && DateTime.UtcNow >= _nextAutoPeekUtc)
                    status.Add("Auto-Peek waiting for idle");
                _watcherStatusLabel.Text = "Watcher ON — " + string.Join(" · ", status);
            }
            catch (Exception ex)
            {
                Log("ERR", "Watcher tick error: " + ex.Message);
            }
            finally
            {
                _isWatching = false;
            }
        }

        internal static bool CanStartScheduledFocus(DateTime nowUtc, DateTime dueUtc, bool focusInUse, bool userBusy)
        {
            return nowUtc >= dueUtc && !focusInUse && !userBusy;
        }

        private bool ProcessWatcherCapture(ChatCaptureResult capture, RobloxInstanceInfo scout, RobloxServerInfo session,
            bool waitForPeek, string webhookUrl, string pingText, bool attachImg)
        {
            List<string> timeline;
            var matches = EventCatalog.MatchAllOccurrences(capture.OcrLines, _config, out timeline);
            bool baselineInitialized;
            int baselineCount;
            var newMatches = _discord.FilterNewOccurrencesForPid(
                scout.ProcessId, session != null ? session.JobId : null, timeline, matches,
                waitForPeek, out baselineInitialized, out baselineCount);
            if (baselineInitialized)
                Log("INFO", string.Format("Baseline synced for {0}: {1} existing event(s) ignored.", scout.Label, baselineCount));
            else if (newMatches.Count > 0)
            {
                string privateServerCode = GetActivePrivateServerCode();
                QueueWatcherDelivery(() => DispatchMatchedEventsAsync(
                    newMatches, scout, session, capture, webhookUrl, pingText, attachImg, privateServerCode));
            }

            return DiscordNotifier.HasReadableChatMessages(timeline, matches);
        }

        private void QueueWatcherDelivery(Func<Task> deliver)
        {
            _watcherDelivery = DeliverAfterAsync(_watcherDelivery, deliver);
        }

        private async Task DeliverAfterAsync(Task previous, Func<Task> deliver)
        {
            try
            {
                await previous;
                await deliver();
            }
            catch (Exception ex)
            {
                Log("ERR", "Watcher notification failed: " + ex.Message);
            }
        }

        private async Task DispatchMatchedEventsAsync(
            List<MatchedEvent> matches,
            RobloxInstanceInfo scout,
            RobloxServerInfo serverInfo,
            ChatCaptureResult captureResult,
            string webhookUrl,
            string pingText,
            bool attachImg,
            string privateServerCode)
        {
            foreach (var m in matches)
            {
                if (m.EffectiveLevel == EventAlertLevel.Ignore) continue;

                string jobId = serverInfo != null ? serverInfo.JobId : null;
                if (_discord.IsDuplicateOnSameServerAndRecord(jobId, m.DedupeKey, scout.ProcessId, recordIfNew: false))
                {
                    Log("INFO", string.Format("Cross-PID same-server duplicate suppressed: {0} on {1}.",
                        m.Rule.DisplayName, scout.Label));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(webhookUrl))
                {
                    _discord.IsDuplicateOnSameServerAndRecord(jobId, m.DedupeKey, scout.ProcessId, recordIfNew: true);
                    Log("OK", string.Format("[{0}] {1} on {2} (no webhook set): {3}",
                        m.EffectiveLevel.ToString().ToUpperInvariant(), m.Rule.DisplayName, scout.Label, m.RawLine));
                    if (_trayIcon != null)
                        _trayIcon.ShowBalloonTip(4000, "RoMulti — " + m.Rule.DisplayName, scout.Label + ": " + m.RawLine, Forms.ToolTipIcon.Info);
                    continue;
                }

                // Crop ONLY the exact tight event text box (2x crisp upscale if small) instead of whole wall + hotbar!
                byte[] tightEventPng = attachImg
                    ? ChatOcrEngine.CropEventBoxFromCapture(captureResult, m.SourceLineIndices)
                    : null;

                var sendRes = await _discord.SendEventAlertAsync(
                    webhookUrl,
                    pingText,
                    m,
                    scout,
                    serverInfo,
                    tightEventPng,
                    skipTimeDedupe: true,
                    privateServerCode: privateServerCode);

                if (sendRes.Sent)
                {
                    _discord.IsDuplicateOnSameServerAndRecord(jobId, m.DedupeKey, scout.ProcessId, recordIfNew: true);
                    Log("OK", string.Format("Discord alert sent [{0}]: {1} on {2} -> {3}",
                        m.EffectiveLevel.ToString().ToUpperInvariant(), m.Rule.DisplayName, scout.Label, m.RawLine));
                    if (_trayIcon != null)
                        _trayIcon.ShowBalloonTip(4000, "RoMulti — " + m.Rule.DisplayName, "Sent to Discord (" + scout.Label + ")", Forms.ToolTipIcon.Info);
                }
                else if (!sendRes.SkippedAsDuplicate)
                {
                    Log("ERR", "Discord webhook failed: " + sendRes.Message);
                }
            }
        }

        private async Task CheckAndRunAntiAfkIfDueAsync(bool allowAutoActions)
        {
            bool scoutsEnabled = _antiAfkToggle.IsChecked == true;
            bool mainEnabled = _antiAfkMainIdleCheck.IsChecked == true;
            if (!scoutsEnabled && !mainEnabled)
            {
                _antiAfkCountdownLabel.Text = "MAIN and Scout Anti-AFK are off.";
                return;
            }

            DateTime now = DateTime.UtcNow;
            uint pcIdleMs = AntiAfkKeeper.GetSystemIdleMs();
            bool scoutDue = IsAntiAfkDue(scoutsEnabled, now, _nextScoutAntiAfkUtc, pcIdleMs, AutomaticFocusIdleMs);
            bool mainDue = IsAntiAfkDue(mainEnabled, now, _nextMainAntiAfkUtc, pcIdleMs, AntiAfkKeeper.MainIdleMs);
            var statuses = new List<string>(2);
            if (mainEnabled)
                statuses.Add(FormatAntiAfkStatus("MAIN", _nextMainAntiAfkUtc - now, "PC idle for 2m"));
            if (scoutsEnabled)
                statuses.Add(FormatAntiAfkStatus("Scout", _nextScoutAntiAfkUtc - now, "PC idle for 5s"));
            _antiAfkCountdownLabel.Text = string.Join(" · ", statuses);

            if (!allowAutoActions || (!scoutDue && !mainDue) || _isChangingFocus ||
                AntiAfkKeeper.IsUserActivelyHoldingInput(0))
                return;

            await RunAntiAfkPulseAsync(respectUserBusy: true, includeScouts: scoutDue, includeMain: mainDue);
        }

        internal static bool IsAntiAfkDue(bool enabled, DateTime nowUtc, DateTime dueUtc, uint pcIdleMs, int minimumIdleMs)
        {
            return enabled && nowUtc >= dueUtc && pcIdleMs >= (uint)minimumIdleMs;
        }

        private static string FormatAntiAfkStatus(string label, TimeSpan remaining, string idleRequirement)
        {
            if (remaining > TimeSpan.Zero)
                return string.Format("{0} next in {1}m {2:00}s", label, (int)remaining.TotalMinutes, remaining.Seconds);
            return label + " pending (" + idleRequirement + ")";
        }

        internal static List<RobloxInstanceInfo> SelectAntiAfkTargets(IEnumerable<RobloxInstanceInfo> instances,
            bool includeScouts, bool includeMain, bool requireActiveDesktop)
        {
            return instances.Where(x => x.WindowHandle != IntPtr.Zero &&
                    ((includeMain && x.Role == InstanceRole.Main) || (includeScouts && x.Role == InstanceRole.AfkSaver)) &&
                    (!requireActiveDesktop || !NativeMethods.IsWindowCloaked(x.WindowHandle)))
                .OrderBy(x => x.Role == InstanceRole.Main ? 0 : 1)
                .ToList();
        }

        private async Task RunAntiAfkPulseAsync(bool respectUserBusy, bool includeScouts, bool includeMain)
        {
            if (_isChangingFocus) return;
            _isChangingFocus = true;
            try
            {
                var targets = SelectAntiAfkTargets(_instances, includeScouts, includeMain, respectUserBusy);
                if (targets.Count == 0)
                {
                    if (!respectUserBusy)
                        Log("WARN", "No AFK Saver instances with visible windows to pulse.");
                }
                else
                {
                    bool wakeChat = _wakeChatPulseCheck.IsChecked == true;
                    bool resetChar = _resetCharAfkCheck.IsChecked == true;
                    bool hasMain = targets.Any(x => x.Role == InstanceRole.Main);
                    var pulseRes = await Task.Run(() => AntiAfkKeeper.ExecuteQuickPulse(targets, respectUserBusy, wakeChat, resetChar, hasMain));
                    if (pulseRes.DeferredBecauseUserBusy)
                    {
                        _antiAfkCountdownLabel.Text = "Anti-AFK pending (PC in use).";
                        return;
                    }

                    Log(pulseRes.FocusFailedCount > 0 ? "WARN" : "OK", pulseRes.Summary);
                }

                int intervalMinutes = ParseSelectedAntiAfkMinutes();
                TimeSpan delay = targets.Count == 0 ? TimeSpan.FromMinutes(intervalMinutes) : AntiAfkKeeper.GetRandomizedNextDelay(intervalMinutes);
                DateTime next = DateTime.UtcNow.Add(delay);
                if (includeScouts) _nextScoutAntiAfkUtc = next;
                if (includeMain) _nextMainAntiAfkUtc = next;
            }
            catch (Exception ex)
            {
                Log("ERR", "Anti-AFK pulse error: " + ex.Message);
            }
            finally
            {
                _isChangingFocus = false;
            }
        }

        private async Task TestCaptureSelectedAsync(bool wakeChatFirst)
        {
            var target = (_instancesList.SelectedItem as RobloxInstanceInfo)
                ?? _instances.FirstOrDefault(x => x.Role == InstanceRole.AfkSaver)
                ?? _instances.FirstOrDefault();

            if (target == null)
            {
                _previewDiagText.Text = "No running Roblox instance found.";
                return;
            }

            if (wakeChatFirst)
            {
                if (_isChangingFocus)
                {
                    _previewDiagText.Text = "Another window action is running. Try again in a moment.";
                    return;
                }
                _previewDiagText.Text = "Waking chat (/ -> Enter) and capturing " + target.Label + " (PID " + target.ProcessId + ")...";
                _isChangingFocus = true;
                try
                {
                    await Task.Run(() => AntiAfkKeeper.WakeChatOnWindowOnce(target.WindowHandle));
                }
                finally
                {
                    _isChangingFocus = false;
                }
            }
            else
            {
                _previewDiagText.Text = "Capturing chat region of " + target.Label + " (PID " + target.ProcessId + ")...";
            }

            bool bgHover = _bgHoverCheck.IsChecked == true;
            var cap = await ChatOcrEngine.CaptureAndReadWindowAsync(target.WindowHandle, bgHover, target.PlaceId);
            _previewDiagText.Text = target.Label + " (PID " + target.ProcessId + "): " + cap.DiagnosticNote;

            if (cap.RawCropPng != null && cap.RawCropPng.Length > 0)
            {
                var bi = new BitmapImage();
                using (var ms = new MemoryStream(cap.RawCropPng))
                {
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                }
                _previewImage.Source = bi;
            }

            if (cap.LooksBlack)
            {
                _previewOcrBox.Text = "[BLACK FRAME] Windows D3D11 returned black because the AFK window is fully covered or minimized.\nKeep part of the AFK window uncovered on your desktop and test again.";
                Log("WARN", "Test Capture on " + target.Label + " returned a black frame.");
                return;
            }

            var matches = EventCatalog.MatchAllOccurrences(cap.OcrLines, _config);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- OCR LINES (" + cap.OcrLines.Count + ") ---");
            foreach (string line in cap.OcrLines)
                sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("--- MATCHED EVENTS (" + matches.Count + ") ---");
            foreach (var m in matches)
                sb.AppendLine(string.Format("[{0}] {1} <- {2}", m.EffectiveLevel.ToString().ToUpperInvariant(), m.Rule.DisplayName, m.RawLine));

            _previewOcrBox.Text = sb.ToString();
            Log("INFO", "Test Capture on " + target.Label + ": " + cap.OcrLines.Count + " OCR line(s), " + matches.Count + " matched event(s).");
        }

        private string GetActivePrivateServerCode()
        {
            string liveCode;
            if (_privateServerBox != null && DiscordNotifier.TryNormalizePrivateServerCode(_privateServerBox.Text, out liveCode))
                return liveCode;
            return _config.PrivateServerCode ?? "";
        }

        private void ApplyPrivateServerCode(bool showLog)
        {
            string code;
            if (!DiscordNotifier.TryNormalizePrivateServerCode(_privateServerBox.Text, out code))
            {
                _privateServerBox.BorderBrush = StatusRed;
                UpdatePrivateServerStatusLabel(isError: true);
                if (showLog) Log("WARN", "Private server code only allows letters, digits, hyphens (-), or underscores (_).");
                return;
            }

            bool changed = !string.Equals(_config.PrivateServerCode ?? "", code, StringComparison.Ordinal);
            _privateServerBox.Text = code;
            _privateServerBox.BorderBrush = BorderSubtle;
            _config.PrivateServerCode = code;
            UpdatePrivateServerStatusLabel(isError: false);

            if (changed || showLog)
            {
                SaveWatcherConfig(silent: true);
                if (showLog)
                {
                    Log("OK", string.IsNullOrEmpty(code)
                        ? "Private server code cleared (hidden from next Discord webhooks)."
                        : "Private server code saved: " + code + " (will show in next Discord webhooks).");
                }
            }
        }

        private void UpdatePrivateServerStatusLabel(bool isError)
        {
            if (_privateServerStatusLabel == null) return;
            if (isError)
            {
                _privateServerStatusLabel.Text = "Invalid code: use only letters, digits, hyphens (-), or underscores (_).";
                _privateServerStatusLabel.Foreground = StatusRed;
                return;
            }
            string active = _config.PrivateServerCode ?? "";
            if (string.IsNullOrEmpty(active))
            {
                _privateServerStatusLabel.Text = "Webhook Private server field: not set (optional).";
                _privateServerStatusLabel.Foreground = TextSecondary;
            }
            else
            {
                _privateServerStatusLabel.Text = "Active in Discord webhook -> Private server: " + active;
                _privateServerStatusLabel.Foreground = StatusGreen;
            }
        }

        private void SaveWatcherConfig(bool silent)
        {
            string privateServerCode;
            if (DiscordNotifier.TryNormalizePrivateServerCode(_privateServerBox.Text, out privateServerCode))
            {
                _config.PrivateServerCode = privateServerCode;
                _privateServerBox.BorderBrush = BorderSubtle;
                UpdatePrivateServerStatusLabel(isError: false);
            }
            else
            {
                _privateServerBox.BorderBrush = StatusRed;
                UpdatePrivateServerStatusLabel(isError: true);
                if (!silent) Log("WARN", "Private server code needs letters, digits, hyphens or underscores. Previous code kept.");
            }
            _config.WebhookUrl = _webhookBox.Text.Trim();
            _config.PingText = string.IsNullOrWhiteSpace(_pingTextBox.Text) ? "@everyone" : _pingTextBox.Text.Trim();
            _config.AttachScreenshot = _attachScreenshotCheck.IsChecked == true;
            _config.AlertOnMapChange = _alertMapChangeCheck.IsChecked == true;
            _config.BackgroundChatHover = _bgHoverCheck.IsChecked == true;
            _config.WakeChatOnAntiAfk = _wakeChatPulseCheck.IsChecked == true;
            _config.WatchMainToo = _watchMainTooCheck.IsChecked == true;
            _config.AutoPeekEnabled = _autoPeekToggle.IsChecked == true;
            _config.AutoPeekIntervalSeconds = ParseSelectedPeekSeconds();
            _config.ResetCharOnAntiAfk = _resetCharAfkCheck.IsChecked == true;
            _config.AntiAfkMainWhenIdle = _antiAfkMainIdleCheck.IsChecked == true;
            _config.AntiAfkEnabled = _antiAfkToggle.IsChecked == true;
            _config.AntiAfkIntervalMinutes = ParseSelectedAntiAfkMinutes();

            foreach (var row in _ruleRows)
                _config.SetLevel(row.Rule.Id, row.Level);

            try
            {
                _config.Save();
                if (!silent)
                    Log("OK", "Saved watcher settings to romulti-config.json.");
            }
            catch (Exception ex)
            {
                Log("ERR", "Failed to save config: " + ex.Message);
            }
        }

        private async Task TestWebhookAsync()
        {
            string privateServerCode;
            if (!DiscordNotifier.TryNormalizePrivateServerCode(_privateServerBox.Text, out privateServerCode))
            {
                Log("WARN", "Fix the private server code before sending a test message.");
                _privateServerBox.Focus();
                return;
            }
            SaveWatcherConfig(silent: true);
            Log("INFO", "Sending test message to Discord Webhook...");
            var res = await _discord.SendTestWebhookAsync(_config.WebhookUrl, _config.PingText, privateServerCode);
            Log(res.Sent ? "OK" : "ERR", res.Message);
        }

        private void CycleRuleLevel(EventRuleRowViewModel row)
        {
            if (row.Level == EventAlertLevel.Ping) row.Level = EventAlertLevel.Normal;
            else if (row.Level == EventAlertLevel.Normal) row.Level = EventAlertLevel.Ignore;
            else row.Level = EventAlertLevel.Ping;

            _config.SetLevel(row.Rule.Id, row.Level);
            SaveWatcherConfig(silent: true);
            Log("INFO", "Event rule '" + row.DisplayName + "' set to " + row.LevelDisplay + ".");
        }

        private int ParseSelectedPeekSeconds()
        {
            string s = _autoPeekIntervalCombo != null && _autoPeekIntervalCombo.SelectedItem != null
                ? _autoPeekIntervalCombo.SelectedItem.ToString()
                : "45s";
            int val;
            if (int.TryParse(s.Replace("s", "").Trim(), out val) && val >= 15 && val <= 300)
                return val;
            return 45;
        }

        private int ParseSelectedAntiAfkMinutes()
        {
            string s = _antiAfkIntervalCombo != null && _antiAfkIntervalCombo.SelectedItem != null
                ? _antiAfkIntervalCombo.SelectedItem.ToString()
                : "11 min";
            int val;
            if (int.TryParse(s.Split(' ')[0], out val) && val >= 3 && val <= 18)
                return val;
            return 11;
        }

        private void ApplyScanToUi(List<RobloxInstanceInfo> scanned, SystemMemoryStats sysMem)
        {
            int previousPid = _selectedPid;
            var curSel = _instancesList.SelectedItem as RobloxInstanceInfo;
            if (curSel != null)
                previousPid = curSel.ProcessId;

            var activePids = new HashSet<int>(scanned.Select(x => x.ProcessId));
            _disconnectedScoutPids.RemoveWhere(pid => !activePids.Contains(pid));
            foreach (int pid in _passiveFrames.Keys.Where(pid => !activePids.Contains(pid)).ToList())
                _passiveFrames.Remove(pid);
            foreach (int deadPid in _customLabels.Keys.Where(k => !activePids.Contains(k)).ToList())
                _customLabels.Remove(deadPid);
            foreach (int deadPid in _instanceRoles.Keys.Where(k => !activePids.Contains(k)).ToList())
                _instanceRoles.Remove(deadPid);
            foreach (int deadPid in _appliedAfkPids.Where(k => !activePids.Contains(k)).ToList())
                _appliedAfkPids.Remove(deadPid);
            _logTracker.PruneExitedPids(activePids);
            _discord.PruneExitedPids(activePids);

            _instances.Clear();
            foreach (var inst in scanned)
                _instances.Add(inst);

            RobloxInstanceInfo toSelect = scanned.FirstOrDefault(x => x.ProcessId == previousPid) ?? scanned.FirstOrDefault();
            if (toSelect != null)
            {
                _selectedPid = toSelect.ProcessId;
                _instancesList.SelectedItem = toSelect;
            }
            else
            {
                _selectedPid = -1;
            }

            UpdateHandlesPaneFromSelection();
            UpdateKpis(scanned, sysMem);
        }

        private void UpdateHandlesPaneFromSelection()
        {
            _selectedHandles.Clear();
            var selected = _instancesList.SelectedItem as RobloxInstanceInfo;
            if (selected == null)
            {
                _handlesHeaderSub.Text = "No Roblox instance selected.";
                _toggleAfkBtn.Content = "Set AFK Saver";
                return;
            }

            _selectedPid = selected.ProcessId;
            if (selected.Role == InstanceRole.AfkSaver)
            {
                _toggleAfkBtn.Content = "Set as Main";
                _toggleAfkBtn.Background = AccentBlueBg;
                _toggleAfkBtn.Foreground = AccentBlue;
                _toggleAfkBtn.BorderBrush = AccentBlue;
            }
            else
            {
                _toggleAfkBtn.Content = "Set AFK Saver";
                _toggleAfkBtn.Background = TealBg;
                _toggleAfkBtn.Foreground = TealAccent;
                _toggleAfkBtn.BorderBrush = TealAccent;
            }

            if (selected.State == SingletonState.AccessDenied)
            {
                _handlesHeaderSub.Text = "Access denied for PID " + selected.ProcessId + " — click 'Run as Admin' in the top bar.";
                return;
            }

            foreach (var h in selected.Handles)
                _selectedHandles.Add(h);

            _handlesHeaderSub.Text = string.Format(
                "PID {0} ({1} · {2}) — {3} Roblox handle(s) found, {4} singleton lock(s) active.",
                selected.ProcessId,
                selected.Label,
                selected.RoleDisplay,
                selected.Handles.Count,
                selected.SingletonHandleCount);
        }

        private void UpdateKpis(List<RobloxInstanceInfo> scanned, SystemMemoryStats sysMem)
        {
            int total = scanned.Count;
            int mainCount = scanned.Count(x => x.Role == InstanceRole.Main);
            int afkCount = scanned.Count(x => x.Role == InstanceRole.AfkSaver);
            int locked = scanned.Count(x => x.State == SingletonState.Locked);
            int denied = scanned.Count(x => x.State == SingletonState.AccessDenied);
            double totalMb = scanned.Sum(x => x.MemoryMB);

            _kpiCountValue.Text = total.ToString();
            _kpiCountSub.Text = total == 0
                ? "Waiting for RobloxPlayerBeta.exe"
                : string.Format("{0} Main · {1} AFK Saver", mainCount, afkCount);

            if (total == 0)
            {
                _kpiStatusValue.Text = "Idle";
                _kpiStatusValue.Foreground = TextSecondary;
                _kpiStatusSub.Text = "Launch Roblox to begin";
            }
            else if (denied > 0)
            {
                _kpiStatusValue.Text = "Admin Needed";
                _kpiStatusValue.Foreground = StatusRed;
                _kpiStatusSub.Text = denied + " instance(s) require Administrator rights";
            }
            else if (locked > 0)
            {
                _kpiStatusValue.Text = locked + " Locked";
                _kpiStatusValue.Foreground = StatusAmber;
                _kpiStatusSub.Text = "Click 'Unlock All' or enable Auto-Unlock";
            }
            else
            {
                _kpiStatusValue.Text = "Ready";
                _kpiStatusValue.Foreground = StatusGreen;
                _kpiStatusSub.Text = "All instances unlocked — safe to launch next account";
            }

            string robloxRamStr = totalMb >= 1024.0
                ? (totalMb / 1024.0).ToString("0.00") + " GB"
                : totalMb.ToString("0") + " MB";

            _kpiRamValue.Text = string.Format("Free: {0:0.00} GB", sysMem.AvailablePhysicalGB);
            if (sysMem.AvailablePhysicalGB < 1.2)
                _kpiRamValue.Foreground = StatusRed;
            else if (sysMem.AvailablePhysicalGB < 2.5)
                _kpiRamValue.Foreground = StatusAmber;
            else
                _kpiRamValue.Foreground = StatusGreen;

            _kpiRamSub.Text = string.Format(
                "Roblox WS: {0}  |  Total RAM: {1:0.0} GB ({2}% used)",
                robloxRamStr,
                sysMem.TotalPhysicalGB,
                sysMem.MemoryLoadPercent);
        }

        private async Task ToggleSelectedRoleAsync()
        {
            var selected = _instancesList.SelectedItem as RobloxInstanceInfo;
            if (selected == null)
            {
                Log("WARN", "Select a Roblox instance first.");
                return;
            }

            InstanceRole newRole = (selected.Role == InstanceRole.AfkSaver)
                ? InstanceRole.Main
                : InstanceRole.AfkSaver;

            _instanceRoles[selected.ProcessId] = newRole;
            if (newRole == InstanceRole.Main)
                _appliedAfkPids.Remove(selected.ProcessId);
            else
                _appliedAfkPids.Add(selected.ProcessId);

            var res = await Task.Run(() =>
                AfkOptimizer.ApplyRoleProfile(selected.ProcessId, selected.WindowHandle, newRole, resizeWindow: (newRole == InstanceRole.AfkSaver)));

            LogAfkApplyResult(selected.Label, res);
            await RefreshAsync(allowAutoActions: false);
        }

        private void LogAfkApplyResult(string label, AfkApplyResult res)
        {
            if (res.Role == InstanceRole.AfkSaver)
            {
                string ramNote = res.RamTrimmed
                    ? string.Format("RAM {0:0}->{1:0} MB", res.BeforeWorkingSetMB, res.AfterWorkingSetMB)
                    : "RAM unchanged";
                Log("OK", string.Format(
                    "AFK Saver ON for {0} (PID {1}): {2}, EcoQoS={3}, Compact={4}, Mute={5}",
                    label, res.ProcessId, ramNote,
                    res.EcoQoSApplied ? "ON" : "no",
                    res.WindowResized ? "520x320" : "skip",
                    res.AudioMuteUpdated ? "ON" : "pending"));
            }
            else
            {
                Log("OK", string.Format(
                    "Restored MAIN profile for {0} (PID {1}): Normal priority, EcoQoS=OFF, Audio={2}",
                    label, res.ProcessId, res.AudioMuteUpdated ? "UNMUTED" : "unchanged"));
            }

            foreach (string w in res.Warnings)
            {
                if (w.IndexOf("No active audio session", StringComparison.OrdinalIgnoreCase) < 0)
                    Log("WARN", w);
            }
        }

        private async Task TrimAllAfkRamAsync(bool isManual)
        {
            var afkTargets = _instances.Where(x => x.Role == InstanceRole.AfkSaver).ToList();
            if (afkTargets.Count == 0)
            {
                if (isManual)
                    Log("INFO", "No AFK Saver instances to trim. Select a scout account and click 'Set AFK Saver' first.");
                return;
            }

            double totalBefore = 0;
            double totalAfter = 0;
            int okCount = 0;

            foreach (var inst in afkTargets)
            {
                double b = 0, a = 0;
                string err = null;
                bool ok = await Task.Run(() => AfkOptimizer.TrimWorkingSet(inst.ProcessId, out b, out a, out err));
                if (ok)
                {
                    okCount++;
                    totalBefore += b;
                    totalAfter += a;
                }
                else if (!string.IsNullOrEmpty(err))
                {
                    Log("WARN", err);
                }
            }

            if (okCount > 0)
            {
                Log("OK", string.Format(
                    "Trimmed {0} AFK instance(s): {1:0} MB -> {2:0} MB (freed ~{3:0} MB working set).",
                    okCount, totalBefore, totalAfter, Math.Max(0, totalBefore - totalAfter)));
                await RefreshAsync(allowAutoActions: false);
            }
        }

        private async Task UnlockAllAsync()
        {
            bool includeMutex = _includeMutexCheck.IsChecked == true;
            var targets = _instances.Where(x => x.State == SingletonState.Locked || x.SingletonHandleCount > 0).ToList();

            if (targets.Count == 0)
            {
                Log("INFO", "No locked Roblox instances to unlock.");
                return;
            }

            int totalClosed = 0;
            foreach (var inst in targets)
            {
                var res = await Task.Run(() => _engine.UnlockInstance(inst.ProcessId, true, includeMutex));
                if (res.ClosedCount > 0)
                {
                    totalClosed += res.ClosedCount;
                    Log("OK", string.Format("Unlocked PID {0} ({1}): closed {2}", inst.ProcessId, inst.Label, string.Join(", ", res.ClosedHandleNames)));
                }
                else if (!string.IsNullOrEmpty(res.ErrorMessage))
                {
                    Log("ERR", res.ErrorMessage);
                }
            }

            if (totalClosed > 0)
                await RefreshAsync(allowAutoActions: false);
        }

        private async Task UnlockSelectedAsync()
        {
            var selected = _instancesList.SelectedItem as RobloxInstanceInfo;
            if (selected == null)
            {
                Log("WARN", "Select a Roblox instance first.");
                return;
            }

            bool includeMutex = _includeMutexCheck.IsChecked == true;
            var res = await Task.Run(() => _engine.UnlockInstance(selected.ProcessId, true, includeMutex));
            if (res.ClosedCount > 0)
                Log("OK", string.Format("Unlocked PID {0}: closed {1}", selected.ProcessId, string.Join(", ", res.ClosedHandleNames)));
            else if (!string.IsNullOrEmpty(res.ErrorMessage))
                Log("ERR", res.ErrorMessage);
            else
                Log("INFO", "PID " + selected.ProcessId + " has no active singleton locks.");

            await RefreshAsync(allowAutoActions: false);
        }

        private async Task CloseSelectedHandleAsync()
        {
            var h = _handlesList.SelectedItem as RobloxHandleInfo;
            if (h == null)
            {
                Log("WARN", "Select a handle in the lower pane first.");
                return;
            }

            if (!h.IsPrimaryTarget)
            {
                var confirm = MessageBox.Show(
                    "'" + h.ShortName + "' is not a primary Roblox singleton lock.\nClosing non-singleton handles can destabilize the client.\n\nClose this handle anyway?",
                    "Confirm Close Handle",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;
            }

            string err = null;
            bool ok = await Task.Run(() => _engine.CloseSpecificHandle(h.ProcessId, h.HandleValue, h.FullObjectName, out err));
            if (ok)
                Log("OK", string.Format("Closed handle {0} ({1}) on PID {2}", h.HandleHex, h.ShortName, h.ProcessId));
            else
                Log("ERR", err ?? "Failed to close handle.");

            await RefreshAsync(allowAutoActions: false);
        }

        private void FocusSelected()
        {
            var selected = _instancesList.SelectedItem as RobloxInstanceInfo;
            if (selected == null) return;
            if (selected.WindowHandle == IntPtr.Zero)
            {
                Log("WARN", "PID " + selected.ProcessId + " has no visible top-level window yet.");
                return;
            }

            RobloxHandleEngine.FocusWindow(selected.WindowHandle);
            Log("INFO", "Focused window for " + selected.Label + " (PID " + selected.ProcessId + ").");
        }

        private void RenameSelected()
        {
            var selected = _instancesList.SelectedItem as RobloxInstanceInfo;
            if (selected == null)
            {
                Log("WARN", "Select a Roblox instance to rename.");
                return;
            }

            string newLabel = PromptRenameDialog(selected.Label, selected.WindowTitle);
            if (string.IsNullOrWhiteSpace(newLabel)) return;

            _customLabels[selected.ProcessId] = newLabel.Trim();
            if (selected.WindowHandle != IntPtr.Zero)
                RobloxHandleEngine.RenameWindow(selected.WindowHandle, "Roblox — " + newLabel.Trim());

            Log("OK", string.Format("Renamed PID {0} to '{1}'.", selected.ProcessId, newLabel.Trim()));
            var refreshTask = RefreshAsync(allowAutoActions: false);
        }

        private void TileAllWindows(TileLayout layout)
        {
            var hwnds = _instances.Where(x => x.WindowHandle != IntPtr.Zero).Select(x => x.WindowHandle).ToList();
            if (hwnds.Count == 0)
            {
                Log("WARN", "No visible Roblox windows found to arrange.");
                return;
            }

            int moved = RobloxHandleEngine.TileWindows(hwnds, layout);
            Log("OK", string.Format("Arranged {0} Roblox window(s) using {1} layout.", moved, layout));
        }

        private async Task KillSelectedAsync()
        {
            var selected = _instancesList.SelectedItem as RobloxInstanceInfo;
            if (selected == null) return;

            var res = MessageBox.Show(
                string.Format("Terminate {0} (PID {1})?", selected.Label, selected.ProcessId),
                "Kill Roblox Instance",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            string err;
            if (RobloxHandleEngine.KillProcess(selected.ProcessId, out err))
                Log("WARN", string.Format("Terminated {0} (PID {1}).", selected.Label, selected.ProcessId));
            else
                Log("ERR", "Failed to kill PID " + selected.ProcessId + ": " + err);

            await RefreshAsync(allowAutoActions: false);
        }

        private async Task KillAllAsync()
        {
            if (_instances.Count == 0) return;

            var res = MessageBox.Show(
                string.Format("Terminate ALL {0} running Roblox instance(s)?", _instances.Count),
                "Kill All Roblox Instances",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            int killed = 0;
            foreach (var inst in _instances.ToList())
            {
                string err;
                if (RobloxHandleEngine.KillProcess(inst.ProcessId, out err))
                    killed++;
            }

            Log("WARN", "Terminated " + killed + " Roblox instance(s).");
            await RefreshAsync(allowAutoActions: false);
        }

        private void LaunchNewRoblox()
        {
            if (_instances.Any(x => x.State == SingletonState.Locked))
            {
                var r = MessageBox.Show(
                    "One or more running Roblox instances still hold ROBLOX_singletonEvent.\nUnlock them first before launching a new client?",
                    "Locked Instance Detected",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (r == MessageBoxResult.Cancel) return;
                if (r == MessageBoxResult.Yes)
                {
                    bool includeMutex = _includeMutexCheck.IsChecked == true;
                    foreach (var inst in _instances.Where(x => x.State == SingletonState.Locked).ToList())
                        _engine.UnlockInstance(inst.ProcessId, true, includeMutex);
                    Log("OK", "Unlocked locked instance(s) prior to launch.");
                }
            }

            string msg;
            bool ok = RobloxHandleEngine.LaunchNewRobloxClient(out msg);
            Log(ok ? "OK" : "ERR", msg);
        }

        private void RestartAsAdmin()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                _isExiting = true;
                Close();
            }
            catch (Exception ex)
            {
                Log("WARN", "Admin elevation cancelled or failed: " + ex.Message);
            }
        }

        private string PromptRenameDialog(string currentLabel, string currentWindowTitle)
        {
            var dlg = new Window
            {
                Title = "Rename Roblox Instance",
                Icon = this.Icon,
                Width = 380,
                Height = 185,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = BgSurface,
                Foreground = TextPrimary,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
            };

            dlg.SourceInitialized += (s, e) =>
            {
                NativeMethods.EnableDarkTitleBar(new WindowInteropHelper(dlg).Handle);
            };

            var stack = new StackPanel { Margin = new Thickness(18) };
            stack.Children.Add(new TextBlock
            {
                Text = "Account / Instance Label",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimary,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var tb = CreateDarkTextBox(currentLabel ?? "", "");
            stack.Children.Add(tb);

            var btnBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            string result = null;
            var cancelBtn = CreateCompactButton("Cancel", BgElevated, TextSecondary, BorderSubtle);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (s, e) => dlg.Close();
            btnBar.Children.Add(cancelBtn);

            var okBtn = CreateCompactButton("Save Title", AccentBlue, System.Windows.Media.Brushes.White, AccentBlue);
            okBtn.IsDefault = true;
            okBtn.Click += (s, e) =>
            {
                result = tb.Text;
                dlg.Close();
            };
            btnBar.Children.Add(okBtn);

            stack.Children.Add(btnBar);
            dlg.Content = stack;
            dlg.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
            dlg.ShowDialog();
            return result;
        }

        private void InitTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Icon = CreateAppIcon(),
                Text = "RoMulti — Roblox Multi-Instance, AFK Saver & Event Watcher",
                Visible = true
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open RoMulti", null, (s, e) => Dispatcher.Invoke(RestoreFromTray));

            _trayAutoUnlockItem = new Forms.ToolStripMenuItem("Auto-Unlock: OFF");
            _trayAutoUnlockItem.Click += (s, e) => Dispatcher.Invoke(() =>
            {
                _autoUnlockToggle.IsChecked = !(_autoUnlockToggle.IsChecked == true);
            });
            menu.Items.Add(_trayAutoUnlockItem);

            _trayWatcherItem = new Forms.ToolStripMenuItem("Event Watcher: OFF");
            _trayWatcherItem.Click += (s, e) => Dispatcher.Invoke(() =>
            {
                _watcherToggle.IsChecked = !(_watcherToggle.IsChecked == true);
            });
            menu.Items.Add(_trayWatcherItem);

            _trayAutoPeekItem = new Forms.ToolStripMenuItem(_config.AutoPeekEnabled ? "Smart Auto-Peek: ON" : "Smart Auto-Peek: OFF");
            _trayAutoPeekItem.Click += (s, e) => Dispatcher.Invoke(() =>
            {
                _autoPeekToggle.IsChecked = !(_autoPeekToggle.IsChecked == true);
            });
            menu.Items.Add(_trayAutoPeekItem);

            _trayAntiAfkItem = new Forms.ToolStripMenuItem(_config.AntiAfkEnabled ? "Scout Anti-AFK: ON" : "Scout Anti-AFK: OFF");
            _trayAntiAfkItem.Click += (s, e) => Dispatcher.Invoke(() =>
            {
                _antiAfkToggle.IsChecked = !(_antiAfkToggle.IsChecked == true);
            });
            menu.Items.Add(_trayAntiAfkItem);

            _trayMainAntiAfkItem = new Forms.ToolStripMenuItem(_config.AntiAfkMainWhenIdle ? "MAIN Anti-AFK: ON" : "MAIN Anti-AFK: OFF");
            _trayMainAntiAfkItem.Click += (s, e) => Dispatcher.Invoke(() =>
            {
                _antiAfkMainIdleCheck.IsChecked = _antiAfkMainIdleCheck.IsChecked != true;
            });
            menu.Items.Add(_trayMainAntiAfkItem);

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Unlock All Now", null, (s, e) => Dispatcher.Invoke(async () => await UnlockAllAsync()));
            menu.Items.Add("Trim AFK RAM Now", null, (s, e) => Dispatcher.Invoke(async () => await TrimAllAfkRamAsync(isManual: true)));
            menu.Items.Add("Tile Roblox Windows", null, (s, e) => Dispatcher.Invoke(() => TileAllWindows(TileLayout.AutoGrid)));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit RoMulti", null, (s, e) => Dispatcher.Invoke(() =>
            {
                _isExiting = true;
                Close();
            }));

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => Dispatcher.Invoke(RestoreFromTray);
        }

        private void UpdateAutoUnlockVisual(bool enabled)
        {
            _autoUnlockToggle.Content = enabled ? "Auto-Unlock: ON" : "Auto-Unlock: OFF";
            _autoUnlockToggle.Background = enabled ? StatusGreenBg : BgElevated;
            _autoUnlockToggle.Foreground = enabled ? StatusGreen : TextPrimary;
            _autoUnlockToggle.BorderBrush = enabled ? StatusGreen : BorderSubtle;

            if (_trayAutoUnlockItem != null)
                _trayAutoUnlockItem.Text = enabled ? "Auto-Unlock: ON" : "Auto-Unlock: OFF";
        }

        private void UpdateWatcherVisual(bool enabled)
        {
            _watcherToggle.Content = enabled ? "Event Watcher: ON" : "Event Watcher: OFF";
            _watcherToggle.Background = enabled ? PurpleBg : BgElevated;
            _watcherToggle.Foreground = enabled ? PurpleAccent : TextPrimary;
            _watcherToggle.BorderBrush = enabled ? PurpleAccent : BorderSubtle;

            if (_trayWatcherItem != null)
                _trayWatcherItem.Text = enabled ? "Event Watcher: ON" : "Event Watcher: OFF";
        }

        private void UpdateAutoPeekVisual(bool enabled)
        {
            _autoPeekToggle.Content = enabled ? "Auto-Peek: ON" : "Auto-Peek: OFF";
            _autoPeekToggle.Background = enabled ? AccentBlueBg : BgElevated;
            _autoPeekToggle.Foreground = enabled ? AccentBlue : TextPrimary;
            _autoPeekToggle.BorderBrush = enabled ? AccentBlue : BorderSubtle;

            if (_trayAutoPeekItem != null)
                _trayAutoPeekItem.Text = enabled ? "Smart Auto-Peek: ON" : "Smart Auto-Peek: OFF";
        }

        private void UpdateAntiAfkVisual(bool enabled)
        {
            _antiAfkToggle.Content = enabled ? "Scout Anti-AFK: ON" : "Scout Anti-AFK: OFF";
            _antiAfkToggle.Background = enabled ? TealBg : BgElevated;
            _antiAfkToggle.Foreground = enabled ? TealAccent : TextPrimary;
            _antiAfkToggle.BorderBrush = enabled ? TealAccent : BorderSubtle;

            if (_trayAntiAfkItem != null)
                _trayAntiAfkItem.Text = enabled ? "Scout Anti-AFK: ON" : "Scout Anti-AFK: OFF";
        }

        private void SetMainAntiAfk(bool enabled)
        {
            if (enabled)
                _nextMainAntiAfkUtc = DateTime.UtcNow.AddSeconds(15);
            if (_trayMainAntiAfkItem != null)
                _trayMainAntiAfkItem.Text = enabled ? "MAIN Anti-AFK: ON" : "MAIN Anti-AFK: OFF";
            SaveWatcherConfig(silent: true);
            Log("INFO", enabled ? "MAIN Anti-AFK enabled (requires 2 minutes of PC inactivity)." : "MAIN Anti-AFK disabled.");
        }

        private void HideToTray()
        {
            Hide();
            AfkOptimizer.TrimSelfWorkingSet();
            if (_trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(1500, "RoMulti", "Running in System Tray. Double-click icon to reopen.", Forms.ToolTipIcon.Info);
            }
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            bool keepInTray = !_isExiting && (
                _autoUnlockToggle.IsChecked == true ||
                _watcherToggle.IsChecked == true ||
                _antiAfkToggle.IsChecked == true ||
                _antiAfkMainIdleCheck.IsChecked == true ||
                _instances.Any(x => x.Role == InstanceRole.AfkSaver));

            if (keepInTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _refreshTimer.Stop();
            _watcherTimer.Stop();
            _engine.Dispose();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                var icon = _trayIcon.Icon;
                var menu = _trayIcon.ContextMenuStrip;
                _trayIcon.Dispose();
                if (menu != null) menu.Dispose();
                if (icon != null) icon.Dispose();
                _trayIcon = null;
            }
        }

        private void Log(string level, string message)
        {
            string entry = string.Format("[{0:HH:mm:ss}] [{1}] {2}", DateTime.Now, level, message);
            _activityLog.Insert(0, entry);
            while (_activityLog.Count > 150)
                _activityLog.RemoveAt(_activityLog.Count - 1);
        }

        // --- Styling & Helper Controls ---

        private static SolidColorBrush BrushFromHex(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex);
            b.Freeze();
            return b;
        }

        private static TextBox CreateDarkTextBox(string text, string tooltip)
        {
            var tb = new TextBox
            {
                Text = text ?? "",
                Background = BgCanvas,
                Foreground = TextPrimary,
                CaretBrush = TextPrimary,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 12
            };
            if (!string.IsNullOrEmpty(tooltip))
                tb.ToolTip = tooltip;
            return tb;
        }

        private static Button CreateActionButton(string text, System.Windows.Media.Brush bg, System.Windows.Media.Brush fg, System.Windows.Media.Brush border)
        {
            var btn = new Button
            {
                Content = text,
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = bg,
                Foreground = fg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btn);
            return btn;
        }

        private static Button CreateCompactButton(string text, System.Windows.Media.Brush bg, System.Windows.Media.Brush fg, System.Windows.Media.Brush border)
        {
            var btn = new Button
            {
                Content = text,
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Background = bg,
                Foreground = fg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btn);
            return btn;
        }

        private static void ApplyRoundedButtonTemplate(ButtonBase btn)
        {
            var template = new ControlTemplate(typeof(ButtonBase));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "bd";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(cp);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88, "bd"));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
        }

        private static ListView CreateDarkListView()
        {
            var lv = new ListView
            {
                Background = BgCanvas,
                Foreground = TextPrimary,
                BorderBrush = BorderSubtle,
                BorderThickness = new Thickness(1),
                FontSize = 12,
                SelectionMode = SelectionMode.Single
            };

            var headerStyle = new Style(typeof(GridViewColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, BgElevated));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextSecondary));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, BorderSubtle));
            headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 11.5));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            lv.Resources.Add(typeof(GridViewColumnHeader), headerStyle);

            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 4, 4, 4)));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, BrushFromHex("#181E2B")));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, BgElevated));
            itemStyle.Triggers.Add(hoverTrigger);

            var selTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selTrigger.Setters.Add(new Setter(Control.BackgroundProperty, BgHover));
            selTrigger.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            itemStyle.Triggers.Add(selTrigger);

            lv.ItemContainerStyle = itemStyle;
            return lv;
        }

        private static GridViewColumn CreateTextColumn(string header, string bindingPath, double width, bool bold, bool mono)
        {
            var col = new GridViewColumn { Header = header, Width = width };
            var tpl = new DataTemplate();
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(bindingPath));
            tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (bold) tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            if (mono) tb.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Consolas"));
            tpl.VisualTree = tb;
            col.CellTemplate = tpl;
            return col;
        }

        private static GridViewColumn CreateRoleColumn(string header, double width)
        {
            var col = new GridViewColumn { Header = header, Width = width };
            var tpl = new DataTemplate();

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 2, 8, 2));
            border.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Role") { Converter = new InstanceRoleBgConverter() });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("Role") { Converter = new InstanceRoleFgConverter() });

            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("RoleDisplay"));
            tb.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Role") { Converter = new InstanceRoleFgConverter() });
            tb.SetValue(TextBlock.FontSizeProperty, 10.5);
            tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);

            border.AppendChild(tb);
            tpl.VisualTree = border;
            col.CellTemplate = tpl;
            return col;
        }

        private static GridViewColumn CreateAlertLevelColumn(string header, double width)
        {
            var col = new GridViewColumn { Header = header, Width = width };
            var tpl = new DataTemplate();

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 2, 8, 2));
            border.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Level") { Converter = new AlertLevelBgConverter() });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("Level") { Converter = new AlertLevelFgConverter() });

            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("LevelDisplay"));
            tb.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Level") { Converter = new AlertLevelFgConverter() });
            tb.SetValue(TextBlock.FontSizeProperty, 10.5);
            tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);

            border.AppendChild(tb);
            tpl.VisualTree = border;
            col.CellTemplate = tpl;
            return col;
        }

        private static GridViewColumn CreateAudioColumn(string header, double width)
        {
            var col = new GridViewColumn { Header = header, Width = width };
            var tpl = new DataTemplate();

            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("AudioDisplay"));
            tb.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("IsAudioMuted") { Converter = new AudioFgConverter() });
            tb.SetValue(TextBlock.FontSizeProperty, 11.0);
            tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            tpl.VisualTree = tb;
            col.CellTemplate = tpl;
            return col;
        }

        private static GridViewColumn CreateStatusColumn(string header, double width)
        {
            var col = new GridViewColumn { Header = header, Width = width };
            var tpl = new DataTemplate();

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 2, 8, 2));
            border.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("State") { Converter = new SingletonBgConverter() });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("State") { Converter = new SingletonFgConverter() });

            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StateDisplay"));
            tb.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("State") { Converter = new SingletonFgConverter() });
            tb.SetValue(TextBlock.FontSizeProperty, 10.5);
            tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);

            border.AppendChild(tb);
            tpl.VisualTree = border;
            col.CellTemplate = tpl;
            return col;
        }

        private static GridViewColumn CreateHandleRoleColumn(string header, double width)
        {
            var col = new GridViewColumn { Header = header, Width = width };
            var tpl = new DataTemplate();

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new Thickness(7, 2, 7, 2));
            border.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("RoleTag") { Converter = new RoleBgConverter() });

            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("RoleTag"));
            tb.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("RoleTag") { Converter = new RoleFgConverter() });
            tb.SetValue(TextBlock.FontSizeProperty, 10.5);
            tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);

            border.AppendChild(tb);
            tpl.VisualTree = border;
            col.CellTemplate = tpl;
            return col;
        }

        private static Bitmap RenderAppIconBitmap(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                    float pad = Math.Max(1f, size * 0.05f);
                    var rect = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
                    float r = size * 0.22f;
                    float d = r * 2f;

                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                        path.CloseFigure();

                        using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(
                            rect,
                            System.Drawing.Color.FromArgb(79, 143, 255),
                            System.Drawing.Color.FromArgb(37, 99, 235),
                            System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                        {
                            g.FillPath(bg, path);
                        }
                        using (var border = new System.Drawing.Pen(System.Drawing.Color.FromArgb(90, 255, 255, 255), Math.Max(1f, size * 0.03f)))
                        {
                            g.DrawPath(border, path);
                        }
                    }

                    using (var font = new Font("Segoe UI", size * 0.44f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var fmt = new StringFormat(StringFormat.GenericTypographic))
                    {
                        fmt.Alignment = StringAlignment.Center;
                        fmt.LineAlignment = StringAlignment.Center;
                        var textRect = new RectangleF(0, size * 0.03f, size, size);
                        if (size >= 32)
                        {
                            using (var shadow = new SolidBrush(System.Drawing.Color.FromArgb(70, 12, 26, 66)))
                            {
                                var shRect = new RectangleF(textRect.X, textRect.Y + Math.Max(1f, size * 0.03f), textRect.Width, textRect.Height);
                                g.DrawString("R+", font, shadow, shRect, fmt);
                            }
                        }
                        using (var fg = new SolidBrush(System.Drawing.Color.White))
                        {
                            g.DrawString("R+", font, fg, textRect, fmt);
                        }
                    }
                }
                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }

        private static ImageSource CreateWindowIconSource()
        {
            try
            {
                using (var bmp = RenderAppIconBitmap(64))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = ms;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
            }
            catch
            {
                return null;
            }
        }

        private static Icon CreateAppIcon()
        {
            using (var bmp = RenderAppIconBitmap(32))
            {
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (var icon = System.Drawing.Icon.FromHandle(hIcon))
                        return (Icon)icon.Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }

        public sealed class EventRuleRowViewModel
        {
            public EventRule Rule { get; set; }
            public EventAlertLevel Level { get; set; }
            public string DisplayName { get { return Rule.DisplayName; } }
            public string Hint { get { return Rule.Hint; } }
            public string LevelDisplay { get { return Level.ToString().ToUpperInvariant(); } }
        }

        private sealed class AlertLevelFgConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is EventAlertLevel)
                {
                    var lv = (EventAlertLevel)value;
                    if (lv == EventAlertLevel.Ping) return PurpleAccent;
                    if (lv == EventAlertLevel.Normal) return StatusGreen;
                }
                return TextSecondary;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }

        private sealed class AlertLevelBgConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is EventAlertLevel)
                {
                    var lv = (EventAlertLevel)value;
                    if (lv == EventAlertLevel.Ping) return PurpleBg;
                    if (lv == EventAlertLevel.Normal) return StatusGreenBg;
                }
                return BgElevated;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }

        private sealed class InstanceRoleFgConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is InstanceRole && ((InstanceRole)value) == InstanceRole.AfkSaver)
                    return TealAccent;
                return AccentBlue;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }

        private sealed class InstanceRoleBgConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is InstanceRole && ((InstanceRole)value) == InstanceRole.AfkSaver)
                    return TealBg;
                return AccentBlueBg;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }

        private sealed class AudioFgConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is bool && (bool)value) return TextSecondary;
                return StatusGreen;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }

        private sealed class SingletonFgConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is SingletonState)
                {
                    var st = (SingletonState)value;
                    switch (st)
                    {
                        case SingletonState.Unlocked: return StatusGreen;
                        case SingletonState.Locked: return StatusAmber;
                        case SingletonState.AccessDenied: return StatusRed;
                    }
                }
                return TextSecondary;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }

        private sealed class SingletonBgConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is SingletonState)
                {
                    var st = (SingletonState)value;
                    switch (st)
                    {
                        case SingletonState.Unlocked: return StatusGreenBg;
                        case SingletonState.Locked: return StatusAmberBg;
                        case SingletonState.AccessDenied: return StatusRedBg;
                    }
                }
                return BgElevated;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }

        private sealed class RoleFgConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                string tag = value as string;
                if (tag == "PRIMARY LOCK") return StatusAmber;
                if (tag == "LEGACY LOCK") return AccentBlue;
                return TextSecondary;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }

        private sealed class RoleBgConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                string tag = value as string;
                if (tag == "PRIMARY LOCK") return StatusAmberBg;
                if (tag == "LEGACY LOCK") return AccentBlueBg;
                return BgElevated;
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }
    }
}
