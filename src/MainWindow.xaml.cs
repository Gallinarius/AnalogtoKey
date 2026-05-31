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
        Border[]? Segments,
        int NeutralSegIdx);  // -1 = standard bar; ≥0 = index of neutral segment in center-mode bar
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

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        var oldName = _currentProfile.Name;
        var dlg = new InputDialog("Rename profile", "New name:", oldName) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        var newName = dlg.Result.Trim();
        if (!_profileManager.Rename(oldName, newName))
        {
            MessageBox.Show($"Could not rename — '{newName}' already exists or name is invalid.",
                "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _currentProfile.Name = newName;
        _profileManager.SaveLastProfile(newName);
        RefreshProfileList();
        ProfileCombo.SelectedItem = newName;
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

            // Determine bar type
            bool centerMode = slot != null && slot.UseCenter && slot.UseStandard;
            bool segmented  = centerMode || (slot != null && slot.UseStandard && slot.StepsUp > 1);
            int  N          = centerMode
                ? slot!.StepsDown + 1 + slot.StepsUp
                : (segmented ? slot!.StepsUp : 1);
            int  neutralIdx = centerMode ? slot!.StepsDown : -1;

            RowDefinition? emptyRow  = null;
            RowDefinition? filledRow = null;
            Border[]?      segments  = null;
            UIElement      inner;

            if (segmented)
            {
                // s=N-1 øverst i StackPanel, s=0 nederst
                segments = new Border[N];
                int segH = Math.Max(3, (66 - (N - 1)) / N);
                var sp   = new StackPanel { Orientation = Orientation.Vertical };

                for (int s = N - 1; s >= 0; s--)
                {
                    bool isNeutralSeg = neutralIdx >= 0 && s == neutralIdx;
                    var seg = new Border
                    {
                        Height       = segH,
                        Background   = new SolidColorBrush(isNeutralSeg
                            ? Color.FromRgb(70, 70, 90)
                            : Color.FromRgb(30, 30, 48)),
                        CornerRadius = new CornerRadius(1),
                        Margin       = new Thickness(1, 0, 1, s > 0 ? 1 : 0)
                    };
                    sp.Children.Add(seg);
                    segments[s] = seg;
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

            bars.Add(new AxisBar(axisName, outerBorder, emptyRow, filledRow, segments, neutralIdx));
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

        // ── Name ───────────────────────────────────────────────────
        var nameGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nameLbl = new TextBlock
        {
            Text = "Name:", Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameLbl, 0);
        var nameBox = new TextBox
        {
            Text = axMap.Label,
            Foreground = new SolidColorBrush(Color.FromRgb(150, 200, 255)),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 90)), BorderThickness = new Thickness(0, 0, 0, 1),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13, FontWeight = FontWeights.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        nameBox.TextChanged += (_, _) => axMap.Label = nameBox.Text;
        nameBox.LostFocus   += (_, _) => RebuildMappingUI();
        Grid.SetColumn(nameBox, 1);
        nameGrid.Children.Add(nameLbl);
        nameGrid.Children.Add(nameBox);
        sp.Children.Add(nameGrid);

        // ── Axis combo ─────────────────────────────────────────────
        var axisCombo = new ComboBox { Height = 26, Margin = new Thickness(0, 0, 0, 4), Style = TryFindRes("DarkCombo") as Style };
        _axisNameCombo = axisCombo;
        foreach (var ax in AxisNames) axisCombo.Items.Add(ax);
        axisCombo.SelectedItem = AxisNames.Contains(axMap.AxisName) ? axMap.AxisName : AxisNames[0];
        axisCombo.SelectionChanged += (_, _) =>
        {
            var newName = axisCombo.SelectedItem?.ToString() ?? "AxisX";
            if (newName == axMap.AxisName) return;
            axMap.AxisName = newName;
            _mapper.ResetAxisState(guid);
            RebuildMappingUI();
        };
        sp.Children.Add(axisCombo);

        // ── Mode checkboxes ─────────────────────────────────────────
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };

        // Fælles spinner-felt: [tekstboks | lodret skillelinje | ▲▼ knapper] i én shared border
        (UIElement ctrl, TextBox tb) MakeNumField(int val, int min, int max, Func<int> getV, Action<int> setV, int w = 40)
        {
            var outer = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                VerticalAlignment = VerticalAlignment.Center, Height = 24
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });

            var box = new TextBox
            {
                Text = val.ToString(), Foreground = Brushes.White, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), TextAlignment = TextAlignment.Center,
                FontFamily = new FontFamily("Consolas"), FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            box.TextChanged    += (_, _) => { if (int.TryParse(box.Text, out int v) && v >= min && v <= max) setV(v); };
            box.LostFocus      += (_, _) => { if (int.TryParse(box.Text, out int v) && v >= min && v <= max) { setV(v); RebuildMappingUI(); } else box.Text = getV().ToString(); };
            box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && int.TryParse(box.Text, out int v) && v >= min && v <= max) { setV(v); RebuildMappingUI(); } };
            Grid.SetColumn(box, 0);

            var sep = new Border { Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)) };
            Grid.SetColumn(sep, 1);

            var btnSp = new StackPanel { Orientation = Orientation.Vertical };
            var btnUp = new System.Windows.Controls.Primitives.RepeatButton { Content = "▲", Height = 12, Delay = 400, Interval = 80, FontSize = 7, Padding = new Thickness(0), Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190)), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)) };
            var btnDn = new System.Windows.Controls.Primitives.RepeatButton { Content = "▼", Height = 12, Delay = 400, Interval = 80, FontSize = 7, Padding = new Thickness(0), Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190)), BorderThickness = new Thickness(0) };
            btnUp.Click += (_, _) => { int v = Math.Clamp(getV() + 1, min, max); setV(v); box.Text = v.ToString(); RebuildMappingUI(); };
            btnDn.Click += (_, _) => { int v = Math.Clamp(getV() - 1, min, max); setV(v); box.Text = v.ToString(); RebuildMappingUI(); };
            btnSp.Children.Add(btnUp); btnSp.Children.Add(btnDn);
            Grid.SetColumn(btnSp, 2);

            g.Children.Add(box); g.Children.Add(sep); g.Children.Add(btnSp);
            outer.Child = g;
            return (outer, box);
        }

        CheckBox MakeCb(string label, bool isChecked, Action<bool> onChange)
        {
            var cb = new CheckBox
            {
                Content = label, IsChecked = isChecked,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center
            };
            cb.Checked   += (_, _) => { onChange(true);  RebuildMappingUI(); };
            cb.Unchecked += (_, _) => { onChange(false); RebuildMappingUI(); };
            return cb;
        }

        modeRow.Children.Add(MakeCb("Steps Mode (Standard)", axMap.UseStandard, v => axMap.UseStandard = v));
        modeRow.Children.Add(MakeCb("Center",          axMap.UseCenter,   v => axMap.UseCenter   = v));
        modeRow.Children.Add(MakeCb("Const. Pressure", axMap.UseCp,       v => axMap.UseCp       = v));
        sp.Children.Add(modeRow);

        // ── Dead zone (Center or CP) ───────────────────────────────
        if (axMap.UseCenter || axMap.UseCp)
        {
            var dzRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            dzRow.Children.Add(new TextBlock
            {
                Text = "Dead zone:", Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            });
            var (dzCtrl, _) = MakeNumField(axMap.DeadZonePercent, 1, 95, () => axMap.DeadZonePercent, v => axMap.DeadZonePercent = v);
            dzRow.Children.Add(dzCtrl);
            dzRow.Children.Add(new TextBlock
            {
                Text = "%", Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0)
            });
            sp.Children.Add(dzRow);
        }

        // ── Standard mode section ──────────────────────────────────
        if (axMap.UseStandard)
        {
            if (axMap.UseCenter)
            {
                // Two rows — each with independent Steps + key button
                UIElement MakeCenterStepRow(string rowLabel, int steps, Func<int> getSteps, ushort vk, string keyType, Action<int> onStepsChange)
                {
                    var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

                    var rowLbl = new TextBlock
                    {
                        Text = rowLabel, Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                        FontFamily = new FontFamily("Segoe UI"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(rowLbl, 0);
                    g.Children.Add(rowLbl);

                    var stepsSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    stepsSp.Children.Add(new TextBlock
                    {
                        Text = "Steps:", Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                        FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
                    });
                    var (stepsWidget, _) = MakeNumField(steps, 1, 99, getSteps, onStepsChange, 34);
                    stepsSp.Children.Add(stepsWidget);
                    Grid.SetColumn(stepsSp, 1);

                    var keyBtn = new Button
                    {
                        Content = VKeyNames.GetName(vk), Tag = $"{guid}|{keyType}|{axisIdx}",
                        Foreground = vk == 0 ? ColTextDim : ColTextBright,
                        Background = vk == 0 ? ColUnassigned : ColAssigned,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)), BorderThickness = new Thickness(1),
                        FontFamily = new FontFamily("Consolas"), FontSize = 12,
                        Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Stretch,
                        Cursor = Cursors.Hand, Style = TryFindRes("KeyButton") as Style
                    };
                    keyBtn.Click += KeyButton_Click;
                    Grid.SetColumn(keyBtn, 2);

                    var clrBtn = new Button
                    {
                        Content = "✕", Tag = $"{guid}|{keyType}|{axisIdx}",
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                        Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)), BorderThickness = new Thickness(1),
                        FontSize = 12, Width = 32, Height = 28, Margin = new Thickness(6, 0, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand
                    };
                    clrBtn.Click += ClearButton_Click;
                    Grid.SetColumn(clrBtn, 3);

                    g.Children.Add(stepsSp); g.Children.Add(keyBtn); g.Children.Add(clrBtn);
                    _mappingButtons[$"{guid}|{keyType}|{axisIdx}"] = keyBtn;
                    return g;
                }

                sp.Children.Add(MakeCenterStepRow("Throttle (Up) ▲",  axMap.StepsUp,   () => axMap.StepsUp,   axMap.UpKey,   "axisup",   v => axMap.StepsUp   = v));
                sp.Children.Add(MakeCenterStepRow("Brake (Down) ▼",   axMap.StepsDown, () => axMap.StepsDown, axMap.DownKey, "axisdown", v => axMap.StepsDown = v));
            }
            else
            {
                // Standard single-direction: Steps field above Up/Down rows
                var stepsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                stepsRow.Children.Add(new TextBlock
                {
                    Text = "Steps:", Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
                });
                var (stepsCtrl, _) = MakeNumField(axMap.StepsUp, 1, 99, () => axMap.StepsUp, v => axMap.StepsUp = v);
                stepsRow.Children.Add(stepsCtrl);
                sp.Children.Add(stepsRow);

                var (upRow, upBtn)     = MakeRow("Up ▲",   axMap.UpKey,   guid, "axisup",   axisIdx);
                var (downRow, downBtn) = MakeRow("Down ▼", axMap.DownKey, guid, "axisdown", axisIdx);
                _mappingButtons[$"{guid}|axisup|{axisIdx}"]   = upBtn;
                _mappingButtons[$"{guid}|axisdown|{axisIdx}"] = downBtn;
                sp.Children.Add(upRow);
                sp.Children.Add(downRow);
            }
        }

        // ── Constant Pressure section ──────────────────────────────
        if (axMap.UseCp)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "── Const. Pressure ──",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 140, 200)),
                FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                Margin = new Thickness(0, 4, 0, 2)
            });
            var (cpUpRow,   cpUpBtn)   = MakeRow("Hold Up ▲",   axMap.CpUpKey,   guid, "cpup",   axisIdx);
            var (cpDownRow, cpDownBtn) = MakeRow("Hold Down ▼", axMap.CpDownKey, guid, "cpdown", axisIdx);
            _mappingButtons[$"{guid}|cpup|{axisIdx}"]   = cpUpBtn;
            _mappingButtons[$"{guid}|cpdown|{axisIdx}"] = cpDownBtn;
            sp.Children.Add(cpUpRow);
            sp.Children.Add(cpDownRow);
        }

        // ── Calibration ────────────────────────────────────────────
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
            Text = "—", Foreground = new SolidColorBrush(Color.FromRgb(129, 199, 132)),
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
        if      (type == "hat")      mapping.HatMappings[int.Parse(index)]            = vk;
        else if (type == "btn")      mapping.ButtonMappings[int.Parse(index)]         = vk;
        else if (type == "axisup")   mapping.AxisMappings[int.Parse(index)].UpKey     = vk;
        else if (type == "axisdown") mapping.AxisMappings[int.Parse(index)].DownKey   = vk;
        else if (type == "cpup")     mapping.AxisMappings[int.Parse(index)].CpUpKey   = vk;
        else if (type == "cpdown")   mapping.AxisMappings[int.Parse(index)].CpDownKey = vk;
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

                // CP zone detection — adds cpup/cpdown to currentPressed so buttons highlight
                var cpMap = _currentProfile.GetOrCreate(g);
                for (int cpi = 0; cpi < cpMap.AxisMappings.Count; cpi++)
                {
                    var cpAx = cpMap.AxisMappings[cpi];
                    if (!cpAx.UseCp) continue;
                    int cpR = cpAx.CalMax - cpAx.CalMin;
                    if (cpR <= 0) continue;
                    int cpRaw  = Math.Clamp(selectedState.GetAxis(cpAx.AxisName), cpAx.CalMin, cpAx.CalMax);
                    int cpCtr  = (cpAx.CalMin + cpAx.CalMax) / 2;
                    int cpDead = Math.Max(1, (int)(cpR * cpAx.DeadZonePercent / 100.0 / 2));
                    if (cpRaw > cpCtr + cpDead) currentPressed.Add($"{g}|cpup|{cpi}");
                    if (cpRaw < cpCtr - cpDead) currentPressed.Add($"{g}|cpdown|{cpi}");
                }
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
                        var slot = mapping.AxisMappings.FirstOrDefault(a => a.AxisName == bar.AxisName);

                        if (bar.NeutralSegIdx >= 0 && slot != null)
                        {
                            // Center mode bi-directional bar
                            int curStep = 0;
                            if (connected)
                            {
                                int r2 = slot.CalMax - slot.CalMin;
                                if (r2 > 0)
                                {
                                    int raw2 = Math.Clamp(selectedState!.GetAxis(bar.AxisName), slot.CalMin, slot.CalMax);
                                    int ctr  = (slot.CalMin + slot.CalMax) / 2;
                                    int dz   = Math.Max(1, (int)(r2 * slot.DeadZonePercent / 100.0 / 2));
                                    if (raw2 > ctr + dz)
                                    {
                                        double tR = slot.CalMax - (ctr + dz);
                                        curStep = tR > 0 ? (int)Math.Round((raw2 - ctr - dz) / tR * slot.StepsUp) : slot.StepsUp;
                                        curStep = Math.Clamp(curStep, 1, slot.StepsUp);
                                    }
                                    else if (raw2 < ctr - dz)
                                    {
                                        double bR = (ctr - dz) - slot.CalMin;
                                        curStep = bR > 0 ? -(int)Math.Round((ctr - dz - raw2) / bR * slot.StepsDown) : -slot.StepsDown;
                                        curStep = Math.Clamp(curStep, -slot.StepsDown, -1);
                                    }
                                }
                            }
                            int nIdx = bar.NeutralSegIdx;
                            for (int si = 0; si < bar.Segments.Length; si++)
                            {
                                Color c;
                                if (si == nIdx)
                                    c = Color.FromRgb(70, 70, 90);                                         // neutral — always visible
                                else if (si > nIdx)
                                    c = curStep > 0 && si <= nIdx + curStep                                // throttle zone (top)
                                        ? Color.FromRgb(76, 175, 80) : Color.FromRgb(30, 30, 48);
                                else
                                    c = curStep < 0 && si >= nIdx + curStep                                // brake zone (bottom)
                                        ? Color.FromRgb(255, 82, 82) : Color.FromRgb(30, 30, 48);
                                bar.Segments[si].Background = new SolidColorBrush(c);
                            }
                        }
                        else
                        {
                            // Standard segmented bar — fills from bottom
                            int curStep = 0;
                            if (connected && slot != null)
                            {
                                int r2 = slot.CalMax - slot.CalMin;
                                if (r2 > 0)
                                {
                                    int raw2 = Math.Clamp(selectedState!.GetAxis(bar.AxisName), slot.CalMin, slot.CalMax);
                                    curStep  = (int)Math.Round((double)(raw2 - slot.CalMin) / r2 * slot.StepsUp);
                                    curStep  = Math.Clamp(curStep, 0, slot.StepsUp);
                                }
                            }
                            for (int si = 0; si < bar.Segments.Length; si++)
                                bar.Segments[si].Background = new SolidColorBrush(
                                    si < curStep ? Color.FromRgb(76, 175, 80) : Color.FromRgb(30, 30, 48));
                        }
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
            if (!string.IsNullOrEmpty(_selectedGuid))
            {
                var dispMapping = _currentProfile.GetOrCreate(_selectedGuid);
                for (int ai = 0; ai < dispMapping.AxisMappings.Count; ai++)
                {
                    if (!_axisStepDisplays.TryGetValue($"{_selectedGuid}|axis|{ai}", out var disp)) continue;
                    var axMap = dispMapping.AxisMappings[ai];
                    int range = axMap.CalMax - axMap.CalMin;
                    if (range <= 0 || !connected) { disp.Text = "—"; continue; }

                    int raw     = Math.Clamp(selectedState!.GetAxis(axMap.AxisName), axMap.CalMin, axMap.CalMax);
                    int center  = (axMap.CalMin + axMap.CalMax) / 2;
                    int deadAbs = Math.Max(1, (int)(range * axMap.DeadZonePercent / 100.0 / 2));
                    var sb      = new System.Text.StringBuilder();

                    if (axMap.UseStandard)
                    {
                        if (axMap.UseCenter)
                        {
                            int step;
                            if (raw >= center - deadAbs && raw <= center + deadAbs)
                                step = 0;
                            else if (raw > center + deadAbs)
                            {
                                double tR = axMap.CalMax - (center + deadAbs);
                                step = tR > 0 ? (int)Math.Round((raw - center - deadAbs) / tR * axMap.StepsUp) : axMap.StepsUp;
                                step = Math.Clamp(step, 1, axMap.StepsUp);
                            }
                            else
                            {
                                double bR = (center - deadAbs) - axMap.CalMin;
                                step = bR > 0 ? -(int)Math.Round((center - deadAbs - raw) / bR * axMap.StepsDown) : -axMap.StepsDown;
                                step = Math.Clamp(step, -axMap.StepsDown, -1);
                            }
                            sb.Append(step > 0 ? $"▲{step}/{axMap.StepsUp}" :
                                      step < 0 ? $"▼{-step}/{axMap.StepsDown}" : "neutral");
                        }
                        else
                        {
                            int step = (int)Math.Round((double)(raw - axMap.CalMin) / range * axMap.StepsUp);
                            step = Math.Clamp(step, 0, axMap.StepsUp);
                            sb.Append($"Step:{step}/{axMap.StepsUp}");
                        }
                    }

                    if (axMap.UseCp)
                    {
                        bool upZone   = raw > center + deadAbs;
                        bool downZone = raw < center - deadAbs;
                        if (sb.Length > 0) sb.Append("  ");
                        sb.Append(upZone ? "HOLD▲" : downZone ? "HOLD▼" : "CP:—");
                    }

                    disp.Text = sb.Length > 0 ? sb.ToString() : "—";
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
            "cpup"     => mapping.AxisMappings[int.Parse(parts[2])].CpUpKey,
            "cpdown"   => mapping.AxisMappings[int.Parse(parts[2])].CpDownKey,
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

    private DebugKeyWindow? _debugWindow;
    private void OpenDebug_Click(object sender, RoutedEventArgs e)
    {
        if (_debugWindow == null || !_debugWindow.IsLoaded)
            _debugWindow = new DebugKeyWindow();
        _debugWindow.Show();
        _debugWindow.Activate();
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
