using System.Windows;
using Circuiting.Models;

namespace Circuiting.UI.Dialogs
{
    /// <summary>Collects the details of a circuit created by moving an element onto a spare or space.</summary>
    public partial class NewCircuitDialog : Window
    {
        public NewCircuitDialog(string location, string label, CircuitData? spare, string intro)
        {
            InitializeComponent();
            Title = $"New Circuit — {label}";
            IntroText.Text = intro;
            Fields.Configure("NEW CIRCUIT", $"{label}  ({location})", spare, allowMultiPole: false);
            Fields.LockPoles();
        }

        public CircuitData? Result { get; private set; }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            Result = Fields.CreateResult();
            DialogResult = true;
        }
    }
}
