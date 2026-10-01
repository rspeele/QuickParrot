using System.Diagnostics;

namespace QuickParrot.Audio;

internal static class Continuations
{
    /// <summary>
    /// Runs <paramref name="handler"/> with the task's result on the thread pool once it completes; a throwing
    /// handler is logged rather than left in an unobserved task.
    /// </summary>
    public static void OnCompleted<T>(this Task<T> task, Action<T> handler) =>
        _ = task.ContinueWith(
            completed =>
            {
                try
                {
                    handler(completed.Result);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"QuickParrot: an audio completion handler failed: {e}");
                }
            },
            TaskScheduler.Default);
}
