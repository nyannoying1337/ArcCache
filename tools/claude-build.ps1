# Arc Cache + SCSKiller-Arc build and test (run outside any sandbox: cmd /c tools\claude-build.cmd).
# Builds only; never runs driver trials, game setups, compiles, Reset or Cleanup.
# Log: tools\claude-build.log, last line "CLAUDE-BUILD-RESULT: OK" or "CLAUDE-BUILD-RESULT: FAIL <step>".
param([switch]$SkipScs, [switch]$SkipApp, [switch]$NoLaunch, [switch]$GitStatus, [switch]$GitCommit, [switch]$GitPush, [switch]$Release, [switch]$Task, [string]$Version = '', [string]$RequestFile)
if ($RequestFile -and (Test-Path -LiteralPath $RequestFile)) {
    $words = (Get-Content -LiteralPath $RequestFile -Raw) -split '\s+' | Where-Object { $_ }
    $SkipScs = $words -contains '-SkipScs'; $SkipApp = $words -contains '-SkipApp'; $NoLaunch = $words -contains '-NoLaunch'; $GitStatus = $words -contains '-GitStatus'; $GitCommit = $words -contains '-GitCommit'; $GitPush = $words -contains '-GitPush'; $Release = $words -contains '-Release'; $Task = $words -contains '-Task'
    $v = $words | Where-Object { $_ -match '^\d{4}\.\d{1,2}\.\d{1,2}$' } | Select-Object -First 1; if ($v) { $Version = $v }
}
$ErrorActionPreference = 'Stop'
$tools = $PSScriptRoot
$repo = Split-Path $tools -Parent                      # ...\work\ArcCache
$work = Split-Path $repo -Parent                       # ...\work
$scs = Join-Path $work 'SCSKiller'
$dotnet = Join-Path $work 'dotnet-sdk\dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
$log = Join-Path $tools 'claude-build.log'
$appOut = Join-Path $repo 'src\ArcCache.WinUI\App\bin\Debug\net10.0-windows10.0.26100.0\win-x64'
$scsOut = Join-Path $repo 'out\SCSKiller'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
Remove-Item $log -ErrorAction SilentlyContinue
function Say([string]$m) { $l = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $m; Add-Content -LiteralPath $log -Value $l; Write-Host $l }
$step = 'start'
function Run([string]$name, [string]$exe, [string[]]$a) {
    $script:step = $name
    Say "== $name"
    Say ("   " + $exe + ' ' + ($a -join ' '))
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'   # native stderr must not throw
    $out = & $exe @a 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = $old
    # keep the log readable: errors, warnings, test summaries and the tail
    $keep = $out | Where-Object { $_ -match 'error|warn|fail|passed|Passed|FAIL|PASS|Build succeeded|Total|exception' } | Select-Object -First 200
    foreach ($k in $keep) { Add-Content -LiteralPath $log -Value "   | $k" }
    foreach ($k in ($out | Select-Object -Last 15)) { Add-Content -LiteralPath $log -Value "   > $k" }
    if ($code -ne 0) { throw "$name failed with exit code $code" }
    Say "   ok"
}
if ($Task) {
    # -Task: runs the local, git-ignored tools\claude-task.ps1 (one-off maintenance) and logs its output.
    Remove-Item $log -ErrorAction SilentlyContinue
    $t = Join-Path $tools 'claude-task.ps1'
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & $t -Repo $repo -Work $work 2>&1 | ForEach-Object { Add-Content -LiteralPath $log -Value "$_" }; $res = 'OK' }
    catch { Add-Content -LiteralPath $log -Value "ERROR: $_"; $res = 'FAIL task' }
    $ErrorActionPreference = $old
    Add-Content -LiteralPath $log -Value "CLAUDE-BUILD-RESULT: $res"
    exit 0
}
if ($GitCommit -or $GitPush) {
    # -GitCommit: commits everything not ignored in both repos with the messages in tools\claude-commit-<repo>.txt (no push).
    # -GitPush: pushes the current branch of both repos to origin. Nothing else.
    Remove-Item $log -ErrorAction SilentlyContinue
    $git = (Get-Command git -ErrorAction SilentlyContinue).Source
    foreach ($pair in @(@($repo, 'arccache'), @($scs, 'scskiller'))) {
        $r = $pair[0]; Add-Content -LiteralPath $log -Value "===== $r"
        $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        if ($GitCommit) {
            $msg = Join-Path $tools ("claude-commit-" + $pair[1] + ".txt")
            if (!(Test-Path $msg)) { Add-Content -LiteralPath $log -Value "no message file $msg"; continue }
            $out = & $git -C $r add -A 2>&1; $out | ForEach-Object { Add-Content -LiteralPath $log -Value "$_" }
            $out = & $git -C $r -c user.name=nyannoying1337 commit -F $msg 2>&1; $code = $LASTEXITCODE
            $out | Select-Object -First 80 | ForEach-Object { Add-Content -LiteralPath $log -Value "$_" }
            Add-Content -LiteralPath $log -Value "commit exit $code"
            & $git -C $r log --stat -1 2>&1 | Select-Object -First 120 | ForEach-Object { Add-Content -LiteralPath $log -Value "$_" }
        }
        if ($GitPush) {
            $branch = (& $git -C $r rev-parse --abbrev-ref HEAD).Trim()
            $out = & $git -C $r push origin $branch 2>&1; $code = $LASTEXITCODE
            $out | ForEach-Object { Add-Content -LiteralPath $log -Value "$_" }
            Add-Content -LiteralPath $log -Value "push $branch exit $code"
        }
        $ErrorActionPreference = $old
    }
    Add-Content -LiteralPath $log -Value 'CLAUDE-BUILD-RESULT: OK'
    exit 0
}
if ($GitStatus) {   # read only: what would be committed, and how pushing would authenticate
    Remove-Item $log -ErrorAction SilentlyContinue
    $git = (Get-Command git -ErrorAction SilentlyContinue).Source
    Add-Content -LiteralPath $log -Value "git=$git gh=$((Get-Command gh -ErrorAction SilentlyContinue).Source)"
    foreach ($r in @($repo, $scs)) {
        Add-Content -LiteralPath $log -Value "===== $r"
        foreach ($a in @(@('remote','-v'), @('branch','-vv'), @('log','--oneline','-8'), @('config','--get','credential.helper'), @('config','--get','user.name'), @('config','--get','user.email'), @('stash','list'), @('status','--porcelain=v1','-uall'))) {
            Add-Content -LiteralPath $log -Value ("--- git " + ($a -join ' '))
            $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
            $out = & $git -C $r @a 2>&1 | ForEach-Object { "$_" } | Select-Object -First 400
            $ErrorActionPreference = $old
            foreach ($o in $out) { Add-Content -LiteralPath $log -Value $o }
        }
    }
    Add-Content -LiteralPath $log -Value 'CLAUDE-BUILD-RESULT: OK'
    exit 0
}
try {
    Say "repo=$repo scs=$scs dotnet=$dotnet"
    Say ("whoami=" + [Environment]::UserName + " localappdata=" + $env:LOCALAPPDATA)
    if ($env:LOCALAPPDATA -match 'Packages\\') { throw 'running inside a packaged (sandboxed) process; start it from Explorer or Run instead' }

    # MSVC + CMake
    $vs = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools'
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) { $p = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath; if ($p) { $vs = $p } }
    $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
    if (!(Test-Path $vcvars)) { throw "vcvars64.bat not found under $vs" }
    $envBat = Join-Path $env:TEMP 'claude-vcvars-env.cmd'
    Set-Content -LiteralPath $envBat -Encoding ASCII -Value @("@call `"$vcvars`" >nul", '@set')
    cmd.exe /c $envBat | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1], $matches[2]) } }
    Remove-Item $envBat -ErrorAction SilentlyContinue
    # vcvars sets Platform=x64, which MSBuild reads as a property: the app would build into bin\x64\Debug instead of bin\Debug
    Remove-Item Env:Platform -ErrorAction SilentlyContinue
    $cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
    if (!$cmake) { $c = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'; if (Test-Path $c) { $cmake = $c } }
    Say "vs=$vs cmake=$cmake"

    $running = if ($SkipApp) { $null } else { Get-Process ArcCache -ErrorAction SilentlyContinue }
    if ($running) { Say "closing running Arc Cache ($($running.Id -join ','))"; $running | ForEach-Object { $_.CloseMainWindow() | Out-Null }; Start-Sleep 3; Get-Process ArcCache -ErrorAction SilentlyContinue | Stop-Process -Force }

    if (!$SkipScs) {
        if (!$cmake) { throw 'cmake not found (install the VS "C++ CMake tools" component)' }
        $nb = Join-Path $scs 'proxy\build'
        # The recorder's version: just above the official SCSKiller release it must replace (1.3.0), so either app keeps
        # this exe.local-aware recorder until the official app ships a newer one. Raise it with each official release.
        $nativeVersion = '1.3.0-arc.1'
        Run 'scs-native-configure' $cmake @('-S', (Join-Path $scs 'proxy'), '-B', $nb, '-A', 'x64', "-DSCSK_VERSION=$nativeVersion")
        Run 'scs-native-build' $cmake @('--build', $nb, '--config', 'Release', '--target', 'scskiller', 'scskiller_warm', 'scskiller_warm_segheap')
        Run 'scs-tests' $dotnet @('test', (Join-Path $scs 'tests\SCSKiller.Tests\SCSKiller.Tests.csproj'), '-c', 'Release', '--filter', 'FullyQualifiedName~ArcCacheDriver|FullyQualifiedName~IntelCacheCap|FullyQualifiedName~WarmerTests')
        $cli = Join-Path $scsOut 'cli'; $native = Join-Path $scsOut 'native'
        if (Test-Path $scsOut) { Remove-Item -LiteralPath $scsOut -Recurse -Force }
        $pubArgs = @('publish', (Join-Path $scs 'src\SCSKiller.Cli\SCSKiller.Cli.csproj'), '-c', 'Release', '-o', $cli)
        if ($Version) { $pubArgs += "-p:Version=1.2.5-arc.$($Version -replace '\.','')" }
        Run 'scs-cli-publish' $dotnet $pubArgs
        New-Item -ItemType Directory -Force -Path (Join-Path $native 'segheap') | Out-Null
        $rel = Join-Path $nb 'Release'
        Copy-Item (Join-Path $rel 'scskiller_warm.exe'), (Join-Path $rel 'd3d12.dll') -Destination $native
        Get-ChildItem $rel -Filter 'amd_ags_x64.dll' | Copy-Item -Destination $native
        Copy-Item (Join-Path $rel 'segheap\scskiller_warm.exe') -Destination (Join-Path $native 'segheap')
        try { Run 'scs-fetch-codecs' (Join-Path $cli 'scskiller.exe') @('fetch-codecs', $cli) } catch { Say "   WARNING: $($_.Exception.Message) (compiles that need codecs may fail)" }
        foreach ($f in @((Join-Path $cli 'scskiller.exe'), (Join-Path $native 'scskiller_warm.exe'), (Join-Path $native 'd3d12.dll'))) {
            Say ("   {0}  {1}" -f (Get-FileHash $f -Algorithm SHA256).Hash, $f)
        }
        # point Arc Cache at this CLI (the setting the app's Settings page writes)
        $bridge = Join-Path $env:LOCALAPPDATA 'IntelArcCacheCap\scskiller-bridge.json'
        if (Test-Path $bridge) { Copy-Item $bridge "$bridge.bak" -Force }
        @{ CliPath = (Join-Path $cli 'scskiller.exe') } | ConvertTo-Json -Compress | Set-Content -LiteralPath $bridge -Encoding UTF8
        Say "   scskiller-bridge.json -> $(Join-Path $cli 'scskiller.exe')"
    }

    if (!$SkipApp) {
        Run 'engine-build' 'cmd.exe' @('/c', (Join-Path $repo 'src\ArcCache.Engine\build.cmd'))
        $engineTests = Join-Path $repo 'src\ArcCache.Engine\bin\ArcCap.Tests.exe'
        foreach ($mode in @('--capabilities', '--driver-resolution', '--isolation-proxy-tests', '--test-historical-trial-receipt', '--hash-cache', '--repair', '--module-paths')) { Run "engine-tests $mode" $engineTests @($mode) }
        # the installed Intel package, read only
        # the package the Arc adapter actually uses (InstalledDisplayDrivers lists its DriverStore UMD paths)
        $pkg = $null
        $vc = Get-CimInstance Win32_VideoController | Where-Object { $_.Name -match 'Intel' -and $_.InstalledDisplayDrivers } | Select-Object -First 1
        if ($vc) { $first = ($vc.InstalledDisplayDrivers -split ',')[0]; if ($first) { $pkg = Get-Item (Split-Path $first -Parent) -ErrorAction SilentlyContinue }; Say "   adapter $($vc.Name) driver $($vc.DriverVersion)" }
        if ($pkg) {
            Say "   driver package $($pkg.FullName)"
            foreach ($d in Get-ChildItem $pkg.FullName -Filter 'igd12um64xe*.dll') { Say ("   {0}  {1}  {2}" -f (Get-FileHash $d.FullName -Algorithm SHA256).Hash, $d.VersionInfo.FileVersion, $d.Name) }
            $xe2 = Join-Path $pkg.FullName 'igd12um64xe2.dll'; $xe3 = Join-Path $pkg.FullName 'igd12um64xe3.dll'; $xeh = Join-Path $pkg.FullName 'igd12um64xeh.dll'
            Run 'engine-planner xe2' $engineTests @('--planner', $xe2)
            if (Test-Path $xe3) { Run 'engine-planner xe3' $engineTests @('--planner', $xe3) }
            if (Test-Path $xeh) { Run 'engine-xe1-guard' $engineTests @('--xe1-guard', $xeh) }
            Run 'engine-inspect-serialize' $engineTests @('--inspect-serialize', $xe2)
            # these pin the reviewed 32.0.101.9034 bytes; on another driver they can't pass
            if ((Get-FileHash $xe2 -Algorithm SHA256).Hash -eq '1A552E471AE986A52D045C4112E1980FB7171AABEBFF93C027A4BBA10A3C6229') {
                Run 'engine-trial-tests' (Join-Path $repo 'src\ArcCache.Engine\bin\ArcCache.DriverTrialTests.exe') @($xe2, $xe3, (Join-Path $repo 'src\ArcCache.Probe\bin\ArcCache.DriverProbe.exe'))
            } else { Say '   engine-trial-tests skipped: they need the reviewed 9034 driver files (installed driver is newer)' }
        } else { Say '   WARNING: no Intel driver package found; driver-based engine tests skipped' }
        Run 'app-build' $dotnet @('build', (Join-Path $repo 'src\ArcCache.WinUI\App\ArcCache.WinUI.csproj'), '-c', 'Debug')
        Run 'app-tests-build' $dotnet @('build', (Join-Path $repo 'src\ArcCache.WinUI\Tests\ArcCache.Tests.csproj'), '-c', 'Debug')
        $testsExe = Join-Path $repo 'src\ArcCache.WinUI\Tests\bin\Debug\net10.0-windows\ArcCache.Tests.exe'
        if ((Get-Item $testsExe).LastWriteTime -lt (Get-Item (Join-Path $repo 'src\ArcCache.WinUI\Tests\Program.cs')).LastWriteTime) { throw "the tests exe is older than its source ($testsExe)" }
        Run 'app-tests' $testsExe @()
        $appExe = Join-Path $appOut 'ArcCache.dll'
        if ((Get-Item $appExe).LastWriteTime -lt (Get-Item (Join-Path $repo 'src\ArcCache.WinUI\App\MainWindow.xaml.cs')).LastWriteTime) { throw "the app in $appOut is older than its source" }
        $probe = Join-Path $appOut 'ArcCache.DriverProbe.exe'
        Say ("   probe {0}" -f (Get-FileHash $probe -Algorithm SHA256).Hash)
        if (!$NoLaunch) { Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$(Join-Path $appOut 'ArcCache.exe')`""; Say '   Arc Cache started through explorer.exe' }
    }
    if ($Release) {
        if ($Version -notmatch '^\d{4}\.\d{1,2}\.\d{1,2}$') { throw 'release needs a version like 2026.10.16' }
        if (!(Test-Path (Join-Path $scsOut 'cli\scskiller.exe'))) { throw 'build SCSKiller first (release without -SkipScs)' }
        $step = 'release'
        $relRoot = Join-Path $repo 'out\release'; $payload = Join-Path $relRoot "ArcCache-$Version"
        if (Test-Path $payload) { Remove-Item -LiteralPath $payload -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $relRoot | Out-Null
        Run 'release-publish' $dotnet @('publish', (Join-Path $repo 'src\ArcCache.WinUI\App\ArcCache.WinUI.csproj'), '-c', 'Release', '-p:Platform=x64', "-p:Version=$Version", '-o', $payload)
        Copy-Item (Join-Path $repo 'LICENSE'), (Join-Path $repo 'NOTICE.md') -Destination $payload
        Copy-Item (Join-Path $repo 'src\ArcCache.WinUI\docs\QUICKSTART.md') -Destination (Join-Path $payload 'GETTING-STARTED.md')
        $prev = Get-ChildItem (Join-Path $repo 'out') -Directory -Filter 'ArcCache-Release-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($prev) { Get-ChildItem $prev.FullName -File -Filter '*.txt' | Where-Object { $_.Name -match 'LICENSE|NOTICE' } | Copy-Item -Destination $payload }
        Copy-Item -LiteralPath $scsOut -Destination (Join-Path $payload 'SCSKiller') -Recurse
        Copy-Item (Join-Path $scs 'LICENSE'), (Join-Path $scs 'LICENSE-EXCEPTION.txt'), (Join-Path $scs 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $payload 'SCSKiller')
        Get-ChildItem $payload -Recurse -Filter '*.pdb' | Remove-Item -Force
        foreach ($need in @('ArcCache.exe', 'ArcCache.SafetyEngine.exe', 'ArcCache.DriverProbe.exe', 'SCSKiller\cli\scskiller.exe', 'SCSKiller\native\scskiller_warm.exe', 'SCSKiller\native\d3d12.dll')) {
            if (!(Test-Path (Join-Path $payload $need))) { throw "release payload is missing $need" }
        }
        $zip = Join-Path $relRoot "ArcCache-$Version-Windows-x64.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -CompressionLevel Optimal
        $h = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant(); Set-Content -LiteralPath "$zip.sha256" -Value "$h  $(Split-Path -Leaf $zip)" -Encoding ascii
        Say ("   zip {0} MB  {1}" -f [math]::Round((Get-Item $zip).Length / 1MB, 1), $h)
        $iscc = $null
        foreach ($k in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')) {
            Get-ItemProperty $k -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like 'Inno Setup*' -and $_.InstallLocation } | ForEach-Object { $c = Join-Path $_.InstallLocation 'ISCC.exe'; if (!$iscc -and (Test-Path $c)) { $iscc = $c } }
        }
        if (!$iscc) { foreach ($c in @((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'))) { if (!$iscc -and (Test-Path $c)) { $iscc = $c } } }
        if (!$iscc) {   # per-user or custom installs: follow the Start menu shortcuts
            $shell = New-Object -ComObject WScript.Shell
            foreach ($root in @((Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'), (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'))) {
                Get-ChildItem $root -Recurse -Filter '*.lnk' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like '*Inno Setup*' } | ForEach-Object {
                    $t = $shell.CreateShortcut($_.FullName).TargetPath
                    if ($t) { $c = Join-Path (Split-Path $t -Parent) 'ISCC.exe'; if (!$iscc -and (Test-Path $c)) { $iscc = $c } }
                }
            }
        }
        if (!$iscc) {
            foreach ($root in @((Join-Path $env:LOCALAPPDATA 'Programs'), $env:ProgramFiles, ${env:ProgramFiles(x86)}, (Join-Path $env:USERPROFILE 'Downloads'), (Join-Path $env:USERPROFILE 'Desktop'), $work, (Join-Path $env:USERPROFILE 'Documents\Codex'))) {
                if ($iscc -or !$root -or !(Test-Path $root)) { continue }
                $hit = Get-ChildItem $root -Recurse -Depth 4 -Filter 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($hit) { $iscc = $hit.FullName }
            }
        }
        if (!$iscc) { $hit = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages\*\LocalCache\Local\Programs\Inno Setup *\ISCC.exe') -ErrorAction SilentlyContinue | Select-Object -First 1; if ($hit) { $iscc = $hit.FullName } }
        Say "   iscc=$iscc"
        $instArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $repo 'packaging\build-installer.ps1'), '-PayloadDirectory', $payload, '-Version', $Version, '-OutputDirectory', $relRoot)
        if ($iscc) { $instArgs += @('-CompilerPath', $iscc); Run 'release-installer' 'powershell.exe' $instArgs }
        else { Say '   WARNING: Inno Setup (ISCC.exe) not found - installer skipped, ZIP only (winget install JRSoftware.InnoSetup)' }
        Get-ChildItem $relRoot -File | ForEach-Object { Say ("   {0}  {1:N0} bytes" -f $_.Name, $_.Length) }
    }
    Say 'CLAUDE-BUILD-RESULT: OK'
} catch {
    Say "ERROR: $($_.Exception.Message)"
    Say "CLAUDE-BUILD-RESULT: FAIL $step"
    exit 1
}
