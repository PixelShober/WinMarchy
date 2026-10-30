using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinMarchy.Core;

/// <param name="Assets">Download URL per asset file name.</param>
/// <param name="Sha256">SHA-256 per asset file name, where GitHub publishes it (asset "digest").</param>
public sealed record ReleaseInfo(string Tag, IReadOnlyDictionary<string, string> Assets, IReadOnlyDictionary<string, string> Sha256);

/// <summary>HTTP downloads and GitHub release lookups.</summary>
public static class Net
{
    public static string UserAgent => "WinMarchy/" + AppInfo.Version;

    /// <summary>
    /// HttpClient behind the Windows system proxy (including PAC/WPAD). A company proxy that wants a
    /// login gets the Windows credentials (Kerberos/NTLM), so nothing has to be typed or stored.
    /// </summary>
    public static HttpClient CreateClient(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            UseDefaultCredentials = false,
        };
        var http = new HttpClient(handler) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);   // GitHub requires a User-Agent
        return http;
    }

    /// <summary>
    /// Latest (non-pre) release of a GitHub repository. Uses the API first; if its rate limit is hit
    /// (many users behind one company IP), falls back to the release page, which shows the digests too.
    /// </summary>
    public static async Task<ReleaseInfo> GetLatestReleaseAsync(HttpClient http, string repo, CancellationToken ct)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest"))
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                Log.Warn(L.T("net.githubRateLimit"));
            }
            else
            {
                EnsureOk(response);
                return ParseApiRelease(await response.Content.ReadAsStringAsync(ct));
            }
        }

        // /releases/latest redirects to /releases/tag/<tag>; the asset list is loaded from expanded_assets.
        var web = "https://github.com/" + repo;
        using var latest = await http.GetAsync(web + "/releases/latest", HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureOk(latest);
        var path = latest.RequestMessage?.RequestUri?.AbsolutePath ?? "";
        var tag = path[(path.LastIndexOf('/') + 1)..];
        if (tag.Length == 0 || tag == "latest") throw new WinMarchyException("releaseTagUnknown", ("repo", repo));
        var html = await http.GetStringAsync(web + "/releases/expanded_assets/" + tag, ct);
        return ParseExpandedAssets(html, repo, tag);
    }

    internal static ReleaseInfo ParseApiRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "?";
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (a.GetProperty("name").GetString() is not { } name) continue;
            if (a.GetProperty("browser_download_url").GetString() is { } url) assets[name] = url;
            if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest &&
                digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha[name] = digest["sha256:".Length..].ToLowerInvariant();
        }
        return new ReleaseInfo(tag, assets, sha);
    }

    /// <summary>Asset links and the "sha256:…" digest shown after each of them on the release page.</summary>
    internal static ReleaseInfo ParseExpandedAssets(string html, string repo, string tag)
    {
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var links = Regex.Matches(html, "href=\"(/" + Regex.Escape(repo) + "/releases/download/[^\"]+)\"", RegexOptions.IgnoreCase);
        for (int i = 0; i < links.Count; i++)
        {
            var url = "https://github.com" + WebUtility.HtmlDecode(links[i].Groups[1].Value);
            var name = FileName(url);
            if (assets.ContainsKey(name)) continue;
            assets[name] = url;
            int from = links[i].Index + links[i].Length;
            int to = i + 1 < links.Count ? links[i + 1].Index : html.Length;
            var digest = Regex.Match(html[from..to], "sha256:([0-9a-fA-F]{64})");
            if (digest.Success) sha[name] = digest.Groups[1].Value.ToLowerInvariant();
        }
        return new ReleaseInfo(tag, assets, sha);
    }

    public static void EnsureOk(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var code = (int)response.StatusCode;
        var hint = code == 407 ? " – " + L.T("errors.proxyAuth") : "";
        throw new HttpRequestException(
            $"HTTP {code} {response.ReasonPhrase} – {response.RequestMessage?.RequestUri}{hint}",
            null, response.StatusCode);
    }

    public static string FileName(string url)
    {
        var path = url.Split('?')[0];
        return Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
    }

    /// <summary>Downloads a file with progress into "target.part" and renames it when complete.</summary>
    public static async Task<long> DownloadAsync(HttpClient http, string url, string target, Action<long, long?>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var part = target + ".part";
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        EnsureOk(response);

        long? total = response.Content.Headers.ContentLength;
        long done = 0;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[1 << 17];
            var sw = Stopwatch.StartNew();
            var last = TimeSpan.Zero;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (sw.Elapsed - last > TimeSpan.FromMilliseconds(250))
                {
                    last = sw.Elapsed;
                    progress?.Invoke(done, total);
                }
            }
        }
        progress?.Invoke(done, total);
        File.Move(part, target, overwrite: true);
        return done;
    }

    /// <summary>Downloads and checks the SHA-256. A mismatching file is deleted, never used.</summary>
    public static async Task DownloadVerifiedAsync(HttpClient http, string url, string target, string sha256,
                                                   Action<long, long?>? progress, CancellationToken ct)
    {
        await DownloadAsync(http, url, target, progress, ct);
        var actual = await Sha256Async(target, ct);
        if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new WinMarchyException("shaMismatch", ("file", Path.GetFileName(target)), ("expected", sha256), ("actual", actual));
        }
    }

    public static async Task<string> Sha256Async(string file, CancellationToken ct)
    {
        await using var fs = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Mb(long bytes) => $"{bytes / 1048576.0:0.0} MB";

    /// <summary>Error message including all inner exceptions, without repetitions.</summary>
    public static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (!string.IsNullOrWhiteSpace(e.Message) && !parts.Contains(e.Message)) parts.Add(e.Message);
        var sb = new StringBuilder(string.Join(" | ", parts));
        if (ex is TaskCanceledException && ex.InnerException is TimeoutException)
            sb.Append(" (").Append(L.T("errors.timeoutSuffix")).Append(')');
        return sb.ToString();
    }
}
