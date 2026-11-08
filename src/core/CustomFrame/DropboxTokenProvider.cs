using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gradinware.CustomFrame;

// One cache and refresh gate per server process; credentials never leave this service.
public sealed class DropboxTokenProvider
{
    private readonly IHttpClientFactory clients;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private string appKey, appSecret, refreshToken, accessToken;
    private DateTimeOffset refreshAt, retryAt;

    public DropboxTokenProvider(IHttpClientFactory clients, TimeProvider clock)
    { this.clients = clients; this.clock = clock; }

    public async Task<string> GetAsync(Source source, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (source.AppKey != appKey || source.AppSecret != appSecret || source.RefreshToken != refreshToken)
            {
                appKey = source.AppKey;
                appSecret = source.AppSecret;
                refreshToken = source.RefreshToken;
                accessToken = null;
                refreshAt = retryAt = default;
            }
            var now = clock.GetUtcNow();
            if (accessToken != null && now < refreshAt) return accessToken;
            if (now < retryAt) throw new InvalidDataException();
            try
            {
                using var client = clients.CreateClient("CustomFrameDropbox");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var body = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken,
                    ["client_id"] = appKey, ["client_secret"] = appSecret
                });
                using var response = await client.PostAsync("https://api.dropboxapi.com/oauth2/token", body, timeout.Token);
                if (!response.IsSuccessStatusCode) throw new InvalidDataException();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                var root = json.RootElement;
                if (!root.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(token.GetString())
                    || !root.TryGetProperty("expires_in", out var expiry) || !expiry.TryGetInt32(out var seconds) || seconds <= 0)
                    throw new InvalidDataException();
                // Base expiry on request start so network time cannot extend the token lifetime.
                refreshAt = now.AddSeconds(seconds - Math.Min(300, seconds / 10.0));
                if (refreshAt <= clock.GetUtcNow()) throw new InvalidDataException();
                accessToken = token.GetString();
                return accessToken;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException
                || ex is JsonException || ex is InvalidDataException || ex is InvalidOperationException)
            {
                accessToken = null;
                retryAt = clock.GetUtcNow().AddSeconds(60);
                // Never expose upstream response bodies or credential-bearing exception details.
                throw new InvalidDataException();
            }
        }
        finally { gate.Release(); }
    }
}
