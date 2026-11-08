namespace Circuiting.Models
{
    public class ChassisData
    {
        public Guid ChassisGuid { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MaxPoles { get; set; }
        public int StartingCircuit { get; set; } = 1;
        public string Prefix { get; set; } = string.Empty;
        public string Separator { get; set; } = string.Empty;
        public bool ShowPhaseLabel { get; set; } = true;
        public bool ContinuePhaseFromPrevious { get; set; } = false;
        public List<string> PhaseLabels { get; set; } = new List<string>();
        public List<CircuitData> Circuits { get; set; } = new List<CircuitData>();
    }
}