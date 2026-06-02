using System.Windows;
using System.Windows.Input;

namespace AnalogtoKey;

public partial class AxisMonitorWindow : Window
{
    public AxisMonitorWindow()
    {
        InitializeComponent();
    }

    public void SetControllerName(string name)
    {
        ControllerLabel.Text = name;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
