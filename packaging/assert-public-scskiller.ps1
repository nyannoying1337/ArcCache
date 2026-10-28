param([Parameter(Mandatory=$true)][string]$Repository,[switch]$ApplyReviewedPatch)
$ErrorActionPreference='Stop'
# This reviewed fork revision is based on BlueHeisenberg's PUBLIC v1.2.5, not a supporter source archive.
# Add another revision only after reviewing its public upstream provenance and our changes.
$approved='cf86e74e260154b1314afacac3e79052a7f44fde'
# Upstream rewrote the commit identity; bf2571a and public b10c1e3 have the identical source tree below.
$upstream='bf2571af3db4d1d3c49aeb05cda170571eae0704'
$publicTree='e27cb1acfc09c922a4e9f647be8ab74092bed3bc'
$revision=(& git -C $Repository rev-parse HEAD 2>$null)
if($LASTEXITCODE -ne 0 -or !$revision -or $revision.Trim() -ne $approved){throw 'SCSKiller source is not the reviewed public revision. Unpublished alpha/beta source must never be packaged or pushed.'}
& git -C $Repository merge-base --is-ancestor $upstream HEAD 2>$null
if($LASTEXITCODE -ne 0){throw 'The reviewed public upstream base cannot be verified.'}
if((& git -C $Repository rev-parse "$upstream`^{tree}").Trim() -ne $publicTree){throw 'The public upstream source tree does not match the reviewed contents.'}
$patchPath=Join-Path $PSScriptRoot 'scskiller-public-readonly-recorders.patch'
$patch=[IO.File]::ReadAllText($patchPath).Replace("`r`n","`n")
$diff=((& git -C $Repository diff --binary HEAD) -join "`n")
if(!$diff -and $ApplyReviewedPatch){& git -C $Repository apply -- $patchPath;if($LASTEXITCODE -ne 0){throw 'The reviewed public recorder-protection patch could not be applied.'};$diff=((& git -C $Repository diff --binary HEAD) -join "`n")}
if(($diff+"`n") -ne $patch){throw 'Public packaging requires exactly the reviewed public source and the recorder-protection patch; other source changes are blocked.'}
if(& git -C $Repository ls-files --others --exclude-standard){throw 'Untracked files in the SCSKiller checkout must be reviewed or removed before packaging.'}
$sha=[Security.Cryptography.SHA256]::Create();try{$patchHash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($patch))).Replace('-','')}finally{$sha.Dispose()}
[pscustomobject]@{Public=$true;Revision=$approved;PatchSHA256=$patchHash;Upstream='https://github.com/BlueHeisenberg/SCSKiller/commit/b10c1e3';Fork='https://github.com/nyannoying1337/SCSKiller/commit/'+$approved}
