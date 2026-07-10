using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrudApp.Services
{
    public class CpuStressService
    {
        private readonly object _lock = new();
        private CancellationTokenSource? _releaseCts;
        private Task[]? _workers;

        public int ActiveThreads { get; private set; }

        public int PercentCpu { get; private set; }

        public bool IsActive => ActiveThreads > 0;

        public Task StartAsync(int percentCpu, int? threads, int durationSeconds)
        {
            if (percentCpu <= 0 || percentCpu > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(percentCpu));
            }

            var workerCount = threads ?? Math.Max(1,
                (int)Math.Ceiling(Environment.ProcessorCount * percentCpu / 100.0));

            lock (_lock)
            {
                StopInternal();

                PercentCpu = percentCpu;
                ActiveThreads = workerCount;
                _releaseCts = new CancellationTokenSource();
                var token = _releaseCts.Token;

                _workers = new Task[workerCount];
                for (var i = 0; i < workerCount; i++)
                {
                    _workers[i] = Task.Run(() => BurnCpu(token), token);
                }
            }

            return ScheduleStopAsync(durationSeconds, _releaseCts!.Token);
        }

        public void Stop()
        {
            lock (_lock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            _releaseCts?.Cancel();
            _releaseCts?.Dispose();
            _releaseCts = null;

            if (_workers != null)
            {
                try
                {
                    Task.WaitAll(_workers, TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                    // Workers cancelled during shutdown.
                }

                _workers = null;
            }

            ActiveThreads = 0;
            PercentCpu = 0;
        }

        private static void BurnCpu(CancellationToken cancellationToken)
        {
            var x = 1.0;
            while (!cancellationToken.IsCancellationRequested)
            {
                for (var i = 0; i < 50000; i++)
                {
                    x = Math.Sqrt(x + i) * Math.Sin(x);
                }
            }
        }

        private async Task ScheduleStopAsync(int durationSeconds, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds), cancellationToken);
                Stop();
            }
            catch (TaskCanceledException)
            {
                // Nova sessão ou stop manual cancelou o timer.
            }
        }
    }
}
