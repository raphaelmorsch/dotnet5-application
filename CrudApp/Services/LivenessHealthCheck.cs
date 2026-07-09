using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrudApp.Services
{
    public class LivenessHealthCheck : IHealthCheck
    {
        private readonly LivenessDegradeService _liveness;

        public LivenessHealthCheck(LivenessDegradeService liveness)
        {
            _liveness = liveness;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (_liveness.IsDegraded)
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy("Liveness intentionally degraded for test."));
            }

            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}
