using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Circuiting.UI.Dialogs
{
    public class AddBoardDialog : Window
    {
        private TextBox _nameBox;
        private RadioButton _threePhaseRadio;
        private RadioButton _singlePhaseRadio;

        public string BoardName { get; private set; } = string.Empty;
        public bool IsThreePhase { get; private set; } = true;
        public bool Confirmed { get; private set; } = false;

        public AddBoardDialog()
        {
            Title = "Add Distribution Board";
            Width = 380;
            Height = 260;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));

            var mainStack = new StackPanel
            {
                Margin = new Thickness(20)
            };

            // Board Name
            mainStack.Children.Add(new TextBlock
            {
                Text = "Board Name:",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _nameBox = new TextBox
            {
                Text = "DB-L1",
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 16)
            };
            mainStack.Children.Add(_nameBox);

            // Supply Type
            mainStack.Children.Add(new TextBlock
            {
                Text = "Supply:",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _threePhaseRadio = new RadioButton
            {
                Content = "3 Phase",
                Foreground = Brushes.White,
                FontSize = 13,
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 4)
            };
            mainStack.Children.Add(_threePhaseRadio);

            _singlePhaseRadio = new RadioButton
            {
                Content = "Single Phase",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 16)
            };
            mainStack.Children.Add(_singlePhaseRadio);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
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

            var addButton = new Button
            {
                Content = "Add",
                Width = 80,
                Padding = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            addButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_nameBox.Text))
                {
                    MessageBox.Show("Please enter a board name.");
                    return;
                }

                BoardName = _nameBox.Text.Trim();
                IsThreePhase = _threePhaseRadio.IsChecked == true;
                Confirmed = true;
                Close();
            };
            buttonPanel.Children.Add(addButton);

            mainStack.Children.Add(buttonPanel);
            Content = mainStack;
        }
    }
}