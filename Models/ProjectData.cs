namespace Circuiting.Models
{
    public sealed class ProjectData
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public ProjectSettings Settings { get; set; } = new ProjectSettings();
        public List<BoardData> Boards { get; set; } = new List<BoardData>();
    }
}
