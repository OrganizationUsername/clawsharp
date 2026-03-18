using System.Net.Http.Headers;
using System.Text.Json;
using Spectre.Console;

namespace Clawsharp.Auth;

/// <summary>
/// Implements the GitHub OAuth device flow for Copilot authentication.
/// After obtaining a GitHub OAuth token, exchanges it for a short-lived Copilot API token.
/// </summary>
public sealed class GitHubDeviceFlow(IHttpClientFactory httpFactory)
{
    // Well-known public client_id for GitHub Copilot (used by many open-source projects)
    private const string CopilotClientId = "Iv1.b507a08c87ecfe98";

    private const string Scope = "read:user";

    /// <summary>
    /// Runs the full device flow login: obtains a GitHub token, then exchanges for a Copilot token.
    /// Returns an <see cref="OAuthToken"/> where AccessToken is the Copilot token and
    /// RefreshToken is the GitHub OAuth token (used to obtain new Copilot tokens).
    /// </summary>
    public async Task<OAuthToken?> LoginAsync(CancellationToken ct)
    {
        using var http = httpFactory.CreateClient("llm");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Step 1: Request device code
        var deviceCode = await RequestDeviceCodeAsync(http, ct);
        if (deviceCode is null)
        {
            AnsiConsole.MarkupLine("[red][[auth]][/] Failed to request device code from GitHub.");
            return null;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  Open this URL in your browser:  {Markup.Escape(deviceCode.VerificationUri)}");
        AnsiConsole.MarkupLine($"  Enter this code:                {Markup.Escape(deviceCode.UserCode)}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  Waiting for authorization...");

        // Step 2: Poll for GitHub access token
        var githubToken = await PollForAccessTokenAsync(http, deviceCode, ct);
        if (githubToken is null)
        {
            AnsiConsole.MarkupLine("[red][[auth]][/] Device flow authorization timed out or was denied.");
            return null;
        }

        AnsiConsole.MarkupLine("  GitHub authorization successful. Fetching Copilot token...");

        // Step 3: Exchange GitHub token for Copilot token
        var copilotToken = await ExchangeForCopilotTokenAsync(http, githubToken, ct);
        if (copilotToken is null)
        {
            AnsiConsole.MarkupLine("[red][[auth]][/] Failed to obtain Copilot token. Ensure your GitHub account has Copilot access.");
            return null;
        }

        return copilotToken;
    }

    /// <summary>
    /// Exchanges a stored GitHub OAuth token for a fresh Copilot API token.
    /// Called when the Copilot token has expired.
    /// </summary>
    public async Task<OAuthToken?> RefreshCopilotTokenAsync(string githubToken, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient("llm");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await ExchangeForCopilotTokenAsync(http, githubToken, ct);
    }

    private static async Task<GitHubDeviceCodeResponse?> RequestDeviceCodeAsync(HttpClient http, CancellationToken ct)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = CopilotClientId,
            ["scope"] = Scope
        });

        try
        {
            var resp = await http.PostAsync("https://github.com/login/device/code", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                AnsiConsole.MarkupLine($"[red][[auth]][/] Device code request failed ({resp.StatusCode}): {Markup.Escape(err)}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize(json, AuthJsonContext.Default.GitHubDeviceCodeResponse);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red][[auth]][/] Device code request error: {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    private static async Task<string?> PollForAccessTokenAsync(
        HttpClient http, GitHubDeviceCodeResponse deviceCode, CancellationToken ct)
    {
        var interval = Math.Max(deviceCode.Interval, 5);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresIn);

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);

            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = CopilotClientId,
                ["device_code"] = deviceCode.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });

            try
            {
                var resp = await http.PostAsync("https://github.com/login/oauth/access_token", body, ct);
                var json = await resp.Content.ReadAsStringAsync(ct);
                var tokenResp = JsonSerializer.Deserialize(json, AuthJsonContext.Default.GitHubAccessTokenResponse);

                if (tokenResp is null)
                {
                    continue;
                }

                if (tokenResp.AccessToken is not null)
                {
                    return tokenResp.AccessToken;
                }

                switch (tokenResp.Error)
                {
                    case "authorization_pending":
                        // User hasn't authorized yet, keep polling
                        continue;
                    case "slow_down":
                        // Back off by adding 5 seconds
                        interval += 5;
                        continue;
                    case "expired_token":
                        AnsiConsole.MarkupLine("[red][[auth]][/] Device code expired. Please try again.");
                        return null;
                    case "access_denied":
                        AnsiConsole.MarkupLine("[red][[auth]][/] Authorization was denied by the user.");
                        return null;
                    default:
                        AnsiConsole.MarkupLine(
                            $"[red][[auth]][/] Unexpected error: {Markup.Escape(tokenResp.Error ?? "")} - {Markup.Escape(tokenResp.ErrorDescription ?? "")}");
                        return null;
                }
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red][[auth]][/] Poll error: {Markup.Escape(ex.Message)}");
                // Continue polling on transient errors
            }
        }

        return null;
    }

    private static async Task<OAuthToken?> ExchangeForCopilotTokenAsync(
        HttpClient http, string githubToken, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/v2/token");
            req.Headers.Authorization = new AuthenticationHeaderValue("token", githubToken);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("clawsharp", "1.0"));

            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                AnsiConsole.MarkupLine($"[red][[auth]][/] Copilot token exchange failed ({resp.StatusCode}): {Markup.Escape(err)}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            var copilotResp = JsonSerializer.Deserialize(json, AuthJsonContext.Default.CopilotTokenResponse);
            if (copilotResp is null || string.IsNullOrEmpty(copilotResp.Token))
            {
                AnsiConsole.MarkupLine("[red][[auth]][/] Empty Copilot token response.");
                return null;
            }

            return new OAuthToken
            {
                AccessToken = copilotResp.Token,
                TokenType = "Bearer",
                RefreshToken = githubToken,
                ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(copilotResp.ExpiresAt),
                Scope = Scope
            };
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red][[auth]][/] Copilot token exchange error: {Markup.Escape(ex.Message)}");
            return null;
        }
    }
}