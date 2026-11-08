using Circuiting.ViewModels;

namespace Circuiting.Views
{
    public sealed partial class CircuitingView
    {
        public CircuitingView(CircuitingViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}