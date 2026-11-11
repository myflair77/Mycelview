using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PinShelf;
internal static class WorkspaceWheelTests
{
    internal static async Task RunAsync(string output)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var folder = Path.Combine(output, "fixture"); Directory.CreateDirectory(folder);
        var store = new StateStore(Path.Combine(output, "state"));
        var tab = new WorkspaceTab { Name = "검증", Layout = new PaneLayout { ExplorerCollapsed = true } };
        for (int i = 0; i < 10; i++) tab.Cards.Add(new CardState { InitialPath = folder, CurrentPath = folder });
        store.Save(new WorkspaceState { Workspaces = [tab, new WorkspaceTab { Name = "다른 탭" }], ActiveWorkspaceId = tab.Id });
        using var vm = new MainViewModel(store);
        var window = new MainWindow(vm) { TestMode = true, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        Application.Current.MainWindow = window; window.Show();
        try
        {
            await vm.InitializeAsync(); window.RestorePaneLayout(); window.UpdateLayout();
            await Task.Delay(100); window.UpdateLayout();
            var file = Path.Combine(folder, "row-hit-test.txt"); File.WriteAllText(file, "test");
            vm.AddPath(file); vm.RecentFiles.Add(new(file, DateTimeOffset.UtcNow));
            window.UpdateLayout(); await Task.Delay(100); window.UpdateLayout();
            foreach (var list in new[] { window.FavoritesList, window.RecentFilesList })
            {
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Check(row != null, "File row must be realised.");
                foreach (var point in new[] { new Point(3, 3), new Point(row!.ActualWidth - 4, 3), new Point(row.ActualWidth - 4, row.ActualHeight - 4), new Point(row.ActualWidth / 2, row.ActualHeight / 2) })
                {
                    var hit = window.InputHitTest(row.TranslatePoint(point, window)) as DependencyObject;
                    Check(ReferenceEquals(FileContextMenus.Parent<ListBoxItem>(hit), row), "File row padding and right edge must resolve to the context-menu row.");
                }
                Check(CardView.FindChild<Button>(row) == null, "File rows must not contain trailing action buttons.");
            }
            Check(vm.FavoriteColumns == 1 && vm.RecentColumns == 1, "File column defaults must be one.");
            for (int i = 0; i < 6; i++)
            {
                var extra = Path.Combine(folder, $"columns-{i}.txt"); File.WriteAllText(extra, "test");
                vm.AddPath(extra); vm.RecentFiles.Add(new(extra, DateTimeOffset.UtcNow));
            }
            foreach (int columns in new[] { 1, 2, 3, 5 })
            {
                vm.FavoriteColumns = columns; vm.RecentColumns = columns; window.UpdateLayout();
                await Task.Delay(200); window.UpdateLayout();
                foreach (var list in new[] { window.FavoritesList, window.RecentFilesList })
                {
                    var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    var nextRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(columns);
                    var origin = first.TranslatePoint(new Point(), list);
                    var next = nextRow.TranslatePoint(new Point(), list);
                    Check(Math.Abs(origin.X - next.X) < 1 && next.Y > origin.Y, "File columns must wrap into the next row.");
                    if (columns > 1)
                    {
                        var second = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
                        var point = second.TranslatePoint(new Point(), list);
                        Check(point.X > origin.X && Math.Abs(point.Y - origin.Y) < 1, "Files must fill columns left to right.");
                    }
                }
            }
            vm.FavoriteColumns = 2; vm.RecentColumns = 3;
            Check(vm.Save(), "Column settings must save.");
            var saved = store.Load().Workspaces.Single(t => t.Id == tab.Id);
            Check(saved.FavoriteColumns == 2 && saved.RecentColumns == 3, "File lists must persist independent column counts.");
            vm.FavoriteColumns = 1; vm.RecentColumns = 1; window.UpdateLayout();
            using (var explorer = new ExplorerViewModel())
            {
                var child = Path.Combine(folder, "navigation-child"); Directory.CreateDirectory(child);
                await explorer.NavigateAsync(folder); await explorer.NavigateAsync(child);
                Check(explorer.CanBack && !explorer.CanForward, "Explorer must record navigation.");
                await explorer.BackAsync();
                Check(explorer.CurrentPath == folder && explorer.CanForward && !explorer.CanBack, "Back must restore previous folder.");
                await explorer.NavigateAsync(folder);
                Check(explorer.CanForward && !explorer.CanBack, "Refresh must preserve history.");
                await explorer.ForwardAsync(); Check(explorer.CurrentPath == child, "Forward must restore child.");
                await explorer.UpAsync(); Check(explorer.CurrentPath == folder, "Up must navigate to parent.");
                await explorer.BackAsync();
                await explorer.NavigateAsync(Path.Combine(folder, "missing-navigation-folder"));
                Check(explorer.CanForward && explorer.CurrentPath == child, "Failed navigation must preserve current path and history.");
                await explorer.NavigateAsync(folder);
                Check(!explorer.CanForward, "New navigation must discard forward history.");
            }
            Check(window.FolderWorkspaceScroll.ScrollableHeight > 0, "Fixture must overflow the workspace.");
            double top = window.PaneGrid.RowDefinitions[0].ActualHeight;
            double right = window.PaneGrid.ColumnDefinitions.Take(3).Sum(c => c.ActualWidth);
            bool Wheel(Point point, int delta = -120)
            {
                var hit = window.PaneGrid.InputHitTest(point) as DependencyObject;
                Check(hit != null, "Blank workspace must participate in hit testing.");
                return window.TryScrollFolderWorkspace(point, hit, delta);
            }
            foreach (var point in new[] { new Point(5, top + 80), new Point(100, top + 20), new Point(right - 7, top + 100) })
            {
                double before = window.FolderWorkspaceScroll.VerticalOffset;
                Check(Wheel(point), "Left gutter, toolbar and right margin must forward wheel input.");
                window.UpdateLayout();
                if (SystemParameters.WheelScrollLines != 0)
                    Check(window.FolderWorkspaceScroll.VerticalOffset > before, "Forwarded wheel must scroll the card collection.");
            }
            var previous = window.FolderWorkspaceScroll.VerticalOffset;
            Check(Wheel(new Point(5, top + 80), 120), "Reverse wheel must be routed."); window.UpdateLayout();
            if (SystemParameters.WheelScrollLines != 0) Check(window.FolderWorkspaceScroll.VerticalOffset < previous, "Reverse wheel must move upwards.");
            window.FolderWorkspaceScroll.ScrollToTop(); window.UpdateLayout(); await Task.Delay(100); window.UpdateLayout();
            var card = CardView.FindChild<CardView>(window.FolderCards)!;
            Check(card != null, "Visible card must be realised.");
            var gap = card!.TranslatePoint(new Point(card.ActualWidth + 5, 100), window.PaneGrid);
            Check(Wheel(gap), "The gap between neighbouring cards must scroll the workspace."); window.UpdateLayout();
            window.FolderWorkspaceScroll.ScrollToTop(); window.UpdateLayout();
            var cardPoint = card!.TranslatePoint(new Point(30, 20), window.PaneGrid);
            Check(!Wheel(cardPoint), "Card header wheel must retain its own behaviour.");
            var listPoint = card.TranslatePoint(new Point(30, 100), window.PaneGrid);
            Check(!Wheel(listPoint), "Card list wheel must retain its own behaviour.");
            Check(!Wheel(new Point(right + 30, top + 80)), "Favorites must not scroll the workspace.");
            Check(!Wheel(new Point(60, 10)), "Workspace tabs must not scroll the cards.");
            window.ToggleExplorer(window, new RoutedEventArgs()); window.UpdateLayout();
            Check(!Wheel(new Point(10, top + 80)), "Expanded explorer must retain normal wheel routing.");
            Check(window.WorkspaceHint.Text == "＋를 눌러 작업공간을 추가하세요", "Workspace hint must use requested label.");
            var otherTab = vm.Workspaces.Single(t => t.Id != tab.Id);
            var originalTabId = vm.ActiveWorkspace!.Id;
            Check(!vm.ExplorerCollapsed, "Explorer starts this check expanded.");
            window.FavoritesRow.Height = new GridLength(.6, GridUnitType.Star); window.RecentRow.Height = new GridLength(.4, GridUnitType.Star);
            window.ExplorerColumn.Width = new GridLength(320); window.FavoritesColumn.Width = new GridLength(350); window.ExplorerTreeRow.Height = new GridLength(210); window.UpdateLayout();
            await vm.SwitchWorkspaceAsync(otherTab.Id); window.UpdateLayout();
            Check(Math.Abs(window.ExplorerColumn.ActualWidth - 320) < 1, "Tab switching must preserve explorer width.");
            Check(Math.Abs(window.ExplorerTreeRow.ActualHeight - 210) < 1, "Tab switching must preserve explorer tree divider.");
            Check(Math.Abs(window.FavoritesColumn.ActualWidth - 350) < 1, "Tab switching must preserve right file pane boundary.");
            Check(Math.Abs(window.FavoritesRow.ActualHeight / (window.FavoritesRow.ActualHeight + window.RecentRow.ActualHeight) - .6) < .005, "Tab switching must preserve the favorites/recent divider.");
            Check(!vm.ExplorerCollapsed && window.ExplorerPane.Visibility == Visibility.Visible, "Tab switch must preserve expanded explorer.");
            window.ToggleExplorer(window, new RoutedEventArgs()); window.UpdateLayout();
            Check(vm.ActiveWorkspace!.Id == otherTab.Id, "Collapsing explorer must not change active tab.");
            await vm.SwitchWorkspaceAsync(originalTabId); window.UpdateLayout();
            Check(vm.ExplorerCollapsed && window.ExplorerPane.Visibility == Visibility.Collapsed, "Tab switch must preserve collapsed explorer.");
            Check(vm.Save() && store.Load().ExplorerCollapsed == true, "Global collapsed state must persist.");
            window.ToggleExplorer(window, new RoutedEventArgs()); window.UpdateLayout();
            Check(vm.ActiveWorkspace!.Id == originalTabId && !vm.ExplorerCollapsed, "Expanding explorer must not change active tab.");
            Check(Math.Abs(window.ExplorerColumn.ActualWidth - 320) < 1, "Collapse, tab switching and expansion must preserve explorer width.");
            Check(Math.Abs(window.ExplorerTreeRow.ActualHeight - 210) < 1, "Collapse, tab switching and expansion must preserve tree divider.");
            Check(Math.Abs(window.FavoritesColumn.ActualWidth - 350) < 1, "Returning to the original tab must preserve right file pane boundary.");
            Check(vm.Save() && Math.Abs(store.Load().FavoritesRatio!.Value - .6) < .005 && store.Load().FavoritesWidth == 350 && store.Load().ExplorerTreeHeight == 210 && store.Load().ExplorerCollapsed == false && store.Load().ExplorerWidth == 320, "Global expanded state and width must persist.");
            var defaults = new CardEnlargementSettings();
            Check(defaults.FixedSize && defaults.Width == 700 && defaults.Height == 600, "Enlargement defaults must be fixed 700 by 600.");
            vm.FolderColumns = 1; window.FolderWorkspaceScroll.ScrollToTop(); window.UpdateLayout();
            await Task.Delay(200); window.UpdateLayout();
            foreach (var mode in new[] { "A", "B" })
            {
                var wideCard = CardView.FindChild<CardView>(window.FolderCards)!;
                double before = wideCard.ActualWidth;
                Check(before > 600, "Width-floor fixture must be wider than configured enlargement.");
                vm.SetEnlargement(new CardEnlargementSettings { Mode = mode, Width = 600, Height = 600 });
                window.OpenCardEnlargement(wideCard, false); window.UpdateLayout();
                await Task.Delay(200); window.UpdateLayout();
                Check(wideCard.ActualWidth >= before - 1, "Neither enlargement mode may shrink the original card width.");
                window.CloseCardEnlargement(); window.UpdateLayout();
            }
            vm.SetEnlargement(new CardEnlargementSettings { Width = 720, Height = 680 });
            var storedEnlargement = store.Load().Enlargement;
            Check(storedEnlargement.FixedSize && storedEnlargement.Width == 720 && storedEnlargement.Height == 680, "Custom enlargement size must persist.");
            File.WriteAllText(Path.Combine(output, "results.txt"), "PASS: favorites/recent row edges and padding resolve to the context-menu row; no trailing action buttons; hidden WPF hit tests and real scroll offsets for collapsed gutter/toolbar/margins, reverse scroll, card header/list exclusions, favorites/tab exclusions, expanded explorer and workspace label. No physical input.");
        }
        finally { window.Close(); }
    }
}
