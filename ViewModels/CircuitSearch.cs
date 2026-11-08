using Circuiting.Models;

namespace Circuiting.ViewModels
{
    public enum CircuitSearchScope
    {
        All,
        Boards,
        Chassis,
        Circuits
    }

    public sealed class CircuitSearch
    {
        public static readonly CircuitSearch None = new CircuitSearch(null, CircuitSearchScope.All);

        private readonly string[] _terms;

        public CircuitSearch(string? text, CircuitSearchScope scope)
        {
            Text = text?.Trim() ?? string.Empty;
            Scope = scope;
            _terms = Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        }

        public string Text { get; }
        public CircuitSearchScope Scope { get; }
        public bool IsActive => _terms.Length > 0;

        public bool MatchesBoard(BoardData board) =>
            IsActive && Includes(CircuitSearchScope.Boards) && Matches(board.DisplayName);

        public bool MatchesChassis(ChassisData chassis) =>
            IsActive && Includes(CircuitSearchScope.Chassis) && Matches(chassis.Name, chassis.Prefix);

        public bool MatchesCircuit(CircuitData circuit) =>
            IsActive && Includes(CircuitSearchScope.Circuits) && Matches(circuit.DisplayLabel,
                circuit.CircuitNumber.ToString(), circuit.Description, circuit.BreakerSize,
                circuit.ProtectionType, circuit.EarthLeakage);

        private bool Includes(CircuitSearchScope scope) => Scope == CircuitSearchScope.All || Scope == scope;

        // Every term must appear in at least one field, so "16A RCBO" finds circuits with both.
        private bool Matches(params string?[] fields) =>
            _terms.All(term => fields.Any(field =>
                field != null && field.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
    }
}
