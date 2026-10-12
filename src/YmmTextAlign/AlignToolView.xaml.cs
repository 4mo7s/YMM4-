using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace YmmTextAlign;

public partial class AlignToolView : UserControl
{
    public AlignToolView() => InitializeComponent();

    private void Place(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AlignToolViewModel vm || sender is not Button button || button.Tag is not string tag)
            return;
        if (!vm.TrySetMargins(HorizontalMarginBox.Text, VerticalMarginBox.Text)) return;
        var parts = tag.Split(',');
        var cursor = Mouse.OverrideCursor;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            vm.Align(parts[0] == "-" ? null : int.Parse(parts[0]), parts[1] == "-" ? null : int.Parse(parts[1]));
        }
        catch (Exception ex) { vm.ReportFailure(ex); }
        finally { Mouse.OverrideCursor = cursor; }
    }
}
