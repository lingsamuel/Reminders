param([string]$OutputDirectory = '')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    [xml]$properties = Get-Content 'Directory.Build.props'
    $version = $properties.Project.PropertyGroup.Version
    $backendDirectory = Join-Path $repositoryRoot 'artifacts/backend'
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'artifacts/win-x64' }
    dotnet publish src/Reminders.Windows/Reminders.Windows.csproj -c Release -r win-x64 --self-contained true -o $backendDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Backend publish failed.' }
    Push-Location 'src/Reminders.Desktop'
    try {
        npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency restore failed.' }
        npm.cmd run tauri -- build --target x86_64-pc-windows-msvc
        if ($LASTEXITCODE -ne 0) { throw 'Tauri Windows build failed.' }
    } finally { Pop-Location }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Copy-Item 'src/Reminders.Desktop/src-tauri/target/x86_64-pc-windows-msvc/release/reminders-desktop.exe' (Join-Path $OutputDirectory 'Reminders.exe')
    $portableBackend = Join-Path $OutputDirectory 'backend'
    New-Item -ItemType Directory -Path $portableBackend -Force | Out-Null
    Copy-Item (Join-Path $backendDirectory '*') $portableBackend -Recurse -Force
    $archivePath = Join-Path $repositoryRoot "artifacts/Reminders-$version-win-x64.zip"
    Copy-Item README.md $OutputDirectory
    $documentationDirectory = Join-Path $OutputDirectory 'docs'
    New-Item -ItemType Directory -Path $documentationDirectory -Force | Out-Null
    Copy-Item docs/architecture.md,docs/verification.md $documentationDirectory
    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $archivePath -Force
    Write-Output "Portable ZIP: $archivePath"
    Write-Output 'Installer: src/Reminders.Desktop/src-tauri/target/x86_64-pc-windows-msvc/release/bundle/nsis'
} finally { Pop-Location }
