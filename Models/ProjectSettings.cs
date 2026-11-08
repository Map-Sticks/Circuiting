namespace Circuiting.Models
{
    public class ProjectSettings
    {
        public ResolutionMode ResolutionMode { get; set; } = ResolutionMode.Automatic;
        public AutomaticResolutionRules AutoRules { get; set; } = new AutomaticResolutionRules();
        public string ModeChangedBy { get; set; } = string.Empty;
        public DateTime ModeLastChanged { get; set; } = DateTime.Now;
    }

    public enum ResolutionMode
    {
        Automatic,
        Manual
    }

    public class AutomaticResolutionRules
    {
        public CopiedFixtureAction OnFixtureCopied { get; set; }
            = CopiedFixtureAction.ClearAllData;
        public CopiedBoardAction OnBoardCopied { get; set; }
            = CopiedBoardAction.RegisterAsNewBoard;
        public DeletedFixtureAction OnFixtureDeleted { get; set; }
            = DeletedFixtureAction.RemoveFromCircuit;
        public bool LogAutomaticActions { get; set; } = true;
    }

    public enum CopiedFixtureAction
    {
        ClearAllData,
        AssignToSameCircuit,
        ClearAndNotify
    }

    public enum CopiedBoardAction
    {
        RegisterAsNewBoard,
        StripAllData,
        RegisterAsNewBoardAndNotify
    }

    public enum DeletedFixtureAction
    {
        RemoveFromCircuit,
        RemoveAndNotify
    }
}