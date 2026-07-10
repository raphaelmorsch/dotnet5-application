using Microsoft.Extensions.Configuration;

namespace CrudApp.Services
{
    public class ReadinessDegradeService
    {
        private bool _localDegraded;
        private readonly IConfiguration _configuration;

        public ReadinessDegradeService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public bool IsDegraded =>
            _localDegraded || _configuration.GetValue<bool>("StressTest:ReadinessDegraded");

        public bool IsLocallyDegraded => _localDegraded;

        public bool IsGloballyDegraded => _configuration.GetValue<bool>("StressTest:ReadinessDegraded");

        public void Degrade() => _localDegraded = true;

        public void Restore() => _localDegraded = false;
    }
}
