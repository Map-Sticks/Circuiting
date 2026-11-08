using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Circuiting.ViewModels;

namespace Circuiting.UI.DockablePanel
{
    public partial class CircuitManagerPane
    {
        private CircuitSearch _search = CircuitSearch.None;
        // The user's own expansion state while a search forces matches open; restored when cleared.
        private HashSet<Guid>? _expandedBeforeSearch;
        private DispatcherTimer? _searchTimer;

        private CircuitSearchScope SelectedSearchScope =>
            (SearchScopeCombo?.SelectedItem as ComboBoxItem)?.Tag is string tag &&
            Enum.TryParse(tag, out CircuitSearchScope scope) ? scope : CircuitSearchScope.All;

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchTimer == null)
            {
                _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _searchTimer.Tick += (_, _) => ApplySearch();
            }
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplySearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ClearSearch();
                e.Handled = true;
            }
        }

        private void SearchScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplySearch();

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e) => ClearSearch();

        private void ClearSearch()
        {
            SearchBox.Clear();
            ApplySearch();
        }

        private void ApplySearch()
        {
            _searchTimer?.Stop();
            // SelectionChanged fires while InitializeComponent is still creating controls.
            if (SearchBox == null || SearchScopeCombo == null || BoardTree == null) return;
            var next = new CircuitSearch(SearchBox.Text, SelectedSearchScope);
            if (next.Text == _search.Text && next.Scope == _search.Scope) return;
            HashSet<Guid> expanded = GetExpandedGuids();
            _search = next;
            _expandedBeforeSearch = next.IsActive ? expanded : null;
            if (_dataLoaded) RefreshTree(expanded);
        }
    }
}
