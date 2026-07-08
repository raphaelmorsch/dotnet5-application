namespace CrudApp.Models
{
    public class StressRequest
    {
        public int Megabytes { get; set; } = 128;
        public int DurationSeconds { get; set; } = 300;
    }
}
