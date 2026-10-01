using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static QuickParrot.Input.NativeMethods;

namespace QuickParrot.Input;

/// <summary>
/// A message-loop thread for low-level hooks that also runs posted commands in order. While it isn't running,
/// commands run on the caller's thread instead, one at a time.
/// </summary>
internal sealed class HookThread(string name)
{
    private const uint WM_RUN_COMMANDS = WM_APP + 1;

    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly Lock _lock = new();

    // Guarded by _lock; the thread sets _threadId before Start returns.
    private Thread? _thread;
    private uint _threadId;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return _thread is not null;
        }
    }

    /// <summary>
    /// Runs <paramref name="install"/> on a new thread and rethrows its failure here. After a failed install or once
    /// stopped, the thread runs <paramref name="uninstall"/>, any commands still queued, then <paramref name="release"/>.
    /// </summary>
    public void Start(Action install, Action uninstall, Action release)
    {
        lock (_lock)
        {
            if (_thread is not null)
                return;

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => Run(install, uninstall, release, started))
            {
                IsBackground = true,
                Name = name,
                Priority = ThreadPriority.Highest, // every keystroke system-wide waits on this thread
            };
            thread.Start();
            try
            {
                started.Task.GetAwaiter().GetResult();
            }
            catch
            {
                thread.Join();
                throw;
            }

            _thread = thread;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_thread is null)
                return;

            if (!PostThreadMessageW(_threadId, WM_QUIT, 0, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError()); // Join would otherwise wait forever
            _thread.Join();
            _thread = null;
        }
    }

    /// <summary>Queues a command for the thread; if it isn't running, runs it here unless told to drop it.</summary>
    public void Post(Action command, bool dropIfStopped = false)
    {
        lock (_lock)
        {
            if (_thread is null)
            {
                if (!dropIfStopped)
                    command();

                return;
            }

            _commands.Enqueue(command);
            PostThreadMessageW(_threadId, WM_RUN_COMMANDS, 0, 0);
        }
    }

    private void Run(Action install, Action uninstall, Action release, TaskCompletionSource started)
    {
        _threadId = GetCurrentThreadId();
        PeekMessageW(out _, 0, 0, 0, PM_NOREMOVE); // creates the message queue, so posts after Start can't be lost
        try
        {
            install();
        }
        catch (Exception e)
        {
            Finish(uninstall, release);
            started.SetException(e);
            return;
        }

        started.SetResult();
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            if (msg.hwnd == 0 && msg.message == WM_RUN_COMMANDS)
                RunCommands();
            else
                DispatchMessageW(in msg);
        }

        Finish(uninstall, release);
    }

    private void Finish(Action uninstall, Action release)
    {
        uninstall();
        RunCommands();
        release();
    }

    private void RunCommands()
    {
        while (_commands.TryDequeue(out var command))
            command();
    }
}
