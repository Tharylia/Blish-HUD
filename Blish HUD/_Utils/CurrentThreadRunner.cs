using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Blish_HUD {

    public class CurrentThreadRunner {

        public static void RunSync(Func<Task> work, bool allowLateContinuations) {
            var previous = SynchronizationContext.Current;
            var context  = new PumpContext(allowLateContinuations);
            SynchronizationContext.SetSynchronizationContext(context);

            try {
                // First segment runs right here on the main thread,
                // and each await captures our context.
                var task = work();

                task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);

                // Main thread processes continuations until the task is done.
                context.RunOnCurrentThread();

                task.GetAwaiter().GetResult(); // propagate exceptions
            } finally {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }
        
        public static TResult RunSync<TResult>(Func<Task<TResult>> work, bool allowLateContinuations) {
            var previous = SynchronizationContext.Current;
            var context  = new PumpContext(allowLateContinuations);
            SynchronizationContext.SetSynchronizationContext(context);

            try {
                // First segment runs right here on the main thread,
                // and each await captures our context.
                var task = work();

                task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);

                // Main thread processes continuations until the task is done.
                context.RunOnCurrentThread();

                return task.GetAwaiter().GetResult(); // propagate exceptions
            } finally {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        private sealed class PumpContext : SynchronizationContext {

            private readonly bool                                                      _allowLateContinuations;
            private readonly BlockingCollection<(SendOrPostCallback cb, object state)> _queue = new BlockingCollection<(SendOrPostCallback cb, object state)>();

            public PumpContext(bool allowLateContinuations) {
                _allowLateContinuations = allowLateContinuations;
            }

            public override void Post(SendOrPostCallback d, object state) {
                try {
                    _queue.Add((d, state));
                } catch (InvalidOperationException) {
                    if (!_allowLateContinuations) {
                        throw;
                    }

                    // Pump already shut down: run the late continuation on the thread pool
                    ThreadPool.QueueUserWorkItem(_ => d(state));
                }
            }

            public void Complete() => _queue.CompleteAdding();

            public void RunOnCurrentThread() {
                foreach (var (cb, state) in _queue.GetConsumingEnumerable())
                    cb(state);
            }
        }

    }

}