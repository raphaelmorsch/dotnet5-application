namespace CrudApp.Services
{
    public class ReadinessDegradeService
    {
        public bool IsDegraded { get; private set; }

        public void Degrade() => IsDegraded = true;

        public void Restore() => IsDegraded = false;
    }
}
