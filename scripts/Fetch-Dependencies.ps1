param([string]$Destination=(Join-Path $PSScriptRoot '..\artifacts\publish-next\tools'))
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$cache=Join-Path $repoRoot 'artifacts\dependencies'
New-Item -ItemType Directory -Path $cache,$Destination -Force | Out-Null
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
foreach($dependency in $manifest){
    $download=Join-Path $cache $dependency.Name
    if(!(Test-Path -LiteralPath $download)){Invoke-WebRequest -Uri $dependency.Url -OutFile $download}
    $actual=(Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
    if($actual.ToLowerInvariant() -ne $dependency.Sha256.ToLowerInvariant()){throw "Hash mismatch: $($dependency.Name). Locked builds never accept a silently changed upstream artifact."}
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
$licenseTarget=Join-Path $Destination 'licenses'
New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
$gsTarget=Join-Path $Destination 'ghostscript'
New-Item -ItemType Directory -Path $gsTarget -Force | Out-Null
foreach($folder in @('bin','lib','Resource','iccprofiles')){Copy-Item -LiteralPath (Join-Path $gs $folder) -Destination $gsTarget -Recurse -Force}
Get-ChildItem -LiteralPath "$gsTarget\bin" -File | Copy-Item -Destination $gsTarget -Force
if(!(Test-Path -LiteralPath (Join-Path $Destination 'libreoffice\program\soffice.com'))){
    $lessMsi=Join-Path $cache $manifest[3].Name
    $extract=Join-Path $cache 'lessmsi-tool'
    if(!(Test-Path -LiteralPath (Join-Path $extract 'lessmsi.exe'))){
        New-Item -ItemType Directory -Path $extract -Force | Out-Null
        Expand-Archive -LiteralPath $lessMsi -DestinationPath $extract -Force
    }
    $extractionRoot=Join-Path $cache 'libreoffice-extract'
    if(Test-Path -LiteralPath $extractionRoot){Remove-Item -LiteralPath $extractionRoot -Recurse -Force}
    New-Item -ItemType Directory -Path $extractionRoot -Force | Out-Null
    $extractDirectory=$extractionRoot.TrimEnd('\')+'\'
    $extractArgs=@('x',(Join-Path $cache $manifest[2].Name),$extractDirectory)
    & (Join-Path $extract 'lessmsi.exe') @extractArgs 2>&1 | Out-Null
    if($LASTEXITCODE -ne 0){throw 'LibreOffice MSI extraction failed.'}
    $libreOfficeSource=Join-Path $extractionRoot 'SourceDir\LibreOffice'
    if(!(Test-Path -LiteralPath (Join-Path $libreOfficeSource 'program\soffice.exe'))){throw 'LibreOffice program directory was not found in its package.'}
    Get-ChildItem -LiteralPath $libreOfficeSource -File | Where-Object {$_.Name -in @('LICENSE.html','license.txt','NOTICE')} | Copy-Item -Destination $licenseTarget -Force
    $libreOfficeTarget=Join-Path $Destination 'libreoffice'
    Copy-Item -LiteralPath $libreOfficeSource -Destination $libreOfficeTarget -Recurse -Force
    Remove-Item -LiteralPath $extractionRoot -Recurse -Force
}
$profileTemplate=Join-Path $Destination 'libreoffice-profile'
if(!(Test-Path -LiteralPath (Join-Path $profileTemplate 'user\registrymodifications.xcu'))){
    New-Item -ItemType Directory -Path $profileTemplate -Force | Out-Null
    $profileUri=[Uri]::new([IO.Path]::GetFullPath($profileTemplate)).AbsoluteUri
    $profileInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $Destination 'libreoffice\program\soffice.com'))
    $profileInfo.WorkingDirectory=Join-Path $Destination 'libreoffice\program'
    $profileInfo.UseShellExecute=$false
    $profileInfo.CreateNoWindow=$true
    foreach($argument in @("-env:UserInstallation=$profileUri",'--headless','--invisible','--nodefault','--nologo','--terminate_after_init')){$profileInfo.ArgumentList.Add($argument)}
    $profileProcess=[Diagnostics.Process]::Start($profileInfo)
    if(!$profileProcess.WaitForExit(120000)){$profileProcess.Kill($true);throw 'LibreOffice profile initialization exceeded two minutes.'}
    if($profileProcess.ExitCode -ne 0){throw "LibreOffice profile initialization failed ($($profileProcess.ExitCode))."}
    $profileProcess.Dispose()
}
Get-ChildItem -LiteralPath $ffmpeg -Filter 'LICENSE*' -Recurse | Copy-Item -Destination $licenseTarget -Force
Copy-Item -LiteralPath "$gs\doc\COPYING" -Destination "$licenseTarget\Ghostscript-COPYING" -ErrorAction SilentlyContinue
