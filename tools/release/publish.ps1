<#
.SYNOPSIS
  Builds a portable, self-contained flutter package for Windows.

.DESCRIPTION
  Produces a single-file, self-contained flutter.exe and a zip archive. The .NET runtime and native
  dependencies are bundled, so the target computer does not need the .NET SDK or Runtime installed.

  Examples:
    pwsh tools/release/publish.ps1
    pwsh tools/release/publish.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $repo 'FluidBar.sln'))) {
    throw "Cannot locate FluidBar.sln from $PSScriptRoot"
}

$out = Join-Path $repo "build/release/$Runtime"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host "Publishing flutter ($Runtime, self-contained single file)..."
$args = @(
    'publish', (Join-Path $repo 'src/FluidBar.App/FluidBar.App.csproj'),
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:IncludeAllContentForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $out,
    '-v', 'minimal',
    '--nologo'
)
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

foreach ($file in @('README.md', 'PROJECT-NOTES.md', 'CHANGELOG.md')) {
    $source = Join-Path $repo $file
    if (Test-Path $source) { Copy-Item $source (Join-Path $out $file) -Force }
}
$docs = Join-Path $repo 'docs/plugin'
if (Test-Path $docs) { Copy-Item $docs (Join-Path $out 'docs-plugin') -Recurse -Force }

$exe = Join-Path $out 'flutter.exe'
if (-not (Test-Path $exe)) { throw "Published exe missing: $exe" }
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
Write-Host "Published: $exe ($sizeMb MB)"
Write-Host "SHA256: $hash"

$manifest = [ordered]@{
    product = 'flutter'
    version = '4.1.0'
    runtime = $Runtime
    selfContained = $true
    singleFile = $true
    exe = 'flutter.exe'
    sizeMb = $sizeMb
    sha256 = $hash
    publishedAt = (Get-Date).ToString('o')
}
$manifestJson = $manifest | ConvertTo-Json
$manifestPath = Join-Path $out 'publish.json'
if ($PSVersionTable.PSVersion.Major -ge 6) {
    Set-Content -Path $manifestPath -Value $manifestJson -Encoding utf8NoBOM
} else {
    # Windows PowerShell 5.1 does not expose utf8NoBOM in Set-Content.
    [System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))
}

# Drop native package PDBs before zipping; they are not needed by users and bloat the download.
Get-ChildItem -Path $out -Filter *.pdb -File -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

if (-not $SkipZip) {
    $zip = Join-Path $repo "build/release/flutter-4.1.0-$Runtime-portable.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Zip: $zip"
}

