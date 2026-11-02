param([string]$Msi=(Join-Path $PSScriptRoot '..\installer\bin\x64\Release\FileConverterNext-3.0.5-win-x64.msi'))
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$msiFile=(Resolve-Path -LiteralPath $Msi).Path
$installDirectory=Join-Path $env:LOCALAPPDATA 'Programs\File Converter Next'
$dataDirectory=Join-Path $env:LOCALAPPDATA 'FileConverterNext'
$settingsFile=Join-Path $dataDirectory 'settings.json'
$menuFile=Join-Path $dataDirectory 'menu.bin'
$handler='Software\Classes\*\shellex\ContextMenuHandlers\FileConverterNext.Modern'
$aotClass='Software\Classes\CLSID\{61699489-9EEA-43D6-BF5B-32DB32B7A301}'
$cppClass='Software\Classes\CLSID\{61699489-9EEA-43D6-BF5B-32DB32B7A302}'
if(Test-Path -LiteralPath $installDirectory){throw 'Installer acceptance requires an absent target directory; existing installation is preserved.'}
foreach($key in @($handler,$aotClass,$cppClass)){
    $existing=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key)
    if($existing){$existing.Dispose();throw 'Existing new-version shell registration is preserved; use a clean test account.'}
}
$originalSettings=if(Test-Path -LiteralPath $settingsFile){[IO.File]::ReadAllBytes($settingsFile)}else{$null}
$originalMenu=if(Test-Path -LiteralPath $menuFile){[IO.File]::ReadAllBytes($menuFile)}else{$null}
$legacyFile=Join-Path $dataDirectory 'Settings.user.xml'
$legacyHash=if(Test-Path -LiteralPath $legacyFile){(Get-FileHash -LiteralPath $legacyFile).Hash}else{$null}
$report=[Collections.Generic.List[object]]::new()
function Assert-True([bool]$value,[string]$name){if(!$value){throw $name};$report.Add([pscustomobject]@{Check=$name;Result='Passed'})}
function Run-Process([string]$file,[string[]]$arguments){
    $info=[Diagnostics.ProcessStartInfo]::new($file)
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    foreach($argument in $arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($info)
    try{$process.WaitForExit();return $process.ExitCode}finally{$process.Dispose()}
}
function Run-Msi([string[]]$arguments){Run-Process (Join-Path $env:SystemRoot 'System32\msiexec.exe') $arguments}
function Registry-Value([string]$key){
    $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
    $opened=$root.OpenSubKey($key)
    try{if($opened){$opened.GetValue('')}}finally{if($opened){$opened.Dispose()};$root.Dispose()}
}
$installed=$false
try{
    $blocked=Run-Msi @('/i',$msiFile,'LEGACY_USER=acceptance-condition-fixture','/qn','/norestart','/l*v',(Join-Path $repoRoot 'artifacts\msi-legacy-block.log'))
    Assert-True ($blocked -eq 1603) 'Old-version launch condition blocks installation (property fixture, no legacy extension registered)'
    Assert-True (!(Test-Path -LiteralPath $installDirectory)) 'Blocked install creates no target directory'
    $installCode=Run-Msi @('/i',$msiFile,'/qn','/norestart','/l*v',(Join-Path $repoRoot 'artifacts\msi-install.log'))
    $installed=$installCode -in @(0,3010)
    Assert-True $installed 'Per-user MSI installs successfully'
    $exe=Join-Path $installDirectory 'FileConverterNext.exe'
    Assert-True (Test-Path -LiteralPath $exe) 'Installed executable exists'
    $installedHandler=Registry-Value $handler
    Write-Host ('Installed handler: '+$installedHandler)
    Assert-True ($installedHandler -eq '{61699489-9EEA-43D6-BF5B-32DB32B7A301}') 'Default menu uses NativeAOT'
    foreach($mode in @('NativeCpp','NativeAot')){
        $configuration=Get-Content -Raw -LiteralPath $settingsFile | ConvertFrom-Json
        $configuration.Shell.Mode=$mode
        [IO.File]::WriteAllText($settingsFile,($configuration | ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
        Assert-True ((Run-Process $exe @('--apply-shell-mode')) -eq 0) ("Apply "+$mode+" succeeds")
        $chosen=if($mode -eq 'NativeAot'){$aotClass}else{$cppClass}
        $other=if($mode -eq 'NativeAot'){$cppClass}else{$aotClass}
        Assert-True ((Registry-Value ($chosen+'\InprocServer32')) -like ('*FileConverterNext.Shell.'+$mode+'.dll')) ($mode+' server path is correct')
        Assert-True ($null -eq (Registry-Value ($other+'\InprocServer32'))) ($mode+' leaves only one COM server registered')
    }
}
catch{
    $report.Add([pscustomobject]@{Check='Installer acceptance interrupted';Result='Failed';Detail=$_.Exception.Message})
    throw
}
finally{
    if($installed){
        $uninstallCode=Run-Msi @('/x',$msiFile,'/qn','/norestart','/l*v',(Join-Path $repoRoot 'artifacts\msi-uninstall.log'))
        $report.Add([pscustomobject]@{Check='MSI uninstall exit code';Result=$uninstallCode})
    }
    if($null -ne $originalSettings){[IO.File]::WriteAllBytes($settingsFile,$originalSettings)}
    elseif(Test-Path -LiteralPath $settingsFile){Remove-Item -LiteralPath $settingsFile}
    if($null -ne $originalMenu){[IO.File]::WriteAllBytes($menuFile,$originalMenu)}
    elseif(Test-Path -LiteralPath $menuFile){Remove-Item -LiteralPath $menuFile}
    if($legacyHash){Assert-True ((Get-FileHash -LiteralPath $legacyFile).Hash -eq $legacyHash) 'Legacy XML is unchanged'}
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts\installer-acceptance.json') -Encoding utf8
}
Assert-True ($null -eq (Registry-Value $handler)) 'Uninstall removes new menu handler'
Assert-True ($null -eq (Registry-Value ($aotClass+'\InprocServer32'))) 'Uninstall removes NativeAOT registration'
Assert-True ($null -eq (Registry-Value ($cppClass+'\InprocServer32'))) 'Uninstall removes C++ registration'
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts\installer-acceptance.json') -Encoding utf8
$report | Format-Table -AutoSize
