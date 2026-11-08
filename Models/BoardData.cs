namespace Circuiting.Models
{
    public class BoardData
    {
        public Guid BoardGuid { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public SupplyType Supply { get; set; }
        public List<ChassisData> Chassis { get; set; } = new List<ChassisData>();
    }

    public enum SupplyType
    {
        ThreePhase,
        SinglePhase
    }
}