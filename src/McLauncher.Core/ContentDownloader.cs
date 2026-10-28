using System.Net.Http;
using System.Security.Cryptography;

namespace McLauncher.Core;

public sealed class ContentDownloader(HttpClient httpClient)
{
    public static string GetDestinationDirectory(string gameDirectory, ContentKind kind) =>
        Path.Combine(Path.GetFullPath(gameDirectory), kind switch
        {
            ContentKind.Mod => "mods", ContentKind.Modpack => "modpacks",
            ContentKind.Shader => "shaderpacks", _ => "resourcepacks"
        });

    public async Task<string> DownloadAsync(
        ContentFile file, string destinationDirectory, IProgress<ContentDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("作者未提供可用的直接下载地址，请使用“打开文件网页”手动下载。");
        ValidateFileName(file.FileName);
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(Path.GetFullPath(destinationDirectory), file.FileName);
        if (File.Exists(destination))
            throw new IOException($"文件已存在，未覆盖：{destination}");
        var temporary = Path.Combine(destinationDirectory, ".mclauncher-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("McLauncher/1.0 (https://github.com/baiji559/McLauncher)");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } finalUrl && finalUrl.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("下载被重定向到非 HTTPS 地址，已停止下载。");
            var total = response.Content.Headers.ContentLength ?? (file.Size > 0 ? file.Size : (long?)null);
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            long received = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int count;
                long lastReport = 0;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    sha512.AppendData(buffer, 0, count);
                    sha1.AppendData(buffer, 0, count);
                    received += count;
                    if (Environment.TickCount64 - lastReport >= 100)
                    {
                        progress?.Report(new(received, total));
                        lastReport = Environment.TickCount64;
                    }
                }
                await output.FlushAsync(cancellationToken);
            }
            if ((file.Size > 0 && received != file.Size) || (total is { } expected && received != expected))
                throw new InvalidDataException("下载文件长度与平台记录不符，请重新下载。");
            VerifyHash(file.Sha512, sha512.GetHashAndReset());
            VerifyHash(file.Sha1, sha1.GetHashAndReset());
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false);
            progress?.Report(new(received, received));
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void VerifyHash(string? expected, byte[] actual)
    {
        if (!string.IsNullOrWhiteSpace(expected) && !Convert.ToHexString(actual).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("文件哈希校验失败，请重新下载。");
    }

    private static void ValidateFileName(string name)
    {
        var stem = name.Split('.')[0];
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.EndsWith('.') || name.EndsWith(' ')
            || Path.GetFileName(name) != name || stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] is >= '0' and <= '9'))
            throw new InvalidDataException("平台返回了不安全的文件名。");
    }
}
