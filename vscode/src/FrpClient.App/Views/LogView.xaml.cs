using System.Windows.Controls;

namespace FrpClient.App.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
    }

    public void AppendLogLine(string message)
    {
        LogTextBox.AppendText(message);
        LogTextBox.ScrollToEnd();
    }
}
