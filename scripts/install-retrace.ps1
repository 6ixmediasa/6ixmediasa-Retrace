$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$version = "0.2.0"
$repo = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $env:LOCALAPPDATA "RetraceBuild"
$dotnetRoot = Join-Path $buildRoot "dotnet"
$publishDir = Join-Path $buildRoot ("publish-" + $version + "-" + [Guid]::NewGuid().ToString("N"))
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\Retrace"
$installDir = Join-Path $installRoot ($version + "-" + (Get-Date -Format "yyyyMMddHHmmss"))
$logDir = Join-Path $env:LOCALAPPDATA "Retrace"
$installLog = Join-Path $logDir "install.log"

New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
New-Item -ItemType Directory -Force -Path $dotnetRoot | Out-Null
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Stop-OlderRetraceBestEffort {
    Write-Host "      Requesting older Retrace processes to close..." -ForegroundColor DarkGray
    $before = @(Get-Process -Name "Retrace" -ErrorAction SilentlyContinue)
    if ($before.Count -eq 0) {
        Write-Host "      No older Retrace process is running." -ForegroundColor DarkGray
        return
    }

    # Do not use taskkill /T here. Retrace can launch Explorer or other child
    # processes that Windows may legitimately refuse to terminate. Only the
    # Retrace process itself matters, and installation is side-by-side anyway.
    foreach ($p in $before) {
        try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
    Start-Sleep -Milliseconds 900

    $remaining = @(Get-Process -Name "Retrace" -ErrorAction SilentlyContinue)
    if ($remaining.Count -gt 0) {
        Write-Host "      An older Retrace process is still running. Continuing with a side-by-side install." -ForegroundColor Yellow
        Write-Host "      The old locked files will be cleaned up after Windows releases them." -ForegroundColor DarkGray
    } else {
        Write-Host "      Older Retrace process closed." -ForegroundColor DarkGray
    }
}

function Remove-OldVersionFoldersBestEffort {
    param([string]$KeepDirectory)

    # Never make installation depend on deleting an old build. Locked folders
    # are harmless because shortcuts and startup already point to the new build.
    try {
        Get-ChildItem -Path $installRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.FullName -ne $KeepDirectory) {
                try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop } catch { }
            }
        }
    } catch { }
}

try {
    Start-Transcript -Path $installLog -Append | Out-Null

    if (-not [Environment]::Is64BitOperatingSystem) {
        throw "Retrace V0.2.0 currently requires 64-bit Windows."
    }

    Write-Host "[1/6] Preparing Retrace build environment..." -ForegroundColor Cyan
    Stop-OlderRetraceBestEffort

    $dotnet = Join-Path $dotnetRoot "dotnet.exe"
    if (-not (Test-Path $dotnet)) {
        Write-Host "[2/6] Downloading the private .NET 8 build toolchain (one-time)..." -ForegroundColor Cyan
        $installer = Join-Path $buildRoot "dotnet-install.ps1"
        Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer -UseBasicParsing
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 8.0 -Architecture x64 -InstallDir $dotnetRoot -NoPath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dotnet)) {
            throw ".NET SDK installation failed."
        }
    } else {
        Write-Host "[2/6] Build toolchain already available." -ForegroundColor DarkGray
    }

    $project = Join-Path $repo "src\Retrace.App\Retrace.App.csproj"
    if (-not (Test-Path $project)) {
        throw "Retrace project file was not found. Re-extract the ZIP and run INSTALL_RETRACE.cmd from inside the extracted folder."
    }

    Write-Host "[3/6] Restoring Retrace dependencies..." -ForegroundColor Cyan
    & $dotnet restore $project --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    Write-Host "[4/6] Compiling the Windows x64 test build..." -ForegroundColor Cyan
    & $dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $publishDir -p:PublishReadyToRun=false
    if ($LASTEXITCODE -ne 0) { throw "Retrace compilation failed." }

    $builtExe = Join-Path $publishDir "Retrace.exe"
    if (-not (Test-Path $builtExe)) { throw "Retrace.exe was not produced." }

    Write-Host "[5/6] Installing Retrace for this Windows user..." -ForegroundColor Cyan

    # Every install receives a unique directory. We never overwrite a DLL that
    # may still belong to a running older build.
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
    Copy-Item (Join-Path $publishDir "*") $installDir -Recurse -Force
    Copy-Item (Join-Path $repo "scripts\uninstall-retrace.ps1") (Join-Path $installDir "uninstall-retrace.ps1") -Force

    $exe = Join-Path $installDir "Retrace.exe"
    if (-not (Test-Path $exe)) { throw "Installed Retrace.exe was not found after copying files." }

    $desktop = [Environment]::GetFolderPath("Desktop")
    $startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
    $ws = New-Object -ComObject WScript.Shell
    foreach ($shortcutPath in @((Join-Path $desktop "Retrace.lnk"), (Join-Path $startMenu "Retrace.lnk"))) {
        $shortcut = $ws.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $exe
        $shortcut.WorkingDirectory = $installDir
        $shortcut.Description = "Retrace - Your computer remembers"
        $shortcut.IconLocation = "$exe,0"
        $shortcut.Save()
    }

    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name "Retrace" -Value ('"' + $exe + '" --background')

    # Save the exact active executable path for diagnostics and future updates.
    Set-Content -Path (Join-Path $logDir "active-install.txt") -Value $exe -Encoding ASCII

    Remove-OldVersionFoldersBestEffort -KeepDirectory $installDir

    Write-Host "[6/6] Launching Retrace..." -ForegroundColor Cyan
    $process = Start-Process -FilePath $exe -WorkingDirectory $installDir -PassThru
    Start-Sleep -Seconds 3
    $process.Refresh()

    if ($process.HasExited) {
        $startupLog = Join-Path $logDir "retrace.log"
        if (Test-Path $startupLog) {
            Write-Host ""
            Write-Host "Retrace exited during startup. Last startup log entries:" -ForegroundColor Yellow
            Get-Content $startupLog -Tail 40 | ForEach-Object { Write-Host $_ }
        }
        throw "Retrace installed successfully but exited during startup. Run DIAGNOSE_RETRACE.cmd and send the result back to ChatGPT."
    }

    Write-Host ""
    Write-Host "Retrace V0.2.0 is installed and running." -ForegroundColor Green
    Write-Host "Installed at: $installDir"
    Write-Host "Retrace data: $env:LOCALAPPDATA\Retrace"
    Write-Host "Install log: $installLog"
    Write-Host ""
    Write-Host "The private SDK under $dotnetRoot is only needed to rebuild future test versions." -ForegroundColor DarkGray
}
catch {
    Write-Host ""
    Write-Host "INSTALLATION ERROR" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Full installer log: $installLog" -ForegroundColor Yellow
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}
