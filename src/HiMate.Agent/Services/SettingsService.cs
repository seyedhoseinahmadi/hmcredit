using System.Text.Json;
using HiMate.Agent.Models;

namespace HiMate.Agent.Services;

public sealed class SettingsService
{
    public string AppDataDir { get; }
    public string SettingsPath => Path.Combine(AppDataDir, "settings.json");
    public string SecretPath => Path.Combine(AppDataDir, "secret.dat");
    public string DatabasePath => Path.Combine(AppDataDir, "agent.db");

    public SettingsService()
    {
        AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HiMate", "Agent");
        Directory.CreateDirectory(AppDataDir);
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
