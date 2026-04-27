$ErrorActionPreference = "Stop"

$author = "Thanks"
$modName = "ShootZombies"
$projectDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$thunderstoreDir = $PSScriptRoot
$manifestPath = Join-Path $thunderstoreDir "manifest.json"
$manifest = Get-Content $manifestPath | ConvertFrom-Json
$version = $manifest.version_number

$packageName = "$author-$modName-$version"
$packageDir = Join-Path $thunderstoreDir $packageName
$pluginsDir = Join-Path $packageDir "plugins"
$zipPath = Join-Path $thunderstoreDir "$packageName.zip"
$hashPath = Join-Path $thunderstoreDir "$author.$modName-hashes.txt"
$projectFile = Join-Path $projectDir "com.github.PeakTest.ShootZombies.csproj"
$dllPath = Join-Path $projectDir "bin\Release\Thanks.ShootZombies.dll"
$akBundlePath = Join-Path $projectDir "bin\Release\Weapons_shootzombies.bundle"
$akSoundsDir = Join-Path $projectDir "AK_Sounds"

Write-Host "Packaging Thunderstore build: $packageName"

$generatedDirectoryPatterns = @(
    "extracted",
    "PeakTest-ShootZombies-*",
    "Thanks-ShootZombies-*"
)

$generatedFilePatterns = @(
    "PeakTest-ShootZombies-*.zip",
    "Thanks-ShootZombies-*.zip",
    "*-hashes.txt"
)

foreach ($pattern in $generatedDirectoryPatterns) {
    Get-ChildItem $thunderstoreDir -Directory -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

foreach ($pattern in $generatedFilePatterns) {
    Get-ChildItem $thunderstoreDir -File -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
    }
}

if (!(Test-Path $dllPath)) {
    throw "Missing Release DLL. Build the project first: $dllPath"
}

if (Test-Path $packageDir) {
    Remove-Item $packageDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null

Copy-Item (Join-Path $thunderstoreDir "manifest.json") $packageDir
Copy-Item (Join-Path $thunderstoreDir "README.md") $packageDir
Copy-Item (Join-Path $thunderstoreDir "CHANGELOG.md") $packageDir

$iconPath = Join-Path $thunderstoreDir "icon.png"
if (Test-Path $iconPath) {
    Copy-Item $iconPath $packageDir
}

if (!(Test-Path $dllPath)) {
    throw "Missing built DLL: $dllPath"
}
Copy-Item $dllPath $pluginsDir

if (!(Test-Path $akBundlePath)) {
    throw "Missing AK bundle: $akBundlePath"
}
Copy-Item $akBundlePath $pluginsDir

if (Test-Path $akSoundsDir) {
    Copy-Item $akSoundsDir (Join-Path $pluginsDir "AK_Sounds") -Recurse -Force
}

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath

$hashes = @(
    "manifest.json: $((Get-FileHash (Join-Path $thunderstoreDir 'manifest.json') -Algorithm MD5).Hash)"
    "README.md: $((Get-FileHash (Join-Path $thunderstoreDir 'README.md') -Algorithm MD5).Hash)"
    "CHANGELOG.md: $((Get-FileHash (Join-Path $thunderstoreDir 'CHANGELOG.md') -Algorithm MD5).Hash)"
    "Thanks.ShootZombies.dll: $((Get-FileHash $dllPath -Algorithm MD5).Hash)"
    "Weapons_shootzombies.bundle: $((Get-FileHash $akBundlePath -Algorithm MD5).Hash)"
)
$hashes | Set-Content $hashPath

if (Test-Path $packageDir) {
    Remove-Item $packageDir -Recurse -Force
}

Write-Host "Package directory: $packageDir"
Write-Host "Package archive: $zipPath"
