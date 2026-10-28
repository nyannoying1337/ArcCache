param([string]$Dotnet='dotnet',[string]$Cmake='cmake',[Parameter(Mandatory=$true)][string]$Output,[string]$ScsKillerRepository,[switch]$LocalOnly)
$ErrorActionPreference='Stop'
# The core app is standalone. Optional CLI/native packaging uses an explicit separate source checkout.
$repo=$null
if($ScsKillerRepository){$repo=[IO.Path]::GetFullPath($ScsKillerRepository);if(!(Test-Path -LiteralPath (Join-Path $repo 'src/SCSKiller.Cli/SCSKiller.Cli.csproj'))){throw 'Choose a valid separate SCSKiller source checkout.'}}
if($repo){
    & (Join-Path $PSScriptRoot '..\..\packaging\assert-public-scskiller.ps1') -Repository $repo -ApplyReviewedPatch | Out-Null
}
$outputPath=[IO.Path]::GetFullPath($Output)
# Fresh output only; never removes existing releases or game/system paths.
if(Test-Path -LiteralPath $outputPath){throw 'Choose a fresh output directory.'}
function RunTool([string]$Tool,[string[]]$ToolArgs){& $Tool @ToolArgs;if($LASTEXITCODE -ne 0){throw "$Tool failed with exit code $LASTEXITCODE"}}
RunTool $Dotnet @('publish',(Join-Path $PSScriptRoot 'App/ArcCache.WinUI.csproj'),'-c','Release','-p:Platform=x64','-o',$outputPath)
if(!$repo){Write-Output 'Standalone Arc Cache app ready. Optional SCSKiller CLI is not included.';exit 0}
$cli=Join-Path $outputPath 'SCSKiller/cli'
$native=Join-Path $outputPath 'SCSKiller/native'
$nativeBuild=Join-Path $PSScriptRoot 'native-build'
RunTool $Dotnet @('publish',(Join-Path $repo 'src/SCSKiller.Cli/SCSKiller.Cli.csproj'),'-c','Release','-p:DebugType=None','-p:DebugSymbols=false','-o',$cli)
RunTool $Cmake @('-S',(Join-Path $repo 'proxy'),'-B',$nativeBuild,'-A','x64')
RunTool $Cmake @('--build',$nativeBuild,'--config','Release','--target','scskiller_warm','scskiller_warm_segheap')
New-Item -ItemType Directory -Force -Path (Join-Path $native 'segheap') | Out-Null
Copy-Item -LiteralPath (Join-Path $nativeBuild 'Release/scskiller_warm.exe'),(Join-Path $nativeBuild 'Release/d3d12.dll') -Destination $native
Copy-Item -LiteralPath (Join-Path $nativeBuild 'Release/segheap/scskiller_warm.exe') -Destination (Join-Path $native 'segheap')
RunTool (Join-Path $cli 'scskiller.exe') @('fetch-codecs',$cli)
& (Join-Path $PSScriptRoot '..\..\packaging\write-scskiller-provenance.ps1') -Repository $repo -Directory (Join-Path $outputPath 'SCSKiller')
Write-Output "Companion ready: $(Join-Path $outputPath 'ArcCache.exe')"
