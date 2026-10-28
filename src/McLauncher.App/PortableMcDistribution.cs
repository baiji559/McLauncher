using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace McLauncher.App;

/// <summary>Materializes the pinned backend from the application, including in single-file builds.</summary>
public static class PortableMcDistribution
{
    public const string ExecutableSha256 = "1AF06CEDEAA17432047E76C04B73B9AF4E3161C90A8B91324AEB3AD288604F41";

    public static string GetExecutablePath(string? cacheDirectory = null)
    {
        var directory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "McLauncher", "backend", ExecutableSha256);
        Directory.CreateDirectory(directory);
        using var source = typeof(PortableMcDistribution).Assembly.GetManifestResourceStream("McLauncher.PortableMc.zip")
            ?? throw new InvalidDataException("启动器缺少内嵌 PortableMC 后端。");
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);

        // Keep the upstream license and version information alongside the extracted executable.
        Extract(archive, directory, "LICENSE");
        Extract(archive, directory, "README");
        var executable = Path.Combine(directory, "portablemc.exe");
        if (!HasExpectedHash(executable)) Extract(archive, directory, "portablemc.exe");
        if (!HasExpectedHash(executable))
            throw new InvalidDataException("PortableMC 后端校验失败，请重新下载启动器。");
        return executable;
    }

    private static bool HasExpectedHash(string path)
    {
        if (!File.Exists(path)) return false;
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)) == ExecutableSha256;
    }

    private static void Extract(ZipArchive archive, string directory, string name)
    {
        var destination = Path.Combine(directory, name);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var source = archive.GetEntry(name)!.Open())
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                source.CopyTo(output);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
