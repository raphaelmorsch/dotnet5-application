using Microsoft.Extensions.Configuration;

namespace CrudApp.Services
{
    public class LivenessDegradeService
    {
        private bool _localDegraded;
        private readonly IConfiguration _configuration;

        public LivenessDegradeService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public bool IsDegraded =>
            _localDegraded || _configuration.GetValue<bool>("StressTest:LivenessDegraded");

        public bool IsLocallyDegraded => _localDegraded;

        public bool IsGloballyDegraded => _configuration.GetValue<bool>("StressTest:LivenessDegraded");

        public void Degrade() => _localDegraded = true;

        public void Restore() => _localDegraded = false;
    }
}
