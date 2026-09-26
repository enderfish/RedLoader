<#
.SYNOPSIS
    Rebuilds Libs/Alt.Json.dll from the official Newtonsoft.Json source.

.DESCRIPTION
    Alt.Json is Newtonsoft.Json with its namespaces renamed to Alt.Json, so RedLoader and mods can use it without
    clashing with the game's own Newtonsoft.Json. This script makes that binary reproducible from source:

      1. Clones JamesNK/Newtonsoft.Json at the pinned tag and refuses to continue unless it is the expected commit.
      2. In that working copy only, renames "Newtonsoft.Json" to "Alt.Json" in the library sources, and lets the
         project build for net10.0 using Newtonsoft's own net8.0 feature settings. The two build-only packages
         (code analyzers, SourceLink) and the SDK pin are dropped, so nothing but the .NET SDK is needed.
      3. Builds deterministically, with source hashes embedded in the PDB, and copies the result to Libs/Alt.Json.dll.

    Requires git and the .NET 10 SDK. Re-running it with the same SDK version produces the same bytes.
#>
param(
    [string]$WorkDir = (Join-Path $PSScriptRoot '..\..\obj\AltJson')
)

$ErrorActionPreference = 'Stop'

$Tag = '13.0.4'
$Commit = '4e13299d4b0ec96bd4df9954ef646bd2d1b5bf2a'
$RepoUrl = 'https://github.com/JamesNK/Newtonsoft.Json.git'
$Output = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\Libs\Alt.Json.dll'))

$src = Join-Path $WorkDir 'Newtonsoft.Json'
$out = Join-Path $WorkDir 'out'
foreach ($dir in $src, $out) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $WorkDir | Out-Null

# LF line endings on every OS, so the embedded source hashes (and therefore the DLL) match across machines.
git -c advice.detachedHead=false -c core.autocrlf=false -c core.eol=lf clone --quiet --depth 1 --branch $Tag $RepoUrl $src
if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
$head = (git -C $src rev-parse HEAD).Trim()
if ($head -ne $Commit) { throw "Tag $Tag points to $head, expected $Commit; refusing to build." }

$lib = Join-Path $src 'Src\Newtonsoft.Json'
$utf8 = [Text.UTF8Encoding]::new($false)
Get-ChildItem $lib -Recurse -Filter *.cs | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    if ($text.Contains('Newtonsoft.Json')) {
        [IO.File]::WriteAllText($_.FullName, $text.Replace('Newtonsoft.Json', 'Alt.Json'), $utf8)
    }
}

$proj = Join-Path $lib 'Newtonsoft.Json.csproj'
$p = [IO.File]::ReadAllText($proj)
$p = $p.Replace("'`$(TargetFramework)'=='net8.0'", "'`$(TargetFramework)'=='net10.0'")
$p = [regex]::Replace($p, '\s*<PackageReference Include="Microsoft\.(CodeAnalysis\.NetAnalyzers|SourceLink\.GitHub)"[^>]*/>', '')
[IO.File]::WriteAllText($proj, $p, $utf8)
Remove-Item (Join-Path $src 'Src\global.json') -ErrorAction SilentlyContinue

dotnet build $proj -c Release -o $out -nologo `
    -p:LibraryFrameworks=net10.0 `
    -p:AssemblyName=Alt.Json -p:RootNamespace=Alt.Json `
    -p:AssemblyVersion=13.0.0.0 -p:FileVersion=13.0.4 -p:Version=13.0.4 -p:SourceRevisionId=$Commit `
    -p:Company=Alt.Json -p:Product=Alt.Json "-p:AssemblyTitle=Alt.Json (renamed Newtonsoft.Json $Tag)" `
    -p:ContinuousIntegrationBuild=true -p:Deterministic=true -p:EnableSourceLink=false -p:EmbedUntrackedSources=false `
    -p:DebugType=embedded -p:GenerateDocumentationFile=false -p:EnablePackageValidation=false -p:SignAssembly=false `
    -p:TreatWarningsAsErrors=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

Copy-Item (Join-Path $out 'Alt.Json.dll') $Output -Force
Write-Host "Alt.Json.dll -> $Output"
Write-Host "SHA-256: $((Get-FileHash $Output -Algorithm SHA256).Hash.ToLowerInvariant())"
