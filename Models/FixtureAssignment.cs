namespace Circuiting.Models
{
    public sealed class FixtureAssignment
    {
        public Guid FixtureGuid { get; set; }
        public Guid BoardGuid { get; set; }
        public Guid ChassisGuid { get; set; }
        public Guid CircuitGuid { get; set; }
        public string CircuitReference { get; set; } = string.Empty;
    }
}
