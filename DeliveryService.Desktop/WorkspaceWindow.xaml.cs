using System.Windows;
using System.Windows.Input;

namespace DeliveryService.Desktop;

public partial class WorkspaceWindow : Window
{
    public WorkspaceWindow() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F5 && DataContext is WorkspaceViewModel model)
            model.RefreshShipments.Execute(null);
        base.OnKeyDown(e);
    }
}
