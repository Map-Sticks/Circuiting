namespace Circuiting.Models
{
    public class CircuitData
    {
        public Guid CircuitGuid { get; set; }
        public string DisplayLabel { get; set; } = string.Empty;
        public int CircuitNumber { get; set; }
        public int? PoleIndex { get; set; }
        public string Description { get; set; } = string.Empty;
        public string BreakerSize { get; set; } = string.Empty;
        public string ProtectionType { get; set; } = string.Empty;
        public string EarthLeakage { get; set; } = string.Empty;
        public int Poles { get; set; } = 1;
        public List<CircuitedFixture> Fixtures { get; set; } = new List<CircuitedFixture>();
    }

    public class CircuitedFixture
    {
        public Guid FixtureGuid { get; set; }
        public long RevitElementId { get; set; }
        public string RevitUniqueId { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string RoomName { get; set; } = string.Empty;
    }

    public static class CircuitOptions
    {
        public static readonly string[] BreakerSizes =
            { "", "6A", "10A", "16A", "20A", "25A", "32A", "40A", "50A", "63A" };

        public static readonly string[] ProtectionTypes =
            { "", "MCB", "RCD", "RCBO" };

        public static readonly string[] EarthLeakages =
            { "", "10mA", "30mA", "100mA", "300mA" };

        public static readonly int[] PoleOptions = { 1, 2, 3 };
    }
}