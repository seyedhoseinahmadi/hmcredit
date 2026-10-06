using System.IO.Ports;
using Microsoft.Win32;

namespace HiMate.Agent.Services;

public sealed class SerialDeviceService : IDisposable
{
    private readonly LogService _log;
    private SerialPort? _port;

    public event Action<string>? LineReceived;
    public bool IsConnected => _port?.IsOpen == true;
    public string ConnectedPort => _port?.PortName ?? "";

    public SerialDeviceService(LogService log)
    {
        _log = log;
    }

    public static string[] GetPorts()
    {
        var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var port in SerialPort.GetPortNames())
            {
                if (!string.IsNullOrWhiteSpace(port))
                {
                    ports.Add(port.Trim());
                }
            }
        }
        catch
        {
            // Fall back to the Windows registry below.
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is not null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    if (key.GetValue(valueName) is string port && !string.IsNullOrWhiteSpace(port))
                    {
                        ports.Add(port.Trim());
                    }
                }
            }
        }
        catch
        {
            // Some environments restrict registry access. SerialPort enumeration is still used.
        }

        return ports
            .OrderBy(PortSortKey)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int PortSortKey(string port)
    {
        if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(port.AsSpan(3), out var number))
        {
            return number;
        }

        return int.MaxValue;
    }

    public void Connect(string portName, int baudRate = 115200)
    {
        Disconnect();
        _port = new SerialPort(portName, baudRate)
        {
            NewLine = "\n",
            ReadTimeout = 1500,
            WriteTimeout = 1500,
            DtrEnable = false,
            RtsEnable = false
        };
        _port.DataReceived += OnDataReceived;
        _port.Open();
        _log.Info($"Serial connected: {portName} @ {baudRate}");
    }

    public void Disconnect()
    {
        if (_port is null) return;
        try
        {
            _port.DataReceived -= OnDataReceived;
            if (_port.IsOpen) _port.Close();
        }
        catch { }
        finally
        {
            _port.Dispose();
            _port = null;
        }
        _log.Info("Serial disconnected");
    }

    public void Send(string line)
    {
        if (_port?.IsOpen != true) throw new InvalidOperationException("Serial port is not connected.");
        _port.WriteLine(line);
        _log.Info($"TX  {line}");
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            while (_port?.IsOpen == true && _port.BytesToRead > 0)
            {
                var line = _port.ReadLine().Trim();
                if (line.Length == 0) continue;
                _log.Info($"RX  {line}");
                LineReceived?.Invoke(line);
            }
        }
        catch (TimeoutException)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"Serial read failed: {ex.Message}");
        }
    }

    public void Dispose() => Disconnect();
}
