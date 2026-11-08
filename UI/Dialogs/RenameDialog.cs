using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Circuiting.UI.Dialogs
{
    public class RenameDialog : Window
    {
        private TextBox _nameBox;

        public string NewName { get; private set; } = string.Empty;
        public bool Confirmed { get; private set; }

        public RenameDialog(string title, string currentName)
        {
            Title = title;
            Width = 350;
            Height = 160;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(30, 30, 30));

            StackPanel main = new StackPanel
            {
                Margin = new Thickness(20)
            };

            main.Children.Add(new TextBlock
            {
                Text = "Name:",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _nameBox = new TextBox
            {
                Text = currentName,
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 16)
            };
            _nameBox.SelectAll();
            main.Children.Add(_nameBox);

            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Padding = new Thickness(0, 4, 0, 4),
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (s, e) =>
            {
                Confirmed = false;
                Close();
            };
            buttonPanel.Children.Add(cancelButton);

            Button okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Padding = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            okButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_nameBox.Text))
                {
                    MessageBox.Show("Please enter a name.");
                    return;
                }

                NewName = _nameBox.Text.Trim();
                Confirmed = true;
                Close();
            };
            buttonPanel.Children.Add(okButton);

            main.Children.Add(buttonPanel);
            Content = main;
        }
    }
}