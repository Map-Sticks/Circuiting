using Circuiting.Models;

namespace Circuiting.ViewModels
{
    public static class CircuitFieldOptions
    {
        public static string[] IncludeCurrent(string[] options, string? current)
        {
            if (current == null || current.Length == 0 || options.Contains(current)) return options;
            return options.Concat(new[] { current }).ToArray();
        }

        public static int[] Poles(bool allowMultiPole, int? existing)
        {
            if (allowMultiPole) return CircuitOptions.PoleOptions;
            return existing > 1 ? new[] { 1, existing.Value } : new[] { 1 };
        }
    }
}
