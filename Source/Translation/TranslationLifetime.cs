using System.Threading;

namespace RuntimeTranslator
{
    public static class TranslationLifetime
    {
        private static readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private static int stopping;
        public static bool IsStopping { get { return Volatile.Read(ref stopping) != 0; } }
        public static CancellationToken Token { get { return shutdown.Token; } }

        public static void Stop()
        {
            if (Interlocked.Exchange(ref stopping, 1) == 0) shutdown.Cancel();
        }
    }
}