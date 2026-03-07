using System.Text.Json;
using Clawsharp.Config;

namespace Clawsharp.Auth;

/// <summary>
/// Persists OAuth tokens to ~/.clawsharp/auth/{provider}.json.
/// </summary>
public static class AuthStore
{
    private static string GetTokenPath(string provider)
    {
        var dir = ConfigLoader.ExpandHome("~/.clawsharp/auth");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{provider}.json");
    }

    public static async Task SaveAsync(string provider, OAuthToken token, CancellationToken ct = default)
    {
        var path = GetTokenPath(provider);
        var tmpPath = path + ".tmp";
        var json = JsonSerializer.Serialize(token, AuthJsonContext.Default.OAuthToken);
        await File.WriteAllTextAsync(tmpPath, json, ct);

        // Restrict file permissions on Unix (owner read/write only)
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tmpPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(tmpPath, path, overwrite: true);
    }

    public static async Task<OAuthToken?> LoadAsync(string provider, CancellationToken ct = default)
    {
        var path = GetTokenPath(provider);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize(json, AuthJsonContext.Default.OAuthToken);
    }

    public static bool Exists(string provider)
    {
        return File.Exists(GetTokenPath(provider));
    }
}