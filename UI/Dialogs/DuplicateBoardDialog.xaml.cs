using System.Windows;
using System.Windows.Controls;
using Circuiting.Models;
using Circuiting.Services;

namespace Circuiting.UI.Dialogs
{
    public partial class DuplicateBoardDialog : Window
    {
        private readonly IReadOnlyList<string> _existingNames;
        private bool _initializing = true;

        public DuplicateBoardDialog(BoardData source, IEnumerable<string> existingNames)
        {
            InitializeComponent();
            _existingNames = existingNames.ToList();
            int circuits = source.Chassis.Sum(c => c.Circuits.Count);
            SourceText.Text = $"Copy the setup of '{source.DisplayName}' ({source.Chassis.Count} chassis, " +
                $"{(source.Supply == SupplyType.ThreePhase ? "3 phase" : "single phase")}). " +
                $"Its {circuits} circuit(s) and fixture assignments are not copied.";
            (string prefix, string start) = BoardSetup.SuggestNumbering(source.DisplayName, _existingNames);
            PrefixBox.Text = prefix;
            StartBox.Text = start;
            _initializing = false;
            UpdatePreview();
            CountBox.SelectAll();
            CountBox.Focus();
        }

        public List<string> Names { get; private set; } = new List<string>();

        private void Input_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            if (_initializing) return;
            try
            {
                if (!int.TryParse(CountBox.Text.Trim(), out int count))
                    throw new ArgumentException($"Enter a number of copies from 1 to {BoardSetup.MaxCopies}.");
                if (PrefixBox.Text.Trim().Length == 0 && StartBox.Text.Trim().Length == 0)
                    throw new ArgumentException("Enter a name prefix or start number.");
                Names = BoardSetup.UniqueNames(PrefixBox.Text.TrimStart(), StartBox.Text, count, _existingNames);
                PreviewList.ItemsSource = Names;
                DuplicateButton.IsEnabled = true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException)
            {
                Names = new List<string>();
                PreviewList.ItemsSource = new[] { ex is ArgumentOutOfRangeException
                    ? $"Enter a number of copies from 1 to {BoardSetup.MaxCopies}." : FirstLine(ex.Message) };
                DuplicateButton.IsEnabled = false;
            }
        }

        private static string FirstLine(string message) =>
            message.Split(new[] { " (Parameter" }, StringSplitOptions.None)[0];

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (Names.Count == 0) return;
            DialogResult = true;
        }
    }
}
