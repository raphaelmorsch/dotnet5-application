namespace CrudApp.Models
{
    public class CpuStressRequest
    {
        public int PercentCpu { get; set; } = 80;

        public int? Threads { get; set; }

        public int DurationSeconds { get; set; } = 300;
    }
}
