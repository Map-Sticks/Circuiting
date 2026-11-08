namespace Circuiting.ViewModels
{
    public static class ChassisFieldOptions
    {
        public static bool HasLegacyPhaseLabels(IReadOnlyList<string>? labels) =>
            labels != null && labels.Count > 0 && labels.Count < 3;

        public static List<string> PhaseLabels(bool singlePhase, string text,
            IReadOnlyList<string>? existing, bool removeLegacy)
        {
            if (singlePhase)
            {
                if (!removeLegacy && existing != null && HasLegacyPhaseLabels(existing))
                    return existing.ToList();
                return new List<string>();
            }

            if (existing != null && string.Equals(text, string.Join(",", existing), StringComparison.Ordinal))
                return existing.ToList();
            return text.Split(',').Select(label => label.Trim())
                .Where(label => !string.IsNullOrEmpty(label)).ToList();
        }
    }
}
