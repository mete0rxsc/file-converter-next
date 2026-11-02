using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FileConverter.Core;

public sealed record ReleaseInfo(string Tag, string Notes, Uri AssetUrl, string AssetName, string Sha256, long Size);

public sealed partial class UpdateService : IDisposable
{
    public static Version CurrentVersion { get; } = new(3, 0, 3);
    private readonly HttpClient client;
    public UpdateService(HttpMessageHandler? handler = null)
    {
        client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromMinutes(15);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FileConverter", "3.0"));
    }
    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryPattern();
    public async Task<ReleaseInfo?> CheckAsync(string repository, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(repository)) return null;
        if (!RepositoryPattern().IsMatch(repository)) throw new ArgumentException("Updates.Repository 应填写 owner/repo。");
        using var response = await client.GetAsync("https://api.github.com/repos/" + repository + "/releases/latest", token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("发布版本号为空。");
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) throw new InvalidDataException("发布 tag 应使用 v3.0.1 一类版本号。");
        if (parsed <= CurrentVersion) return null;
        var asset = root.GetProperty("assets").EnumerateArray().FirstOrDefault(a =>
            a.GetProperty("name").GetString() is { } name && name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) && name.Contains("x64", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("该版本未发布 x64 MSI。");
        var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
        return new(tag, root.GetProperty("body").GetString() ?? "",
            new Uri(asset.GetProperty("browser_download_url").GetString()!), Path.GetFileName(asset.GetProperty("name").GetString()!),
            digest.StartsWith("sha256:") ? digest[7..] : "", asset.GetProperty("size").GetInt64());
    }
    public async Task<string> DownloadAsync(ReleaseInfo release, string folder, IProgress<double>? progress, CancellationToken token)
    {
        if (release.AssetUrl.Scheme != "https" || release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("发布资产缺少有效 SHA-256 或 HTTPS 地址，请重新发布带 digest 的 GitHub 资产。");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(release.AssetName));
        try
        {
            using var response = await client.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            using var source = await response.Content.ReadAsStreamAsync(token);
            using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                { await destination.WriteAsync(buffer.AsMemory(0, read), token); total += read; if (release.Size > 0) progress?.Report(total / (double)release.Size); }
                await destination.FlushAsync(token);
            }
            using var downloaded = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, token));
            if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包 SHA-256 校验失败。");
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    public static void Install(string path)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe")) { UseShellExecute = false };
        info.ArgumentList.Add("/i"); info.ArgumentList.Add(Path.GetFullPath(path)); Process.Start(info)?.Dispose();
    }
    public void Dispose() => client.Dispose();
}
