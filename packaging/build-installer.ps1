param(
    [Parameter(Mandatory = $true)]
    [string] $PayloadDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$')]
    [string] $Version,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\out\installer'),
    [string] $CompilerPath,
    [switch] $LocalOnly
)

$ErrorActionPreference = 'Stop'

$payloadPath = (Resolve-Path -LiteralPath $PayloadDirectory).Path
Get-ChildItem -LiteralPath $payloadPath -Recurse -File | ForEach-Object {if($_.Name -in @('auth.dat','recorder.key','alpha-pass','recorder.revoked') -or $_.FullName -match '[\\/](recorder-hosts|scskiller-own-data|local-only-backups)[\\/]'){throw 'Local user versions, credentials and adapter caches cannot be packaged.'}}
if(Test-Path -LiteralPath (Join-Path $payloadPath 'SCSKiller')){& (Join-Path $PSScriptRoot 'assert-scskiller-payload.ps1') -Directory (Join-Path $payloadPath 'SCSKiller')}
$requiredFiles = @(
    'ArcCache.exe',
    'ArcCache.SafetyEngine.exe',
    'ArcCache.DriverProbe.exe',
    'ArcCache.Loader.dll',
    'LICENSE',
    'NOTICE.md',
    'GETTING-STARTED.md'
)
foreach ($file in $requiredFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $payloadPath $file) -PathType Leaf)) {
        throw "The release payload is missing required file: $file"
    }
}

if (!$CompilerPath) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) {
        $CompilerPath = $compilerCommand.Source
    } else {
        $knownPaths = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
        )
        $CompilerPath = $knownPaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup ISCC.exe was not found. Install Inno Setup 6 or 7, or pass -CompilerPath.'
}

$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$scriptPath = Join-Path $PSScriptRoot 'ArcCache.iss'
$compilerArgs = @(
    "/DAppVersion=$Version",
    "/DAppPayloadDir=$payloadPath",
    "/O$outputPath",
    $scriptPath
)
& $CompilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputPath "ArcCache-Setup-$Version-Windows-x64.exe"
if (!(Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Inno Setup did not create the expected installer: $installerPath"
}
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$installerPath.sha256"
Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path -Leaf $installerPath)" -Encoding ascii

Write-Output "Installer: $installerPath"
Write-Output "SHA-256:   $hash"
