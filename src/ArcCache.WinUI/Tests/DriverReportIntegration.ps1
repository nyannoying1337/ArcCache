param(
    [Parameter(Mandatory=$true)][string]$WorkerPath,
    [Parameter(Mandatory=$true)][string]$DriverFile
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $DriverFile -PathType Leaf)) {
    Write-Output "SKIP real-driver report integration: Intel driver DLL is unavailable: $DriverFile"
    exit 0
}
function Invoke-Worker([string]$Action, [string]$Path = '') {
    $request = @{ Action = $Action; ReadOnly = $true }
    if ($Path) { $request.DriverFile = [IO.Path]::GetFullPath($Path) }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = [IO.Path]::GetFullPath($WorkerPath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.Write(($request | ConvertTo-Json -Compress))
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "Worker $Action exited $($process.ExitCode): $stdout $stderr" }
        $reply = $stdout | ConvertFrom-Json
        if (-not $reply.Ok) { throw "Worker $Action failed: $($reply.Message)" }
        return $reply.Data
    } finally { $process.Dispose() }
}
$expected = @(512, 1024, 2048, 4096, 8192, 16384)
$direct = @(Invoke-Worker 'DriverLimitCapabilities' $DriverFile)
if ($direct.Count -ne 6 -or (@($direct | ForEach-Object { [int]$_.MiB } | Sort-Object) -join ',') -ne ($expected -join ',')) {
    throw 'DriverLimitCapabilities did not return all six per-size rows.'
}
Write-Output "PASS worker DriverLimitCapabilities action serialized real inspection data ($($direct.Count) sizes)."
foreach ($action in @('Status', 'CheckDriverCompatibility')) {
    $data = Invoke-Worker $action
    $limits = @($data.LimitCapabilities)
    if ($limits.Count -ne 6 -or (@($limits | ForEach-Object { [int]$_.MiB } | Sort-Object) -join ',') -ne ($expected -join ',')) {
        throw "$action did not return all six per-size capability rows."
    }
    if ($action -eq 'Status') {
        if ([string]::IsNullOrWhiteSpace([string]$data.CapabilityDriver)) {
            throw 'Status did not resolve a cache-capable DLL from the active Intel package.'
        }
        if ($null -eq $data.TrialInspection) {
            throw "Status resolved '$($data.CapabilityDriver)' but returned no driver inspection."
        }
        $trial = @($data.TrialCapabilities)
        if (-not ($trial | Where-Object { [int]$_.MiB -eq 512 -and $_.Buildable })) {
            throw 'The unchanged 512 MiB control is not reported buildable for the resolved driver.'
        }
        if (-not ($trial | Where-Object { [int]$_.MiB -gt 512 -and $_.Buildable })) {
            throw "No larger size is reported buildable for '$($data.CapabilityDriver)'."
        }
        Write-Output "PASS Status resolved $($data.CapabilityDriver); inspection and per-size buildability are present."
    }
    Write-Output "PASS worker $action report serialized real per-size capability data ($($limits.Count) sizes)."
}
$routing = Invoke-Worker 'Routing'
if ($routing.PSObject.Properties.Name -notcontains 'DeveloperOverride' -or $routing.PSObject.Properties.Name -notcontains 'RebootRequired') {
    throw 'Routing action omitted its two setting-status fields.'
}
if ($routing.PSObject.Properties.Name.Count -ne 2) {
    throw 'Routing action returned unrelated driver-scan fields; the cheap status path regressed.'
}
Write-Output 'PASS worker Routing action returns only the Windows-setting state.'
