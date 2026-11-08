namespace Circuiting.ViewModels
{
    /// <summary>
    /// Live model information about a circuited element, read from Revit when circuit data is loaded.
    /// It is not stored; stored fixture data is used as a fallback when no details are available.
    /// </summary>
    public sealed class FixtureDetails
    {
        public bool Found { get; set; }
        public long ElementId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string Mark { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public string SpaceNumber { get; set; } = string.Empty;
        public string SpaceName { get; set; } = string.Empty;
        public string RoomName { get; set; } = string.Empty;

        public string Space => $"{SpaceNumber} {SpaceName}".Trim();
    }
}
