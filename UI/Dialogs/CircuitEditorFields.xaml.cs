using System.Windows.Controls;
using Circuiting.Models;
using Circuiting.ViewModels;

namespace Circuiting.UI.Dialogs
{
    public partial class CircuitEditorFields : UserControl
    {
        private CircuitData? _existing;
        private string _label = string.Empty;
        private string[] _breakerOptions = CircuitOptions.BreakerSizes;
        private string[] _protectionOptions = CircuitOptions.ProtectionTypes;
        private string[] _leakageOptions = CircuitOptions.EarthLeakages;

        public CircuitEditorFields()
        {
            InitializeComponent();
        }

        public void Configure(string side, string label, CircuitData? existing, bool allowMultiPole)
        {
            _existing = existing;
            _label = label;
            HeaderText.Text = $"{side} — {label}";
            DescriptionBox.Text = existing?.Description ?? string.Empty;
            _breakerOptions = CircuitFieldOptions.IncludeCurrent(CircuitOptions.BreakerSizes, existing?.BreakerSize);
            _protectionOptions = CircuitFieldOptions.IncludeCurrent(CircuitOptions.ProtectionTypes, existing?.ProtectionType);
            _leakageOptions = CircuitFieldOptions.IncludeCurrent(CircuitOptions.EarthLeakages, existing?.EarthLeakage);
            SetOptions(BreakerCombo, _breakerOptions, existing?.BreakerSize);
            SetOptions(ProtectionCombo, _protectionOptions, existing?.ProtectionType);
            SetOptions(LeakageCombo, _leakageOptions, existing?.EarthLeakage);
            PolesCombo.ItemsSource = CircuitFieldOptions.Poles(allowMultiPole, existing?.Poles);
            PolesCombo.SelectedItem = existing?.Poles ?? 1;
            if (PolesCombo.SelectedIndex < 0) PolesCombo.SelectedIndex = 0;
            PolesCombo.IsEnabled = allowMultiPole || existing?.Poles > 1;
        }

        public bool HasData => _existing != null || !string.IsNullOrWhiteSpace(DescriptionBox.Text) ||
            BreakerCombo.SelectedIndex > 0 || ProtectionCombo.SelectedIndex > 0 ||
            LeakageCombo.SelectedIndex > 0 || (PolesCombo.SelectedItem is int poles && poles > 1);

        public CircuitData CreateResult()
        {
            return new CircuitData
            {
                CircuitGuid = _existing?.CircuitGuid ?? Guid.NewGuid(),
                DisplayLabel = _label,
                CircuitNumber = _existing?.CircuitNumber ?? 0,
                PoleIndex = _existing?.PoleIndex,
                Description = DescriptionBox.Text.Trim(),
                BreakerSize = Value(BreakerCombo, _breakerOptions),
                ProtectionType = Value(ProtectionCombo, _protectionOptions),
                EarthLeakage = Value(LeakageCombo, _leakageOptions),
                Poles = PolesCombo.SelectedItem is int poles
                    ? poles : throw new InvalidOperationException("Select a pole count."),
                Fixtures = _existing?.Fixtures ?? new List<CircuitedFixture>()
            };
        }

        private static void SetOptions(ComboBox combo, string[] options, string? currentValue)
        {
            combo.ItemsSource = options.Select(value => string.IsNullOrEmpty(value) ? "(none)" : value).ToArray();
            int index = Array.IndexOf(options, currentValue ?? string.Empty);
            combo.SelectedIndex = index >= 0 ? index : 0;
        }

        private static string Value(ComboBox combo, string[] options)
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= options.Length)
                throw new InvalidOperationException("A circuit option is not selected.");
            return options[combo.SelectedIndex];
        }
    }
}
