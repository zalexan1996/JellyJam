using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JellyJam.Core.Configuration;

public sealed class JsonConfigurationStore(string directory) : IConfigurationStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string path = Path.Combine(directory, "settings.json");

    public async Task<PlayerConfiguration?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var configuration = await JsonSerializer.DeserializeAsync<PlayerConfiguration>(stream, Options, cancellationToken);
            if (!IsValid(configuration)) throw new JsonException();
            return configuration with { ServerUrl = ServerAddress.Normalize(configuration.ServerUrl) };
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JellyJamException("Settings are invalid or unreadable. Run 'configure' to reconnect.");
        }
    }

    public async Task SaveAsync(PlayerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(configuration, Options), cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool IsValid([NotNullWhen(true)] PlayerConfiguration? configuration)
        => configuration is not null
            && configuration.Version == PlayerConfiguration.CurrentVersion
            && configuration.UserId != Guid.Empty
            && !string.IsNullOrWhiteSpace(configuration.DeviceId)
            && Enum.IsDefined(configuration.Random)
            && Enum.IsDefined(configuration.Repeat);
}
