param([string]$Destination=(Join-Path $PSScriptRoot '..\artifacts\publish\tools'))
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$cache=Join-Path $repoRoot 'artifacts\dependencies'
New-Item -ItemType Directory -Path $cache,$Destination -Force | Out-Null
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
foreach($dependency in $manifest){
    $download=Join-Path $cache $dependency.Name
    if(!(Test-Path -LiteralPath $download)){Invoke-WebRequest -Uri $dependency.Url -OutFile $download}
    $actual=(Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
    if($actual -ne $dependency.Sha256){throw "Hash mismatch: $($dependency.Name). Locked builds never accept a silently changed upstream artifact."}
}
$ffmpeg=Join-Path $cache 'ffmpeg'
if(!(Test-Path -LiteralPath $ffmpeg)){Expand-Archive -LiteralPath (Join-Path $cache $manifest[0].Name) -DestinationPath $ffmpeg}
Get-ChildItem -LiteralPath $ffmpeg -Filter '*.exe' -Recurse | Where-Object {$_.Name -in @('ffmpeg.exe','ffprobe.exe')} | Copy-Item -Destination $Destination
$gs=Join-Path $cache 'ghostscript'
if(!(Test-Path -LiteralPath "$gs\bin\gswin64c.exe")){
    $sevenZip='C:\Program Files\7-Zip\7z.exe'
    if(!(Test-Path -LiteralPath $sevenZip)){throw '7-Zip is required to extract the official Ghostscript installer without installing it.'}
    & $sevenZip x (Join-Path $cache $manifest[1].Name) "-o$gs" -y -bso0
    if($LASTEXITCODE -ne 0){throw 'Ghostscript extraction failed.'}
}
$gsTarget=Join-Path $Destination 'ghostscript'
New-Item -ItemType Directory -Path $gsTarget -Force | Out-Null
foreach($folder in @('bin','lib','Resource','iccprofiles')){Copy-Item -LiteralPath (Join-Path $gs $folder) -Destination $gsTarget -Recurse -Force}
Get-ChildItem -LiteralPath "$gsTarget\bin" -File | Copy-Item -Destination $gsTarget -Force
$licenseTarget=Join-Path $Destination 'licenses'
New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
Get-ChildItem -LiteralPath $ffmpeg -Filter 'LICENSE*' -Recurse | Copy-Item -Destination $licenseTarget -Force
Copy-Item -LiteralPath "$gs\doc\COPYING" -Destination "$licenseTarget\Ghostscript-COPYING" -ErrorAction SilentlyContinue
