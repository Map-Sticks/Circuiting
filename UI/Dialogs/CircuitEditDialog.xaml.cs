using System.Windows;
using Circuiting.Models;

namespace Circuiting.UI.Dialogs
{
    public partial class CircuitEditDialog : Window
    {
        public CircuitEditDialog(string leftLabel, string rightLabel, CircuitData? leftExisting,
            CircuitData? rightExisting, bool allowMultiPole = true)
        {
            InitializeComponent();
            Title = $"Edit Circuit Row — {leftLabel} / {rightLabel}";
            LeftFields.Configure("LEFT", leftLabel, leftExisting, allowMultiPole);
            RightFields.Configure("RIGHT", rightLabel, rightExisting, allowMultiPole);
        }

        public CircuitData? LeftResult { get; private set; }
        public CircuitData? RightResult { get; private set; }
        public bool Confirmed { get; private set; }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (LeftFields.HasData) LeftResult = LeftFields.CreateResult();
            if (RightFields.HasData) RightResult = RightFields.CreateResult();
            Confirmed = true;
            DialogResult = true;
        }
    }
}
