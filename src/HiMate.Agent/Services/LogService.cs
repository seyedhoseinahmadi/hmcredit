using System.Collections.ObjectModel;

namespace HiMate.Agent.Services;

public sealed class LogService
{
    private readonly object _gate = new();
    public ObservableCollection<string> Items { get; } = [];
    public event Action<string>? LineAdded;

    public void Info(string message) => Add("INFO", message);
    public void Warn(string message) => Add("WARN", message);
    public void Error(string message) => Add("ERROR", message);

    private void Add(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {level}  {message}";
        lock (_gate)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Items.Add(line);
                while (Items.Count > 1000)
                {
                    Items.RemoveAt(0);
                }
            });
        }

        LineAdded?.Invoke(line);
    }
}
