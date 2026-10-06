using System.IO;
using System.Text.Json;
using HiMate.Agent.Models;

namespace HiMate.Agent.Services;

public sealed class SettingsService
{
    public string AppDataDir { get; }
    public string SettingsPath => Path.Combine(AppDataDir, "settings.json");
    public string SecretPath => Path.Combine(AppDataDir, "secret.dat");
    public string DatabasePath => Path.Combine(AppDataDir, "credit.db");

    public SettingsService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AppDataDir = Path.Combine(localAppData, "HiMate", "Credit");
        Directory.CreateDirectory(AppDataDir);

        MigrateLegacyAgentData(localAppData);
    }

    private void MigrateLegacyAgentData(string localAppData)
    {
        var legacyDir = Path.Combine(localAppData, "HiMate", "Agent");
        if (!Directory.Exists(legacyDir)) return;

        CopyIfMissing(Path.Combine(legacyDir, "settings.json"), SettingsPath);
        CopyIfMissing(Path.Combine(legacyDir, "secret.dat"), SecretPath);
        CopyIfMissing(Path.Combine(legacyDir, "agent.db"), DatabasePath);
    }

    private static void CopyIfMissing(string source, string destination)
    {
        try
        {
            if (File.Exists(source) && !File.Exists(destination))
            {
                File.Copy(source, destination, overwrite: false);
            }
        }
        catch
        {
            // Migration is best-effort. The application can still start with fresh local state.
        }
    }

    public async Task<AgentSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AgentSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath);
            return JsonSerializer.Deserialize<AgentSettings>(json) ?? new AgentSettings();
        }
        catch
        {
            return new AgentSettings();
        }
    }

    public async Task SaveAsync(AgentSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(SettingsPath, json);
    }
}
