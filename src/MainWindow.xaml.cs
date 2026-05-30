using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnalogtoKey.Models;
using AnalogtoKey.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace AnalogtoKey;

public partial class MainWindow : Window
{
    private readonly InputService   _inputService   = new();
    private readonly ProfileManager _profileManager = new();
    private readonly HidHideService _hidHide        = new();
    private          InputMapper    _mapper;
    private          MappingProfile _currentProfile = new();

    // volatile: læses på poll-tråd, skrives på UI-tråd
    private volatile string _selectedGuid = "";

    private int _selectedAxisIndex = 0;

    // Bruges af slider-klik til at sætte AxisName — sættes i BuildAxisRow
    private ComboBox? _axisNameCombo;

    // Axis-sliders
    // Segmented (Steps > 1): Segments[0] = bundfeltet, Segments[N-1] = topfeltet
    // Kontinuerlig (Steps = 1 eller ikke tildelt): EmptyRow + FilledRow
    private record AxisBar(
        string AxisName,
        Border OuterBorder,
        RowDefinition? EmptyRow,
        RowDefinition? FilledRow,
        Border[]? Segments);
    private AxisBar[] _axisBars = Array.Empty<AxisBar>();

    // Tast-fangst
    private bool    _capturing;
    private string  _captureGuid  = "";
    private string  _captureType  = "";
    private string  _captureIndex = "";
    private Button? _captureButton;

    // Live highlighting
    private readonly Dictionary<string, Button>    _mappingButtons   = new();
    private readonly Dictionary<string, TextBlock> _axisStepDisplays = new();

    private List<StickState> _lastStates = new();

    // Sidst viste aktivitet i statusbar (volatile: kan skrives fra AxisStepSent-handler på bg-tråd)
    private volatile string _lastActivityText = "";

    private TaskbarIcon _trayIcon = null!;
    private bool        _forceClose;

    private static readonly SolidColorBrush ColAssigned   = new(Color.FromRgb(21, 101, 192));
    private static readonly SolidColorBrush ColUnassigned = new(Color.FromRgb(35, 35, 35));
    private static readonly SolidColorBrush ColPressed    = new(Color.FromRgb(76, 175, 80));
    private static readonly SolidColorBrush ColCapture    = new(Color.FromRgb(230, 120, 0));
    private static readonly SolidColorBrush ColTextDim    = new(Color.FromRgb(100, 100, 100));
    private static readonly SolidColorBrush ColTextBright = new(Color.FromRgb(255, 255, 255));

    private static readonly Dictionary<int, string> HatNames = new()
    {
        { 0,     "D-pad Up" },
        { 4500,  "D-pad Up-Right" },
        { 9000,  "D-pad Right" },
        { 13500, "D-pad Down-Right" },
        { 18000, "D-pad Down" },
        { 22500, "D-pad Down-Left" },
        { 27000, "D-pad Left" },
        { 31500, "D-pad Up-Left" },
    };

    private static readonly string[] AxisNames =
        { "AxisX", "AxisY", "AxisZ", "AxisRx", "AxisRy", "AxisRz" };

    private record ControllerItem(string Guid, string Name)
    {
        public override string ToString() => Name;
    }

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowState();

        _mapper = new InputMapper(_currentProfile);

        // Akse-trin: opdatér _lastActivityText på baggrundstråden (volatile)
        _mapper.AxisStepSent += (guid, label, vk) =>
        {
            string deviceName = _inputService.ConnectedDevices
                .FirstOrDefault(d => d.Guid == guid).Name ?? "";
            string keyPart = vk != 0 ? $" → {VKeyNames.GetName(vk)}" : "";
            _lastActivityText = $"{deviceName} | {label}{keyPart}";
        };

        _inputService.StateUpdated += OnStateUpdated;
        _hidHide.WhitelistSelf();
        _inputService.Start();
        _hidHide.HideDevices(_inputService.ConnectedVids);
        UpdateHidHideStatus();

        PopulateControllerDropdown();
        RefreshProfileList(selectLast: true);

        InitTray();
    }

    private void InitTray()
    {
        var icon = new BitmapImage(new Uri("pack://application:,,,/app.ico"));

        var openItem = new MenuItem { Header = "Open" };
        openItem.Click += (_, _) => RestoreWindow();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => { _forceClose = true; Close(); };

        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            IconSource   = icon,
            ToolTipText  = "AnalogtoKey",
            ContextMenu  = menu,
        };
        _trayIcon.TrayMouseDoubleClick += (_, _) => RestoreWindow();
    }

    private void RestoreWindow()
    {
        Show();
        Activate();
        WindowState = WindowState.Normal;
    }

    // ─── Vinduesposition ─────────────────────────────────────────

    private void RestoreWindowState()
    {
        var ws = _profileManager.LoadWindowState();
        if (ws == null) return;
        var (left, top, width, height) = ws.Value;
        if (left > -200 && top > -200 && width >= 600 && height >= 400)
        {
            Left = left; Top = top; Width = width; Height = height;
        }
    }

    // ─── Controller dropdown ─────────────────────────────────────

    private void PopulateControllerDropdown()
    {
        ControllerCombo.SelectionChanged -= ControllerCombo_SelectionChanged;
        ControllerCombo.Items.Clear();

        foreach (var (guid, name) in _inputService.ConnectedDevices)
            ControllerCombo.Items.Add(new ControllerItem(guid, name));

        if (ControllerCombo.Items.Count > 0)
        {
            ControllerCombo.SelectedIndex = 0;
            _selectedGuid = ((ControllerItem)ControllerCombo.Items[0]).Guid;
        }
        else
        {
            _selectedGuid = "";
        }

        ControllerCombo.SelectionChanged += ControllerCombo_SelectionChanged;
        UpdateControllerDot();
    }

    private void ControllerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedGuid      = (ControllerCombo.SelectedItem as ControllerItem)?.Guid ?? "";
        _selectedAxisIndex = 0;
        UpdateControllerDot();
        RebuildMappingUI();
    }

    private void UpdateControllerDot()
    {
        var state     = _lastStates.FirstOrDefault(s => s.DeviceGuid == _selectedGuid);
        bool connected = state?.IsConnected ?? false;
        ControllerDot.Fill        = connected ? ColPressed : new SolidColorBrush(Color.FromRgb(255, 82, 82));
        ControllerStatusText.Text = connected
            ? (ControllerCombo.SelectedItem as ControllerItem)?.Name ?? ""
            : (string.IsNullOrEmpty(_selectedGuid) ? "No controller found" : "Not connected");
    }

    // ─── Profil-håndtering ───────────────────────────────────────

    private void RefreshProfileList(bool selectLast = false)
    {
        ProfileCombo.SelectionChanged -= ProfileCombo_SelectionChanged;
        ProfileCombo.Items.Clear();
        foreach (var name in _profileManager.ListProfiles())
            ProfileCombo.Items.Add(name);

        string target = selectLast ? _profileManager.LoadLastProfile() : _currentProfile.Name;
        ProfileCombo.SelectedItem = ProfileCombo.Items.Contains(target)
            ? target : ProfileCombo.Items[0];

        ProfileCombo.SelectionChanged += ProfileCombo_SelectionChanged;
        LoadSelectedProfile();
    }

    private void LoadSelectedProfile()
    {
        if (ProfileCombo.SelectedItem is not string name) return;
        _currentProfile = _profileManager.Load(name);
        _profileManager.SaveLastProfile(name);
        _mapper.UpdateProfile(_currentProfile);
        RebuildMappingUI();
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => LoadSelectedProfile();

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        _profileManager.Save(_currentProfile);
        _profileManager.SaveLastProfile(_currentProfile.Name);
        var btn  = (Button)sender;
        var orig = btn.Content;
        btn.Content    = "✓ Saved!";
        btn.Background = new SolidColorBrush(Color.FromRgb(46, 125, 50));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) => { btn.Content = orig; btn.Background = ColAssigned; timer.Stop(); };
        timer.Start();
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("New profile", "Enter name:", "MyProfile") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        var profile = new MappingProfile { Name = dlg.Result.Trim() };
        _profileManager.Save(profile);
        _currentProfile = profile;
        RefreshProfileList();
        ProfileCombo.SelectedItem = profile.Name;
    }

    private void CopyProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("Copy profile", "Name for copy:", $"{_currentProfile.Name} — Copy")
            { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        var json = System.Text.Json.JsonSerializer.Serialize(_currentProfile);
        var copy = System.Text.Json.JsonSerializer.Deserialize<MappingProfile>(json)!;
        copy.Name = dlg.Result.Trim();
        _profileManager.Save(copy);
        _currentProfile = copy;
        RefreshProfileList();
        ProfileCombo.SelectedItem = copy.Name;
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) return;
        if (name == "Default") { MessageBox.Show("The Default profile cannot be deleted."); return; }
        if (MessageBox.Show($"Delete profile '{name}'?", "Confirm deletion",
            MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _profileManager.Delete(name);
        RefreshProfileList();
    }

    // ─── Mapping UI ──────────────────────────────────────────────

    private void RebuildMappingUI()
    {
        _mappingButtons.Clear();
        _axisStepDisplays.Clear();
        _axisNameCombo = null;
        _axisBars      = Array.Empty<AxisBar>();
        MappingPanel.Children.Clear();

        if (string.IsNullOrEmpty(_selectedGuid))
        {
            MappingPanel.Children.Add(new TextBlock
            {
                Text = "No controller connected.",
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                FontFamily = new FontFamily("Segoe UI"), FontSize = 14,
                Margin = new Thickness(0, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        var mapping = _currentProfile.GetOrCreate(_selectedGuid);
        BuildStickPanel(MappingPanel, _selectedGuid, mapping);
    }

    private void BuildStickPanel(StackPanel panel, string guid, StickMapping mapping)
    {
        var outerGrid = new Grid();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftPanel  = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var divider    = new Border { Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)) };
        var rightPanel = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };

        // ── Venstre: D-PAD + AKSER ───────────────────────────────
        AddSectionHeader(leftPanel, "D-PAD");
        foreach (var (degrees, vk) in mapping.HatMappings)
        {
            var (row, btn) = MakeRow(HatNames[degrees], vk, guid, "hat", degrees);
            _mappingButtons[$"{guid}|hat|{degrees}"] = btn;
            leftPanel.Children.Add(row);
        }

        AddSectionHeader(leftPanel, "AKSER");
        leftPanel.Children.Add(BuildAxisSliders(guid, mapping));       // sliders
        leftPanel.Children.Add(BuildAxisSelectorRow(guid, mapping));   // dropdown + +/−
        if (mapping.AxisMappings.Count > 0)
        {
            int idx = Math.Clamp(_selectedAxisIndex, 0, mapping.AxisMappings.Count - 1);
            BuildAxisRow(leftPanel, guid, idx, mapping.AxisMappings[idx]);
        }

        // ── Højre: KNAPPER ───────────────────────────────────────
        AddSectionHeader(rightPanel, "KNAPPER");
        foreach (var (idx, vk) in mapping.ButtonMappings)
        {
            var (row, btn) = MakeRow($"Button {idx + 1}", vk, guid, "btn", idx);
            _mappingButtons[$"{guid}|btn|{idx}"] = btn;
            rightPanel.Children.Add(row);
        }

        Grid.SetColumn(leftPanel,  0);
        Grid.SetColumn(divider,    1);
        Grid.SetColumn(rightPanel, 2);
        outerGrid.Children.Add(leftPanel);
        outerGrid.Children.Add(divider);
        outerGrid.Children.Add(rightPanel);
        panel.Children.Add(outerGrid);
    }

    // ─── Akse-sliders ────────────────────────────────────────────

    private UIElement BuildAxisSliders(string guid, StickMapping mapping)
    {
        var container    = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
        var assignedAxes = mapping.AxisMappings.Select(a => a.AxisName).ToHashSet();
        var bars         = new List<AxisBar>();

        foreach (var axisName in AxisNames)
        {
            bool   isAssigned = assignedAxes.Contains(axisName);
            string label      = axisName[4..]; // "AxisX"→"X", "AxisRx"→"Rx"
            var    slot       = mapping.AxisMappings.FirstOrDefault(a => a.AxisName == axisName);
            bool   segmented  = slot != null && slot.Steps > 1;
            int    N          = segmented ? slot!.Steps : 1;

            RowDefinition? emptyRow  = null;
            RowDefinition? filledRow = null;
            Border[]?      segments  = null;
            UIElement      inner;

            if (segmented)
            {
                // N felter stablet: s=N-1 øverst i StackPanel, s=0 nederst
                segments = new Border[N];
                int segH = Math.Max(3, (66 - (N - 1)) / N);
                var sp   = new StackPanel { Orientation = Orientation.Vertical };

                for (int s = N - 1; s >= 0; s--)
                {
                    var seg = new Border
                    {
                        Height          = segH,
                        Background      = new SolidColorBrush(Color.FromRgb(30, 30, 48)),
                        CornerRadius    = new CornerRadius(1),
                        Margin          = new Thickness(1, 0, 1, s > 0 ? 1 : 0)
                    };
                    sp.Children.Add(seg);
                    segments[s] = seg; // segments[0]=bund, segments[N-1]=top
                }
                inner = sp;
            }
            else
            {
                // Kontinuerlig: Grid med tom øverst og fyldt nederst
                var innerGrid = new Grid();
                emptyRow  = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
                filledRow = new RowDefinition { Height = new GridLength(0.001, GridUnitType.Star) };
                innerGrid.RowDefinitions.Add(emptyRow);
                innerGrid.RowDefinitions.Add(filledRow);
                var fill = new Border
                {
                    Background   = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    CornerRadius = new CornerRadius(2, 2, 0, 0)
                };
                Grid.SetRow(fill, 1);
                innerGrid.Children.Add(fill);
                inner = innerGrid;
            }

            var captured    = axisName;
            var outerBorder = new Border
            {
                Width = 32, Height = 68,
                Background      = new SolidColorBrush(Color.FromRgb(22, 22, 38)),
                BorderBrush     = new SolidColorBrush(isAssigned ? Color.FromRgb(21, 101, 192) : Color.FromRgb(55, 55, 55)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(3),
                ClipToBounds    = true,
                Child           = inner,
                Cursor          = Cursors.Hand,
                ToolTip         = $"Click to select {axisName}"
            };
            outerBorder.MouseLeftButtonDown += (_, _) =>
            {
                if (_axisNameCombo != null)
                    _axisNameCombo.SelectedItem = captured;
            };

            var lbl = new TextBlock
            {
                Text = label, Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                FontFamily = new FontFamily("Consolas"), FontSize = 10,
                TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };

            var item = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            item.Children.Add(outerBorder);
            item.Children.Add(lbl);
            container.Children.Add(item);

            bars.Add(new AxisBar(axisName, outerBorder, emptyRow, filledRow, segments));
        }

        _axisBars = bars.ToArray();
        return container;
    }

    // ─── Akse-selector (dropdown + +/−) ─────────────────────────

    private UIElement BuildAxisSelectorRow(string guid, StickMapping mapping)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

        var combo = new ComboBox { Width = 140, Height = 28, Style = TryFindRes("DarkCombo") as Style };

        for (int i = 0; i < mapping.AxisMappings.Count; i++)
            combo.Items.Add(mapping.AxisMappings[i].Label);

        if (mapping.AxisMappings.Count > 0)
            combo.SelectedIndex = Math.Clamp(_selectedAxisIndex, 0, mapping.AxisMappings.Count - 1);

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex != _selectedAxisIndex)
            {
                _selectedAxisIndex = combo.SelectedIndex;
                RebuildMappingUI();
            }
        };

        var addBtn = new Button
        {
            Content   = "+ Axis",
            Height    = 28, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0),
            Style     = TryFindRes("KeyButton") as Style,
            IsEnabled = mapping.AxisMappings.Count < 8,
            ToolTip   = "Add new axis (max 8)"
        };
        addBtn.Click += (_, _) =>
        {
            if (mapping.AxisMappings.Count >= 8) return;
            // Find laveste ubrugte nummer (undgår dubletter ved slet+tilføj)
            var used = mapping.AxisMappings
                .Select(a => a.Label)
                .Where(l => l.StartsWith("Axis ") && int.TryParse(l[5..], out _))
                .Select(l => int.Parse(l[5..]))
                .ToHashSet();
            int n = 1;
            while (used.Contains(n)) n++;
            mapping.AxisMappings.Add(new AxisStepMapping { Label = $"Axis {n}" });
            _selectedAxisIndex = mapping.AxisMappings.Count - 1;
            RebuildMappingUI();
        };

        var removeBtn = new Button
        {
            Content   = "− Remove",
            Height    = 28, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(4, 0, 0, 0),
            Style     = TryFindRes("KeyButton") as Style,
            IsEnabled = mapping.AxisMappings.Count > 1,
            ToolTip   = "Remove selected axis"
        };
        removeBtn.Click += (_, _) =>
        {
            if (mapping.AxisMappings.Count <= 1) return;
            mapping.AxisMappings.RemoveAt(_selectedAxisIndex);
            _selectedAxisIndex = Math.Clamp(_selectedAxisIndex, 0, mapping.AxisMappings.Count - 1);
            RebuildMappingUI();
        };

        sp.Children.Add(combo);
        sp.Children.Add(addBtn);
        sp.Children.Add(removeBtn);
        return sp;
    }

    // ─── Axis row editor ─────────────────────────────────────────

    private void BuildAxisRow(StackPanel panel, string guid, int axisIdx, AxisStepMapping axMap)
    {
        var border = new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(28, 28, 42)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(60, 60, 90)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Margin          = new Thickness(0, 2, 0, 6),
            Padding         = new Thickness(8, 6, 8, 6)
        };
        var sp = new StackPanel();

        // ── Navn-felt (full bredde, bruger-defineret) ─────────────
        var nameGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nameLbl = new TextBlock
        {
            Text = "Name:", Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameLbl, 0);

        var nameBox = new TextBox
        {
            Text             = axMap.Label,
            Foreground       = new SolidColorBrush(Color.FromRgb(150, 200, 255)),
            Background       = Brushes.Transparent,
            BorderBrush      = new SolidColorBrush(Color.FromRgb(60, 60, 90)),
            BorderThickness  = new Thickness(0, 0, 0, 1),
            FontFamily       = new FontFamily("Segoe UI"),
            FontSize         = 13,
            FontWeight       = FontWeights.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip          = "Name for this axis mapping (e.g. Throttle, Brake…)"
        };
        nameBox.TextChanged += (_, _) => axMap.Label = nameBox.Text;
        nameBox.LostFocus   += (_, _) => RebuildMappingUI();
        Grid.SetColumn(nameBox, 1);

        nameGrid.Children.Add(nameLbl);
        nameGrid.Children.Add(nameBox);
        sp.Children.Add(nameGrid);

        // ── Akse + Trin (2 kolonner, ingen gentaget navn) ─────────
        var row1 = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        var axisCombo = new ComboBox { Height = 26, Margin = new Thickness(0, 0, 8, 0), Style = TryFindRes("DarkCombo") as Style };
        _axisNameCombo = axisCombo;
        foreach (var ax in AxisNames) axisCombo.Items.Add(ax);
        axisCombo.SelectedItem = AxisNames.Contains(axMap.AxisName) ? axMap.AxisName : AxisNames[0];
        axisCombo.SelectionChanged += (_, _) =>
        {
            var newName = axisCombo.SelectedItem?.ToString() ?? "AxisX";
            if (newName == axMap.AxisName) return;
            axMap.AxisName = newName;
            Array.Fill(_mapper.GetPrevSteps(guid), -1);
            RebuildMappingUI();
        };
        Grid.SetColumn(axisCombo, 0);

        var trinSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        trinSp.Children.Add(new TextBlock
        {
            Text = "Steps:", Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
        });
        var stepsBox = new TextBox
        {
            Text = axMap.Steps.ToString(), Width = 40, Height = 24,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)), Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)), BorderThickness = new Thickness(1),
            TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        stepsBox.TextChanged += (_, _) => { if (int.TryParse(stepsBox.Text, out int s) && s >= 1 && s <= 8) axMap.Steps = s; };
        stepsBox.LostFocus   += (_, _) => { if (int.TryParse(stepsBox.Text, out int s) && s >= 1 && s <= 8) { axMap.Steps = s; RebuildMappingUI(); } };
        stepsBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && int.TryParse(stepsBox.Text, out int s) && s >= 1 && s <= 8) { axMap.Steps = s; RebuildMappingUI(); } };
        trinSp.Children.Add(stepsBox);
        Grid.SetColumn(trinSp, 1);

        row1.Children.Add(axisCombo);
        row1.Children.Add(trinSp);
        sp.Children.Add(row1);

        var (upRow, upBtn)     = MakeRow("Up ▲",   axMap.UpKey,   guid, "axisup",   axisIdx);
        var (downRow, downBtn) = MakeRow("Down ▼", axMap.DownKey, guid, "axisdown", axisIdx);
        _mappingButtons[$"{guid}|axisup|{axisIdx}"]   = upBtn;
        _mappingButtons[$"{guid}|axisdown|{axisIdx}"] = downBtn;
        sp.Children.Add(upRow);
        sp.Children.Add(downRow);

        var calRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        var minBtn = new Button
        {
            Content = "Capture MIN", Height = 24, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0),
            Cursor = Cursors.Hand, Style = TryFindRes("KeyButton") as Style,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 70))
        };
        minBtn.Click += (_, _) =>
        {
            var state = _lastStates.FirstOrDefault(s => s.DeviceGuid == guid);
            if (state != null) axMap.CalMin = state.GetAxis(axMap.AxisName);
        };

        var maxBtn = new Button
        {
            Content = "Capture MAX", Height = 24, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand, Style = TryFindRes("KeyButton") as Style,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 70))
        };
        maxBtn.Click += (_, _) =>
        {
            var state = _lastStates.FirstOrDefault(s => s.DeviceGuid == guid);
            if (state != null) axMap.CalMax = state.GetAxis(axMap.AxisName);
        };

        var stepDisplay = new TextBlock
        {
            Text = "Step: —", Foreground = new SolidColorBrush(Color.FromRgb(129, 199, 132)),
            FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        };
        _axisStepDisplays[$"{guid}|axis|{axisIdx}"] = stepDisplay;

        calRow.Children.Add(minBtn);
        calRow.Children.Add(maxBtn);
        calRow.Children.Add(stepDisplay);
        sp.Children.Add(calRow);

        border.Child = sp;
        panel.Children.Add(border);
    }

    // Opdaterer slider-kant-farver uden fuld rebuild
    private void RefreshSliderHighlights(string guid)
    {
        if (_axisBars.Length == 0) return;
        var mapping      = _currentProfile.GetOrCreate(guid);
        var assignedAxes = mapping.AxisMappings.Select(a => a.AxisName).ToHashSet();
        foreach (var bar in _axisBars)
            bar.OuterBorder.BorderBrush = new SolidColorBrush(
                assignedAxes.Contains(bar.AxisName)
                    ? Color.FromRgb(21, 101, 192)
                    : Color.FromRgb(55, 55, 55));
    }

    // ─── Hjælpere ────────────────────────────────────────────────

    private static void AddSectionHeader(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text, Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 2)
        });
        panel.Children.Add(new Border
        {
            Height = 1, Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            Margin = new Thickness(0, 0, 0, 4)
        });
    }

    private (UIElement row, Button keyBtn) MakeRow(string label, ushort vk, string guid, string type, object index)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

        var lbl = new TextBlock
        {
            Text = label, Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lbl, 0);

        var keyBtn = new Button
        {
            Content = VKeyNames.GetName(vk), Tag = $"{guid}|{type}|{index}",
            Foreground = vk == 0 ? ColTextDim : ColTextBright,
            Background = vk == 0 ? ColUnassigned : ColAssigned,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)), BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Consolas"), FontSize = 12,
            Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand, Style = TryFindRes("KeyButton") as Style
        };
        keyBtn.Click += KeyButton_Click;
        Grid.SetColumn(keyBtn, 1);

        var clearBtn = new Button
        {
            Content = "✕", Tag = $"{guid}|{type}|{index}",
            Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)), BorderThickness = new Thickness(1),
            FontSize = 12, Width = 32, Height = 28, Margin = new Thickness(6, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand, ToolTip = "Clear mapping"
        };
        clearBtn.Click += ClearButton_Click;
        Grid.SetColumn(clearBtn, 2);

        grid.Children.Add(lbl); grid.Children.Add(keyBtn); grid.Children.Add(clearBtn);
        return (grid, keyBtn);
    }

    private static string GetInputLabel(string key)
    {
        var parts = key.Split('|', 3);
        return parts[1] switch
        {
            "hat"     => HatNames.TryGetValue(int.Parse(parts[2]), out var n) ? n : parts[2],
            "btn"     => $"Knap {int.Parse(parts[2]) + 1}",
            "trigger" => parts[2],
            _         => parts[2]
        };
    }

    // ─── Tast-fangst ─────────────────────────────────────────────

    private void StartCapture(string guid, string type, string index, Button btn)
    {
        if (_capturing) CancelCapture();
        _captureGuid = guid; _captureType = type; _captureIndex = index; _captureButton = btn;
        _capturing = true;
        btn.Content = "▶ Press a key..."; btn.Background = ColCapture; btn.Foreground = ColTextBright;
        CaptureHint.Text = "Press ESC to cancel";
    }

    private void KeyButton_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((Button)sender).Tag.ToString()!.Split('|', 3);
        StartCapture(parts[0], parts[1], parts[2], (Button)sender);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((Button)sender).Tag.ToString()!.Split('|', 3);
        SetMapping(parts[0], parts[1], parts[2], 0);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        if (e.Key == Key.Escape) { CancelCapture(); return; }
        if (!VKeyNames.TryGetVk(e.Key, out var vk)) return;
        SetMapping(_captureGuid, _captureType, _captureIndex, vk);
        CancelCapture();
    }

    private void SetMapping(string guid, string type, string index, ushort vk)
    {
        var mapping = _currentProfile.GetOrCreate(guid);
        if      (type == "hat")      mapping.HatMappings[int.Parse(index)]          = vk;
        else if (type == "btn")      mapping.ButtonMappings[int.Parse(index)]       = vk;
        else if (type == "axisup")   mapping.AxisMappings[int.Parse(index)].UpKey   = vk;
        else if (type == "axisdown") mapping.AxisMappings[int.Parse(index)].DownKey = vk;
        _mapper.UpdateProfile(_currentProfile);
        RebuildMappingUI();
    }

    private void CancelCapture()
    {
        _capturing = false; CaptureHint.Text = ""; _captureButton = null;
        RebuildMappingUI();
    }

    // ─── OnStateUpdated ──────────────────────────────────────────

    private void OnStateUpdated(List<StickState> states)
    {
        _lastStates = states;

        if (!_capturing)
            _mapper.ProcessStates(states);

        Dispatcher.Invoke(() =>
        {
            var selectedState = states.FirstOrDefault(s => s.DeviceGuid == _selectedGuid);
            bool connected    = selectedState?.IsConnected ?? false;

            // ── Byg pressed-set ──────────────────────────────────
            var currentPressed = new HashSet<string>();
            if (connected)
            {
                var g = _selectedGuid;
                if (selectedState!.HatSwitch != -1)
                {
                    int normalized = (int)Math.Round(selectedState.HatSwitch / 4500.0) % 8 * 4500;
                    currentPressed.Add($"{g}|hat|{normalized}");
                }
                for (int b = 0; b < selectedState.Buttons.Length; b++)
                    if (selectedState.Buttons[b])
                        currentPressed.Add($"{g}|btn|{b}");
            }

            // ── Controller dot + status tekst ────────────────────
            ControllerDot.Fill        = connected ? ColPressed : new SolidColorBrush(Color.FromRgb(255, 82, 82));
            ControllerStatusText.Text = connected
                ? (ControllerCombo.SelectedItem as ControllerItem)?.Name ?? ""
                : (string.IsNullOrEmpty(_selectedGuid) ? "No controller found" : "Not connected");

            int count = states.Count(s => s.IsConnected);
            StatusDot.Fill  = count > 0 ? ColPressed : new SolidColorBrush(Color.FromRgb(255, 82, 82));
            StatusText.Text = count > 0 ? $"{count} device(s) active" : "No devices found";

            // ── Live statusbar ───────────────────────────────────
            string deviceName = (ControllerCombo.SelectedItem as ControllerItem)?.Name ?? "";
            if (currentPressed.Count > 0 && !string.IsNullOrEmpty(deviceName))
            {
                var key        = currentPressed.First();
                string inLabel = GetInputLabel(key);
                ushort vk      = GetVkForKey(key);
                string keyPart = vk != 0 ? $" → {VKeyNames.GetName(vk)}" : "";
                string text    = $"{deviceName} | {inLabel}{keyPart}";
                LastKeyText.Text    = text;
                _lastActivityText   = text;
            }
            else
            {
                LastKeyText.Text = string.IsNullOrEmpty(_lastActivityText)
                    ? "Last: —"
                    : $"Last: {_lastActivityText}";
            }

            // ── Akse-sliders ─────────────────────────────────────
            if (_axisBars.Length > 0 && !string.IsNullOrEmpty(_selectedGuid))
            {
                var mapping      = _currentProfile.GetOrCreate(_selectedGuid);
                var assignedAxes = mapping.AxisMappings.Select(a => a.AxisName).ToHashSet();

                foreach (var bar in _axisBars)
                {
                    // Kantfarve: blå = tildelt, grå = ikke tildelt
                    bar.OuterBorder.BorderBrush = new SolidColorBrush(
                        assignedAxes.Contains(bar.AxisName)
                            ? Color.FromRgb(21, 101, 192)
                            : Color.FromRgb(55, 55, 55));

                    if (bar.Segments != null)
                    {
                        // Segmenteret: beregn curStep ud fra slot-kalibrering
                        var slot    = mapping.AxisMappings.FirstOrDefault(a => a.AxisName == bar.AxisName);
                        int curStep = 0;
                        if (connected && slot != null)
                        {
                            int range = slot.CalMax - slot.CalMin;
                            if (range > 0)
                            {
                                int raw = Math.Clamp(selectedState!.GetAxis(bar.AxisName), slot.CalMin, slot.CalMax);
                                curStep = (int)Math.Round((double)(raw - slot.CalMin) / range * slot.Steps);
                                curStep = Math.Clamp(curStep, 0, slot.Steps);
                            }
                        }
                        for (int i = 0; i < bar.Segments.Length; i++)
                            bar.Segments[i].Background = new SolidColorBrush(
                                i < curStep
                                    ? Color.FromRgb(76, 175, 80)   // grøn = tændt
                                    : Color.FromRgb(30, 30, 48));  // mørk = slukket
                    }
                    else if (bar.EmptyRow != null && bar.FilledRow != null)
                    {
                        // Kontinuerlig: fyld proporitionelt med råværdi
                        double frac = connected
                            ? Math.Clamp(selectedState!.GetAxis(bar.AxisName) / 65535.0, 0, 1)
                            : 0;
                        bar.EmptyRow.Height  = new GridLength(Math.Max(0.001, 1 - frac), GridUnitType.Star);
                        bar.FilledRow.Height = new GridLength(Math.Max(0.001, frac),      GridUnitType.Star);
                    }
                }
            }

            // ── Axis step display ────────────────────────────────
            if (connected && !string.IsNullOrEmpty(_selectedGuid))
            {
                var mapping = _currentProfile.GetOrCreate(_selectedGuid);
                for (int ai = 0; ai < mapping.AxisMappings.Count; ai++)
                {
                    if (!_axisStepDisplays.TryGetValue($"{_selectedGuid}|axis|{ai}", out var disp)) continue;
                    var axMap = mapping.AxisMappings[ai];
                    int range = axMap.CalMax - axMap.CalMin;
                    if (range <= 0) { disp.Text = "Step: —"; continue; }
                    int raw  = Math.Clamp(selectedState!.GetAxis(axMap.AxisName), axMap.CalMin, axMap.CalMax);
                    int step = (int)Math.Round((double)(raw - axMap.CalMin) / range * axMap.Steps);
                    disp.Text = $"Step: {step}/{axMap.Steps}";
                }
            }

            // ── Live highlight af mapping-knapper ────────────────
            foreach (var (key, btn) in _mappingButtons)
            {
                if (_capturing && btn == _captureButton) continue;
                bool   pressed = currentPressed.Contains(key);
                ushort vk      = GetVkForKey(key);
                if (pressed)
                {
                    if (vk == 0 && !_capturing) { btn.Background = ColCapture; btn.Foreground = ColTextBright; }
                    else if (vk != 0)           { btn.Background = ColPressed; btn.Foreground = ColTextBright; }
                }
                else
                {
                    btn.Background = vk == 0 ? ColUnassigned : ColAssigned;
                    btn.Foreground = vk == 0 ? ColTextDim    : ColTextBright;
                    btn.Content    = VKeyNames.GetName(vk);
                }
            }
        });
    }

    private ushort GetVkForKey(string key)
    {
        var parts = key.Split('|', 3);
        if (!_currentProfile.Controllers.TryGetValue(parts[0], out var mapping)) return 0;
        return parts[1] switch
        {
            "hat"      => mapping.HatMappings.GetValueOrDefault(int.Parse(parts[2])),
            "btn"      => mapping.ButtonMappings.GetValueOrDefault(int.Parse(parts[2])),
            "axisup"   => mapping.AxisMappings[int.Parse(parts[2])].UpKey,
            "axisdown" => mapping.AxisMappings[int.Parse(parts[2])].DownKey,
            _          => 0
        };
    }

    // ─── HidHide & luk ───────────────────────────────────────────

    private void UpdateHidHideStatus()
    {
        if (!_hidHide.IsAvailable)
        {
            HidHideDot.Fill             = new SolidColorBrush(Color.FromRgb(255, 82, 82));
            HidHideText.Text            = "HidHide: Not installed";
            HidHideWarningBanner.Visibility = Visibility.Visible;
        }
        else if (_hidHide.IsHiding)
        {
            HidHideDot.Fill             = ColPressed;
            HidHideText.Text            = "HidHide: Active";
            HidHideWarningBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            HidHideDot.Fill             = new SolidColorBrush(Color.FromRgb(255, 193, 7));
            HidHideText.Text            = "HidHide: Inactive";
            HidHideWarningBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenManual_Click(object sender, RoutedEventArgs e)
    {
        var pdfPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "AnalogtoKey_UserGuide.pdf");
        if (!System.IO.File.Exists(pdfPath))
        {
            MessageBox.Show(
                "User guide not found.\nExpected location:\n" + pdfPath,
                "File not found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(pdfPath) { UseShellExecute = true });
    }

    private async void InstallHidHide_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        btn.IsEnabled = false;
        btn.Content   = "Downloading...";

        const string downloadUrl =
            "https://github.com/nefarius/HidHide/releases/latest/download/HidHide_Installer.exe";
        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "HidHide_Installer.exe");

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AnalogtoKey/0.1");
            var data = await client.GetByteArrayAsync(downloadUrl);
            await System.IO.File.WriteAllBytesAsync(tempPath, data);

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(tempPath) { UseShellExecute = true });

            MessageBox.Show(
                "Restart AnalogtoKey when the HidHide installation is complete.",
                "Restart required", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            // Download fejlede — åbn browser som fallback
            var res = MessageBox.Show(
                "Could not download HidHide automatically.\nOpen the download page in browser?",
                "Download failed", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(
                        "https://github.com/nefarius/HidHide/releases/latest")
                    { UseShellExecute = true });
        }
        finally
        {
            btn.IsEnabled = true;
            btn.Content   = "Install HidHide";
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _profileManager.SaveWindowState(Left, Top, Width, Height);
        _mapper.ReleaseAll();
        _inputService.Stop();
        _inputService.Dispose();
        _hidHide.RestoreDevices();
        _hidHide.Dispose();
        _trayIcon.Dispose();
    }

    private object? TryFindRes(string key)
    {
        try { return FindResource(key); } catch { return null; }
    }
}
