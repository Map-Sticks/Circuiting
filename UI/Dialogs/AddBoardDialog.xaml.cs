using System.Windows;

namespace Circuiting.UI.Dialogs
{
    public partial class AddBoardDialog : Window
    {
        public AddBoardDialog()
        {
            InitializeComponent();
        }

        public string BoardName { get; private set; } = string.Empty;
        public bool IsThreePhase { get; private set; } = true;
        public bool Confirmed { get; private set; }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Please enter a board name.");
                return;
            }
            BoardName = NameBox.Text.Trim();
            IsThreePhase = ThreePhaseRadio.IsChecked == true;
            Confirmed = true;
            DialogResult = true;
        }
    }
}
