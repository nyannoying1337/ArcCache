param([Parameter(Mandatory=$true)][string]$Repository,[Parameter(Mandatory=$true)][string]$Directory,[switch]$LocalOnly)
$ErrorActionPreference='Stop'
$source=if($LocalOnly){[pscustomobject]@{Public=$false;Revision=(& git -C $Repository rev-parse HEAD);Upstream=$null;Fork=$null}}else{& (Join-Path $PSScriptRoot 'assert-public-scskiller.ps1') -Repository $Repository}
$root=(Resolve-Path -LiteralPath $Directory).Path
$files=[ordered]@{}
Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object Name -ne 'source-provenance.json' | Sort-Object FullName | ForEach-Object {$files[$_.FullName.Substring($root.Length+1).Replace('\','/')]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
[ordered]@{Public=$source.Public;Revision=$source.Revision;PatchSHA256=$source.PatchSHA256;Upstream=$source.Upstream;Fork=$source.Fork;Files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'source-provenance.json') -Encoding UTF8
