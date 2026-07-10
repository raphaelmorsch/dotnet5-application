using System;
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
        private readonly CpuStressService _cpuStress;
        private readonly LivenessDegradeService _livenessDegrade;
        private readonly ReadinessDegradeService _readinessDegrade;
        private readonly IConfiguration _configuration;

        public StressController(
            MemoryStressService memoryStress,
            CpuStressService cpuStress,
            LivenessDegradeService livenessDegrade,
            ReadinessDegradeService readinessDegrade,
            IConfiguration configuration)
        {
            _memoryStress = memoryStress;
            _cpuStress = cpuStress;
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
                pod = GetPodName(),
                enabled = true,
                memoryActive = _memoryStress.IsActive,
                allocatedMegabytes = _memoryStress.AllocatedMegabytes,
                cpuActive = _cpuStress.IsActive,
                cpuPercent = _cpuStress.PercentCpu,
                cpuThreads = _cpuStress.ActiveThreads,
                logicalProcessors = Environment.ProcessorCount,
                livenessDegraded = _livenessDegrade.IsDegraded,
                livenessLocallyDegraded = _livenessDegrade.IsLocallyDegraded,
                livenessGloballyDegraded = _livenessDegrade.IsGloballyDegraded,
                readinessDegraded = _readinessDegrade.IsDegraded,
                readinessLocallyDegraded = _readinessDegrade.IsLocallyDegraded,
                readinessGloballyDegraded = _readinessDegrade.IsGloballyDegraded
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

        [HttpPost("cpu")]
        public IActionResult StartCpuStress([FromBody] CpuStressRequest request)
        {
            if (!IsStressEnabled())
            {
                return NotFound();
            }

            if (request.PercentCpu <= 0 || request.PercentCpu > 100)
            {
                return BadRequest("PercentCpu must be between 1 and 100.");
            }

            if (request.Threads.HasValue &&
                (request.Threads.Value <= 0 || request.Threads.Value > 64))
            {
                return BadRequest("Threads must be between 1 and 64.");
            }

            if (request.DurationSeconds <= 0 || request.DurationSeconds > 3600)
            {
                return BadRequest("DurationSeconds must be between 1 and 3600.");
            }

            _ = _cpuStress.StartAsync(request.PercentCpu, request.Threads, request.DurationSeconds);

            var threads = request.Threads ?? Math.Max(1,
                (int)Math.Ceiling(Environment.ProcessorCount * request.PercentCpu / 100.0));

            return Accepted(new
            {
                message = "CPU stress started.",
                request.PercentCpu,
                threads,
                logicalProcessors = Environment.ProcessorCount,
                request.DurationSeconds,
                autoRelease = true
            });
        }

        [HttpDelete("cpu")]
        public IActionResult StopCpuStress()
        {
            if (!IsStressEnabled())
            {
                return NotFound();
            }

            _cpuStress.Stop();

            return Ok(new { message = "CPU stress stopped." });
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
                pod = GetPodName(),
                message = "Liveness probe will fail on this pod until restored.",
                healthEndpoint = "/health/live",
                restore = "DELETE /api/stress/liveness/degrade",
                multiPodHint = "Com vários pods, repita o DELETE até todos estarem saudáveis."
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

            return Ok(new
            {
                pod = GetPodName(),
                message = "Liveness probe restored on this pod.",
                livenessDegraded = _livenessDegrade.IsDegraded
            });
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
                pod = GetPodName(),
                message = "Readiness probe will fail on this pod until restored.",
                healthEndpoint = "/health/ready",
                restore = "DELETE /api/stress/readiness/degrade",
                multiPodHint = "Com vários pods, repita o DELETE até todos estarem Ready."
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

            return Ok(new
            {
                pod = GetPodName(),
                message = "Readiness probe restored on this pod.",
                readinessDegraded = _readinessDegrade.IsDegraded
            });
        }

        private static string GetPodName() =>
            Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;

        private bool IsStressEnabled()
        {
            return _configuration.GetValue<bool>("StressTest:Enabled");
        }
    }
}
