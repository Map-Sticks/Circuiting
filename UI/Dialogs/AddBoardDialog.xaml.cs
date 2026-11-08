using System.Windows;
using System.Windows.Controls;
using Circuiting.Models;
using Circuiting.Services;

namespace Circuiting.UI.Dialogs
{
    public partial class AddBoardDialog : Window
    {
        private static readonly int[] StandardPoles = { 6, 12, 18, 24, 30, 36, 42, 48 };
        private static readonly BoardData NewSetup = new BoardData { DisplayName = "— New setup —" };

        private readonly IReadOnlyList<string> _existingNames;
        private readonly BoardData? _editing;
        private List<ChassisData> _chassis = new List<ChassisData>();
        private bool _updating = true;

        /// <param name="editing">
        /// When set, the dialog edits a deep copy of this board, keeping its identity, circuits and fixtures.
        /// </param>
        public AddBoardDialog(IReadOnlyList<BoardData>? existingBoards = null, BoardData? template = null,
            BoardData? editing = null)
        {
            InitializeComponent();
            _editing = editing;
            IReadOnlyList<BoardData> boards = existingBoards ?? Array.Empty<BoardData>();
            _existingNames = boards.Where(b => !ReferenceEquals(b, editing)).Select(b => b.DisplayName).ToList();
            if (editing != null)
            {
                Title = $"Edit Board — {editing.DisplayName}";
                ConfirmButton.Content = "Save";
                TemplatePanel.Visibility = System.Windows.Visibility.Collapsed;
                NameBox.Text = editing.DisplayName;
                _chassis = BoardSetup.Clone(editing).Chassis;
                bool threePhase = editing.Supply == SupplyType.ThreePhase;
                if (_chassis.Count == 0) BoardSetup.Resize(_chassis, 1, threePhase);
                ThreePhaseRadio.IsChecked = threePhase;
                SinglePhaseRadio.IsChecked = !threePhase;
                ChassisCountCombo.ItemsSource =
                    Enumerable.Range(1, Math.Max(BoardSetup.MaxChassis, _chassis.Count)).ToList();
                LoadQuickSetup();
                _updating = false;
                RefreshList();
            }
            else
            {
                (string prefix, string start) = BoardSetup.SuggestNumbering(
                    boards.Count > 0 ? boards[boards.Count - 1].DisplayName : "DB-L0", _existingNames);
                NameBox.Text = prefix + start;
                ChassisCountCombo.ItemsSource = Enumerable.Range(1, BoardSetup.MaxChassis).ToList();
                TemplateCombo.ItemsSource = new[] { NewSetup }.Concat(boards).ToList();
                _updating = false;
                TemplateCombo.SelectedItem = template != null && boards.Contains(template) ? template : NewSetup;
            }
            NameBox.SelectAll();
            NameBox.Focus();
        }

        public BoardData? Result { get; private set; }
        public bool Confirmed { get; private set; }

        private bool IsThreePhase => ThreePhaseRadio.IsChecked == true;

        private void TemplateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || TemplateCombo.SelectedItem is not BoardData template) return;
            _updating = true;
            try
            {
                if (ReferenceEquals(template, NewSetup))
                {
                    _chassis = new List<ChassisData>();
                    BoardSetup.Resize(_chassis, 1, IsThreePhase);
                }
                else
                {
                    _chassis = BoardSetup.CopySetup(template, string.Empty).Chassis;
                    if (_chassis.Count == 0) BoardSetup.Resize(_chassis, 1, template.Supply == SupplyType.ThreePhase);
                    ThreePhaseRadio.IsChecked = template.Supply == SupplyType.ThreePhase;
                    SinglePhaseRadio.IsChecked = template.Supply != SupplyType.ThreePhase;
                }
                LoadQuickSetup();
            }
            finally
            {
                _updating = false;
            }
            RefreshList();
        }

        private void LoadQuickSetup()
        {
            ChassisData first = _chassis[0];
            ChassisCountCombo.SelectedItem = Math.Min(_chassis.Count, BoardSetup.MaxChassis);
            var poles = StandardPoles.ToList();
            foreach (ChassisData chassis in _chassis)
                if (!poles.Contains(chassis.MaxPoles)) poles.Add(chassis.MaxPoles);
            poles.Sort();
            PolesCombo.ItemsSource = poles;
            PolesCombo.SelectedItem = first.MaxPoles;
            PrefixBox.Text = first.Prefix;
            ComboBoxItem? separator = SeparatorCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => (string)item.Tag == first.Separator);
            if (separator == null)
            {
                separator = new ComboBoxItem { Content = $"Custom ( {first.Separator} )", Tag = first.Separator };
                SeparatorCombo.Items.Add(separator);
            }
            SeparatorCombo.SelectedItem = separator;
        }

        private void Supply_Checked(object sender, RoutedEventArgs e)
        {
            if (_updating || ChassisList == null) return;
            BoardSetup.ApplySupply(_chassis, IsThreePhase);
            RefreshList();
        }

        private void ChassisCountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating || ChassisCountCombo.SelectedItem is not int count) return;
            if (count < _chassis.Count && !ConfirmRemoval(_chassis.Skip(count).ToList()))
            {
                SetCountSilently();
                return;
            }
            try
            {
                BoardSetup.Resize(_chassis, count, IsThreePhase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException)
            {
                MessageBox.Show(ex.Message, "Chassis", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            RefreshList();
        }

        private void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            if (PolesCombo.SelectedItem is not int poles) return;
            try
            {
                BoardSetup.ApplyToAll(_chassis, poles, PrefixBox.Text.Trim(),
                    (SeparatorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException)
            {
                MessageBox.Show(ex.Message, "Invalid Chassis", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            RefreshList();
        }

        private void EditChassis_Click(object sender, RoutedEventArgs e) => EditSelected();

        private void ChassisList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            EditSelected();

        private void EditSelected()
        {
            int index = ChassisList.SelectedIndex;
            if (index < 0 || index >= _chassis.Count)
            {
                MessageBox.Show("Select a chassis to edit.", "Edit Chassis");
                return;
            }
            var preview = new BoardData { Chassis = _chassis.ToList() };
            var dialog = new ChassisConfigDialog(_chassis[index], allowThreePhase: IsThreePhase, board: preview)
            {
                Owner = this
            };
            dialog.ShowDialog();
            if (!dialog.Confirmed || dialog.Result == null) return;
            _chassis[index] = dialog.Result;
            RefreshList(index);
        }

        private void RemoveChassis_Click(object sender, RoutedEventArgs e)
        {
            int index = ChassisList.SelectedIndex;
            if (index < 0 || index >= _chassis.Count)
            {
                MessageBox.Show("Select a chassis to remove.", "Remove Chassis");
                return;
            }
            if (_chassis.Count == 1)
            {
                MessageBox.Show("A board needs at least one chassis.", "Remove Chassis");
                return;
            }
            if (!ConfirmRemoval(new[] { _chassis[index] })) return;
            _chassis.RemoveAt(index);
            SetCountSilently();
            RefreshList(Math.Min(index, _chassis.Count - 1));
        }

        private bool ConfirmRemoval(IReadOnlyCollection<ChassisData> removed)
        {
            int circuits = removed.Sum(c => c.Circuits.Count);
            if (circuits == 0) return true;
            int fixtures = removed.Sum(c => c.Circuits.Sum(circuit => circuit.Fixtures.Count));
            string names = string.Join(", ", removed.Select(c => $"'{c.Name}'"));
            return MessageBox.Show(
                $"Remove chassis {names}?\n\n{circuits} circuit(s) and {fixtures} fixture assignment(s) " +
                "will be deleted when you save.", "Remove Chassis",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private void SetCountSilently()
        {
            _updating = true;
            ChassisCountCombo.SelectedItem = _chassis.Count;
            _updating = false;
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
        private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

        private void MoveSelected(int offset)
        {
            int index = ChassisList.SelectedIndex;
            int target = index + offset;
            if (index < 0 || target < 0 || target >= _chassis.Count) return;
            (_chassis[index], _chassis[target]) = (_chassis[target], _chassis[index]);
            RefreshList(target);
        }

        private void RefreshList(int selected = -1)
        {
            var board = new BoardData { Chassis = _chassis };
            ChassisList.ItemsSource = _chassis.Select((chassis, i) => Describe(board, chassis, i)).ToList();
            ChassisList.SelectedIndex = selected;
        }

        private static string Describe(BoardData board, ChassisData chassis, int index)
        {
            string phase = chassis.PhaseLabels.Count >= 3 ? "3φ" : "1φ";
            string range;
            try
            {
                List<PoleAssignment> labels = CircuitLabelCalculator.Calculate(board, chassis);
                range = labels.Count == 0 ? "-" : $"{labels[0].DisplayLabel} … {labels[labels.Count - 1].DisplayLabel}";
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException)
            {
                range = "invalid: " + ex.Message;
            }
            string circuits = chassis.Circuits.Count == 0 ? string.Empty : $"  ({chassis.Circuits.Count} circuits)";
            return $"{index + 1,2}. {chassis.Name,-12} {phase} {chassis.MaxPoles,2}p  {range}{circuits}";
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            string name = NameBox.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("Please enter a board name.");
                return;
            }
            if (BoardSetup.NameExists(_existingNames, name))
            {
                MessageBox.Show($"A board named '{name}' already exists. Choose a unique name.",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var board = new BoardData
            {
                BoardGuid = _editing?.BoardGuid ?? Guid.NewGuid(),
                DisplayName = name,
                Supply = IsThreePhase ? SupplyType.ThreePhase : SupplyType.SinglePhase,
                Chassis = _chassis
            };
            string? error = BoardSetup.ValidateLayout(board, _editing);
            if (error != null)
            {
                MessageBox.Show(error, "Invalid Board Layout", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshList(ChassisList.SelectedIndex);
                return;
            }
            Result = board;
            Confirmed = true;
            DialogResult = true;
        }
    }
}
