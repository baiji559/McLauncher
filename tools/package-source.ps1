param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'artifacts/source/LATTICE-latest-source.zip'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($OutputPath)) -Force | Out-Null

$files = @(
    foreach ($folder in @('src', 'tests', 'tools', 'docs')) {
        Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File -Force |
            Where-Object {
                $_.FullName -notmatch '[\\/](bin|obj|\.vs|\.git)[\\/]' -and
                $_.Name -notmatch '\.(user|suo)$' -and
                $_.Name -notin @('portablemc_msa.json', 'lattice.portable.json', 'settings.json')
            }
    }
    foreach ($name in @('.gitignore', '.gitattributes', 'Directory.Build.props', 'McLauncher.slnx')) {
        Get-Item -LiteralPath (Join-Path $root $name)
    }
    Get-ChildItem -LiteralPath $root -File -Filter '*.md'
) | Sort-Object FullName -Unique

$manifest = [Collections.Generic.List[string]]::new()
$stream = [IO.File]::Open($OutputPath, [IO.FileMode]::Create)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
try {
    foreach ($file in $files) {
        $prefix = $root + [IO.Path]::DirectorySeparatorChar
        if (-not $file.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Source file is outside the project: $($file.FullName)"
        }
        $relative = $file.FullName.Substring($prefix.Length).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName,
            'LATTICE-source/' + $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $manifest.Add((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative)
    }
    $notes = @'
LATTICE complete current source

Includes the main solution, application/core source, tests, WPF assets,
publish profiles, documentation, and the PortableMC backend with its license.
Generated files, local settings, old archives, font backups and independent
historical examples are excluded.

Requirements: Windows and .NET 10 SDK.
Run these commands from the directory containing this file:

  dotnet build McLauncher.slnx -c Release
  dotnet run --project src\McLauncher.App\McLauncher.App.csproj -c Release

Standalone publication:
  dotnet publish src\McLauncher.App\McLauncher.App.csproj -p:PublishProfile=Standalone

Tests:
  dotnet run --project tests\McLauncher.Core.Tests\McLauncher.Core.Tests.csproj -c Release
  dotnet run --project tests\McLauncher.App.Tests\McLauncher.App.Tests.csproj -c Release

Package again:
  powershell -File tools\package-source.ps1

SOURCE_MANIFEST.sha256 contains hashes for all packaged project files.
Archive entries use UTF-8. This note has an ASCII filename for compatibility.
'@
    foreach ($extra in @(
        @{ Name = 'SOURCE_NOTES.txt'; Text = $notes },
        @{ Name = 'SOURCE_MANIFEST.sha256'; Text = $manifest -join "`n" }
    )) {
        $entry = $zip.CreateEntry('LATTICE-source/' + $extra.Name)
        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($extra.Text) } finally { $writer.Dispose() }
    }
}
finally { $zip.Dispose(); $stream.Dispose() }

Get-Item -LiteralPath $OutputPath | Select-Object FullName, Length
