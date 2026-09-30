param(
    [string]$Version = '0.9.4',
    [string]$DllPath = "$PSScriptRoot/bin/Release/net48/BuildHotbar.dll",
    [string]$OutputRoot = "$PSScriptRoot/dist"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$source = $PSScriptRoot
if (!(Test-Path -LiteralPath $DllPath)) { throw 'Build the plugin or supply -DllPath.' }
$dllVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $DllPath).Path).FileVersion
if ([version]$dllVersion -ne [version]"$Version.0") { throw "DLL version $dllVersion does not match package $Version" }
$dllHash = (Get-FileHash -LiteralPath $DllPath).Hash
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$staging = Join-Path $OutputRoot ("staging-" + [guid]::NewGuid().ToString('N'))
function Write-Zip($directory, $destination) {
    if (Test-Path -LiteralPath $destination) { throw "Archive already exists: $destination" }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($directory, $destination, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}
foreach ($platform in 'thunderstore', 'hexium') {
    $package = Join-Path $staging $platform
    New-Item -ItemType Directory -Path "$package/plugins/BuildHotbar" -Force | Out-Null
    Copy-Item -LiteralPath $DllPath -Destination "$package/plugins/BuildHotbar/BuildHotbar.dll"
    foreach ($name in 'README.md', 'CHANGELOG.md', 'LICENSE.txt', 'icon.png') {
        Copy-Item -LiteralPath "$source/$name" -Destination "$package/$name"
    }
    $manifest = [ordered]@{
        name = 'Build_Hotbar'
        version_number = $Version
        website_url = 'https://github.com/KakimakiDev/ValheimBuildHotbar'
        description = 'Saved building shortcuts for keyboard and controller: paged radial menu, custom bindings, native control hints, adjustable sizes and built-in settings.'
        dependencies = @()
    }
    if ($platform -eq 'thunderstore') { $manifest.dependencies = @('denikson-BepInExPack_Valheim-5.4.2350') }
    [IO.File]::WriteAllText("$package/manifest.json", ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $archive = Join-Path $OutputRoot "$platform-Build_Hotbar-$Version.zip"
    Write-Zip $package $archive
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $names = @($zip.Entries.FullName)
        $expected = @('manifest.json','README.md','CHANGELOG.md','icon.png','LICENSE.txt','plugins/BuildHotbar/BuildHotbar.dll')
        if ($names -match '(?i)\.(zip|7z|rar|tar|gz|ps1|cs|csproj)$') { throw 'Nested archives, scripts and source files must not be packaged' }
        if (Compare-Object ($names | Sort-Object) ($expected | Sort-Object)) { throw 'Unexpected package contents' }
    } finally { $zip.Dispose() }
    if ((Get-FileHash "$package/plugins/BuildHotbar/BuildHotbar.dll").Hash -ne $dllHash) { throw 'DLL changed during packaging' }
    Get-Item -LiteralPath $archive | Select-Object FullName, Length
}
