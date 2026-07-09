using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrudApp.Services
{
    public class ReadinessHealthCheck : IHealthCheck
    {
        private readonly ReadinessDegradeService _readiness;

        public ReadinessHealthCheck(ReadinessDegradeService readiness)
        {
            _readiness = readiness;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (_readiness.IsDegraded)
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy("Readiness intentionally degraded for test."));
            }

            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}
