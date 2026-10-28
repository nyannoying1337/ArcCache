param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $Directory).Path
$manifest=Join-Path $root 'source-provenance.json'
if(!(Test-Path -LiteralPath $manifest)){throw 'SCSKiller payload has no public-source provenance manifest.'}
$provenance=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if($provenance.Public -ne $true -or $provenance.Revision -ne 'cf86e74e260154b1314afacac3e79052a7f44fde'){throw 'SCSKiller payload is not from the approved public source. Local alpha/beta builds cannot be distributed.'}
$patch=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'scskiller-public-readonly-recorders.patch')).Replace("`r`n","`n")
$sha=[Security.Cryptography.SHA256]::Create();try{$patchHash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($patch))).Replace('-','')}finally{$sha.Dispose()}
if($provenance.PatchSHA256 -ne $patchHash){throw 'SCSKiller payload lacks the reviewed protection for the supplied recorder.'}
$expected=@{}
foreach($entry in $provenance.Files.PSObject.Properties){
    $relative=$entry.Name.Replace('/',[IO.Path]::DirectorySeparatorChar)
    $target=[IO.Path]::GetFullPath((Join-Path $root $relative))
    if(!$target.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid provenance path.'}
    if(!(Test-Path -LiteralPath $target -PathType Leaf) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.Value){throw "SCSKiller payload changed after its public build: $relative"}
    $expected[$target]=$true
}
foreach($file in Get-ChildItem -LiteralPath $root -File -Recurse){
    if($file.Name -eq 'oodle-data-shared.dll'){throw 'Native Oodle is downloaded locally on first use; it cannot be included in public packages.'}
    if($file.Name -in @('auth.dat','recorder.key','alpha-pass','recorder.revoked','settings.json') -or $file.FullName -match '[\\/](recorder-hosts|scskiller-own-data)[\\/]'){throw 'Local user data is forbidden in a distributable SCSKiller payload.'}
    if($file.FullName -eq $manifest -or $file.Name -in @('LICENSE','LICENSE-EXCEPTION.txt','THIRD-PARTY-NOTICES.md')){continue}
    if(!$expected.ContainsKey($file.FullName)){throw "Unaccounted file in SCSKiller payload: $($file.Name)"}
}
Write-Output 'Public SCSKiller payload: source revision and every file hash verified.'
