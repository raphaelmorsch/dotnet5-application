using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrudApp.Services
{
    public class MemoryStressService
    {
        private readonly List<byte[]> _buffers = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _releaseCts;

        public int AllocatedMegabytes { get; private set; }

        public bool IsActive => AllocatedMegabytes > 0;

        public Task AllocateAsync(int megabytes, int durationSeconds)
        {
            if (megabytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(megabytes));
            }

            lock (_lock)
            {
                _releaseCts?.Cancel();
                _releaseCts?.Dispose();
                _buffers.Clear();
                AllocatedMegabytes = 0;

                _buffers.Add(new byte[megabytes * 1024 * 1024]);
                AllocatedMegabytes = megabytes;

                _releaseCts = new CancellationTokenSource();
            }

            return ScheduleReleaseAsync(durationSeconds, _releaseCts.Token);
        }

        public void Release()
        {
            lock (_lock)
            {
                _releaseCts?.Cancel();
                _releaseCts?.Dispose();
                _releaseCts = null;
                _buffers.Clear();
                AllocatedMegabytes = 0;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private async Task ScheduleReleaseAsync(int durationSeconds, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds), cancellationToken);
                Release();
            }
            catch (TaskCanceledException)
            {
                // Nova alocação ou release manual cancelou o timer.
            }
        }
    }
}
