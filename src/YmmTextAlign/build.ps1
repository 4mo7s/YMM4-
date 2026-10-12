param(
    [Parameter(Mandatory = $true)][string]$YMM4Dir
)

$ErrorActionPreference = 'Stop'
$sourceDir = $PSScriptRoot
$referenceDir = (Resolve-Path -LiteralPath $YMM4Dir).Path
foreach ($name in @('YukkuriMovieMaker.dll', 'YukkuriMovieMaker.Plugin.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $referenceDir $name))) {
        throw "Required DLL not found: $name"
    }
}
$ymmAssemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $referenceDir 'YukkuriMovieMaker.dll')).Version.ToString()
if ($ymmAssemblyVersion -ne '4.53.0.1') { throw "This project supports YMM4 4.53.0.1 only. Found: $ymmAssemblyVersion" }

dotnet build (Join-Path $sourceDir 'YmmTextAlign.csproj') -c Release -p:YMM4Dir="$referenceDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$dist = Join-Path $sourceDir 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$pluginDll = Join-Path $sourceDir 'bin\Release\net10.0-windows\YmmTextAlign.dll'
$pluginAssembly = [System.Reflection.Assembly]::LoadFile($pluginDll)
$linkedVersion = ($pluginAssembly.GetReferencedAssemblies() | Where-Object Name -EQ 'YukkuriMovieMaker' | Select-Object -First 1).Version.ToString()
if ($linkedVersion -ne $ymmAssemblyVersion) { throw "Wrong YMM4 reference: built $linkedVersion, requested $ymmAssemblyVersion" }
foreach ($reference in $pluginAssembly.GetReferencedAssemblies()) {
    if ($reference.Name -match '^(YukkuriMovieMaker|Vortice|SharpGen)') {
        $hostFile = Join-Path $referenceDir ($reference.Name + '.dll')
        $hostVersion = [System.Reflection.AssemblyName]::GetAssemblyName($hostFile).Version
        if ($reference.Version -ne $hostVersion) { throw "Reference version mismatch: $($reference.Name)" }
    }
}
$versionNote = Join-Path $dist 'BUILT_FOR_YMM4.txt'
"YmmTextAlign $($pluginAssembly.GetName().Version.ToString(3))`nBuilt against YMM4 $ymmAssemblyVersion`nnet10.0-windows / x64" | Set-Content -LiteralPath $versionNote -Encoding utf8
$package = Join-Path $dist 'YmmTextAlign.ymme'
$temporaryPackage = Join-Path $dist ([guid]::NewGuid().ToString() + '.ymme')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($temporaryPackage, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $pluginDll, 'YmmTextAlign/YmmTextAlign.dll') | Out-Null
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $sourceDir 'README.md'), 'YmmTextAlign/README.md') | Out-Null
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $versionNote, 'YmmTextAlign/BUILT_FOR_YMM4.txt') | Out-Null
} finally { $archive.Dispose() }
Move-Item -LiteralPath $temporaryPackage -Destination $package -Force
Write-Host $package
