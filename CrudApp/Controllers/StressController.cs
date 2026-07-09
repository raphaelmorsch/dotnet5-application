using CrudApp.Models;
using CrudApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace CrudApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StressController : ControllerBase
    {
        private readonly MemoryStressService _memoryStress;
        private readonly LivenessDegradeService _livenessDegrade;
        private readonly ReadinessDegradeService _readinessDegrade;
        private readonly IConfiguration _configuration;

        public StressController(
            MemoryStressService memoryStress,
            LivenessDegradeService livenessDegrade,
            ReadinessDegradeService readinessDegrade,
            IConfiguration configuration)
        {
            _memoryStress = memoryStress;
            _livenessDegrade = livenessDegrade;
            _readinessDegrade = readinessDegrade;
            _configuration = configuration;
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            if (!IsStressEnabled())
            {
                return NotFound();
            }

            return Ok(new
            {
                enabled = true,
                memoryActive = _memoryStress.IsActive,
                allocatedMegabytes = _memoryStress.AllocatedMegabytes,
                livenessDegraded = _livenessDegrade.IsDegraded,
                readinessDegraded = _readinessDegrade.IsDegraded
            });
        }

        [HttpPost("memory")]
        public IActionResult StartMemoryStress([FromBody] StressRequest request)
        {
            if (!IsStressEnabled())
            {
                return NotFound();
            }

            if (request.Megabytes <= 0 || request.Megabytes > 1024)
            {
                return BadRequest("Megabytes must be between 1 and 1024.");
            }

            if (request.DurationSeconds <= 0 || request.DurationSeconds > 3600)
            {
                return BadRequest("DurationSeconds must be between 1 and 3600.");
            }

            _ = _memoryStress.AllocateAsync(request.Megabytes, request.DurationSeconds);

            return Accepted(new
            {
                message = "Memory stress started.",
                request.Megabytes,
                request.DurationSeconds,
                autoRelease = true
            });
        }

        [HttpDelete("memory")]
        public IActionResult StopMemoryStress()
        {
            if (!IsStressEnabled())
            {
                return NotFound();
            }

            _memoryStress.Release();

            return Ok(new { message = "Memory stress released." });
        }

        [HttpPost("liveness/degrade")]
        [HttpGet("liveness/degrade")]
        public IActionResult DegradeLiveness()
        {
            if (!IsStressEnabled())
            {
                return NotFound();
            }

            _livenessDegrade.Degrade();

            return Accepted(new
            {
                message = "Liveness probe will fail until restored.",
                healthEndpoint = "/health/live",
                restore = "DELETE /api/stress/liveness/degrade"
            });
        }

        [HttpDelete("liveness/degrade")]
        public IActionResult RestoreLiveness()
        {
            if (!IsStressEnabled())
            {
                return NotFound();
            }

            _livenessDegrade.Restore();

            return Ok(new { message = "Liveness probe restored." });
        }

        [HttpPost("readiness/degrade")]
        [HttpGet("readiness/degrade")]
        public IActionResult DegradeReadiness()
        {
            if (!IsStressEnabled())
            {
                return NotFound();
            }

            _readinessDegrade.Degrade();

            return Accepted(new
            {
                message = "Readiness probe will fail until restored.",
                healthEndpoint = "/health/ready",
                restore = "DELETE /api/stress/readiness/degrade"
            });
        }

        [HttpDelete("readiness/degrade")]
        public IActionResult RestoreReadiness()
        {
            if (!IsStressEnabled())
            {
                return NotFound();
            }

            _readinessDegrade.Restore();

            return Ok(new { message = "Readiness probe restored." });
        }

        private bool IsStressEnabled()
        {
            return _configuration.GetValue<bool>("StressTest:Enabled");
        }
    }
}
