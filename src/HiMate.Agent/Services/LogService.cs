using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace HiMate.Agent.Services;

public sealed class LogService
{
    // Never block SerialPort.DataReceived on a synchronous UI Dispatcher call.
    private readonly ConcurrentQueue<string> _pending = new();
    private int _flushScheduled;
    private const int MaxPending = 2000;
    private const int MaxDisplayed = 500;
    public ObservableCollection<string> Items { get; } = [];
    public event Action<string>? LineAdded;

    public void Info(string message) => Add("INFO", message);
    public void Warn(string message) => Add("WARN", message);
    public void Error(string message) => Add("ERROR", message);

    private void Add(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {level}  {message}";
        _pending.Enqueue(line);
        while (_pending.Count > MaxPending && _pending.TryDequeue(out _)) { }
        ScheduleFlush();
        LineAdded?.Invoke(line);
    }

    private void ScheduleFlush()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0) return;
        try
        {
            dispatcher.BeginInvoke(new Action(Flush), DispatcherPriority.Background);
        }
        catch (TaskCanceledException) { Interlocked.Exchange(ref _flushScheduled, 0); }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _flushScheduled, 0); }
    }

    private void Flush()
    {
        try
        {
            var processed = 0;
            while (processed++ < 80 && _pending.TryDequeue(out var line))
            {
                Items.Add(line);
                if (Items.Count > MaxDisplayed) Items.RemoveAt(0);
            }
        }
        finally { Interlocked.Exchange(ref _flushScheduled, 0); }
        if (!_pending.IsEmpty) ScheduleFlush();
    }
}
