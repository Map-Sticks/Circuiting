using System.Windows;

namespace Circuiting.UI.Dialogs
{
    public partial class RenameDialog : Window
    {
        public RenameDialog(string title, string currentName)
        {
            InitializeComponent();
            Title = title;
            NameBox.Text = currentName;
            NameBox.SelectAll();
        }

        public string NewName { get; private set; } = string.Empty;
        public bool Confirmed { get; private set; }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Please enter a name.");
                return;
            }
            NewName = NameBox.Text.Trim();
            Confirmed = true;
            DialogResult = true;
        }
    }
}
