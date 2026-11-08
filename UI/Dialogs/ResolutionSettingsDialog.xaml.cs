using System.Windows;
using Circuiting.Models;

namespace Circuiting.UI.Dialogs
{
    public partial class ResolutionSettingsDialog : Window
    {
        private sealed class Option<T>
        {
            public T Value { get; set; } = default!;
            public string Label { get; set; } = string.Empty;
        }

        public ResolutionSettingsDialog(AutomaticResolutionRules rules)
        {
            InitializeComponent();
            var copied = new List<Option<CopiedFixtureAction>>
            {
                new Option<CopiedFixtureAction> { Value = CopiedFixtureAction.ClearAllData, Label = "Clear the copied assignment" },
                new Option<CopiedFixtureAction> { Value = CopiedFixtureAction.AssignToSameCircuit, Label = "Assign the copy to the same circuit" },
                new Option<CopiedFixtureAction> { Value = CopiedFixtureAction.ClearAndNotify, Label = "Clear the copied assignment and notify me" }
            };
            var deleted = new List<Option<DeletedFixtureAction>>
            {
                new Option<DeletedFixtureAction> { Value = DeletedFixtureAction.RemoveFromCircuit, Label = "Remove it from the circuit" },
                new Option<DeletedFixtureAction> { Value = DeletedFixtureAction.RemoveAndNotify, Label = "Remove it from the circuit and notify me" }
            };
            CopiedFixtureCombo.ItemsSource = copied;
            CopiedFixtureCombo.SelectedItem = copied.Single(o => o.Value == rules.OnFixtureCopied);
            DeletedFixtureCombo.ItemsSource = deleted;
            DeletedFixtureCombo.SelectedItem = deleted.Single(o => o.Value == rules.OnFixtureDeleted);
            LogCheck.IsChecked = rules.LogAutomaticActions;
        }

        public CopiedFixtureAction CopiedAction =>
            ((Option<CopiedFixtureAction>)CopiedFixtureCombo.SelectedItem).Value;
        public DeletedFixtureAction DeletedAction =>
            ((Option<DeletedFixtureAction>)DeletedFixtureCombo.SelectedItem).Value;
        public bool LogAutomaticActions => LogCheck.IsChecked == true;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (CopiedFixtureCombo.SelectedItem == null || DeletedFixtureCombo.SelectedItem == null)
            {
                MessageBox.Show("Select both fixture resolution rules.", "Fixture Resolution Rules",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
