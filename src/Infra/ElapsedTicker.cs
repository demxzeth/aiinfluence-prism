using System;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Infra
{
    /// <summary>
    /// Accumulates elapsed time per scope across multiple requests.
    /// The total is periodically flushed to the log and shown in chat
    /// messages. The ticker is additive — it runs only while Prism
    /// is waiting for a reply.
    ///
    /// Thread-safe: all operations are lock-free for reads and
    /// use a single lock for writes (called from non-overlapping
    /// async continuations, actually, but safety is cheap).
    /// </summary>
    public static class ElapsedTicker
    {
        private static readonly object Gate = new object();
        private static long _totalTicks;
        private static long _requestCount;

        /// <summary>Accumulate a request's elapsed time. Called after every reply.</summary>
        public static void Add(double milliseconds)
        {
            if (milliseconds <= 0)
            {
                // A non-positive measurement is a glitch, not a request:
                // it must neither accumulate time nor inflate the counter.
                return;
            }

            lock (Gate)
            {
                _totalTicks += (long)(milliseconds * TimeSpan.TicksPerMillisecond);
                _requestCount++;
            }
        }

        /// <summary>How many requests were recorded (for periodic flushing decisions).</summary>
        public static int RequestCount
        {
            get
            {
                lock (Gate)
                {
                    return (int)_requestCount;
                }
            }
        }

        /// <summary>Total accumulated time (human-readable). Resets on read.</summary>
        public static string TakeTotal()
        {
            lock (Gate)
            {
                long ticks = _totalTicks;
                long count = _requestCount;
                _totalTicks = 0;
                _requestCount = 0;

                if (count == 0)
                {
                    return "no requests yet";
                }

                TimeSpan span = TimeSpan.FromTicks(ticks);
                return count + " request(s), "
                    + (span.Hours > 0 ? span.Hours + "h " : "")
                    + (span.Minutes > 0 ? span.Minutes + "m " : "")
                    + span.Seconds + "s";
            }
        }

        /// <summary>Log the accumulated time.</summary>
        public static void FlushToLog()
        {
            string report = TakeTotal();
            LogSink.Always("elapsed: " + report);
        }
    }
}