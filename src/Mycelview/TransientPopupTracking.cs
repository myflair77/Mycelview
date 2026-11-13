using System.Windows;
using System.Windows.Controls;

namespace Mycelview;
internal static class TransientPopupTracking
{
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(Opened));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(Opened));
    }
    private static void Opened(object sender, RoutedEventArgs e)
    {
        var menu = sender as ContextMenu; var tip = sender as ToolTip;
        if (tip != null && NativeShellMenu.IsOpen) { tip.IsOpen = false; return; }
        var target = menu?.PlacementTarget ?? tip?.PlacementTarget;
        var owner = (target == null ? null : Window.GetWindow(target)) ?? Application.Current.MainWindow;
        if (owner == null) return;
        void Close(object? source, EventArgs args) { if (menu != null) menu.IsOpen = false; if (tip != null) tip.IsOpen = false; }
        void Detach(object source, RoutedEventArgs args)
        {
            owner.LocationChanged -= Close; owner.SizeChanged -= Close; owner.StateChanged -= Close;
            if (menu != null) menu.Closed -= Detach;
            if (tip != null) tip.Closed -= Detach;
        }
        owner.LocationChanged += Close; owner.SizeChanged += Close; owner.StateChanged += Close;
        if (menu != null) menu.Closed += Detach;
        if (tip != null) tip.Closed += Detach;
    }
}
