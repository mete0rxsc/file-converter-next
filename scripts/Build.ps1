param([switch]$SkipInstaller,[string]$PreviousAot)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot
function Check-Exit([string]$step) { if($LASTEXITCODE -ne 0){throw "$step failed ($LASTEXITCODE)"} }
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if(!(Test-Path -LiteralPath $vswhere)){throw 'Install Visual Studio 2022 Build Tools, C++ x64 tools, and Windows 11 SDK (26100 or newer).'}
$vsRoot=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vsRoot){throw 'Missing Visual Studio C++ x64 Build Tools.'}
$sdkRoot='C:\Program Files (x86)\Windows Kits\10'
if(!(Test-Path -LiteralPath "$sdkRoot\Lib")){throw 'Missing Windows SDK libraries.'}
$vcvars=Join-Path $vsRoot 'VC\Auxiliary\Build\vcvars64.bat'
$environmentLines=& $env:ComSpec /d /s /c ('"'+$vcvars+'" >nul && set')
Check-Exit 'VC environment'
foreach($line in $environmentLines){if($line -match '^([^=]+)=(.*)$'){[Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process')}}
$output=Join-Path $repoRoot 'artifacts\publish'
$shellOutput=Join-Path $output 'shell\3.0.2'
# Publish only this build's shell payload; installed DLLs are never touched here.
$shellRoot=[IO.Path]::GetFullPath((Join-Path $output 'shell'))
if(!$shellRoot.StartsWith([IO.Path]::GetFullPath($repoRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Shell output is outside the repository.'}
if(Test-Path -LiteralPath $shellRoot){Remove-Item -LiteralPath $shellRoot -Recurse -Force}
New-Item -ItemType Directory -Path $output,"$output\worker",$shellOutput,'artifacts\native' -Force | Out-Null
dotnet build 'File Conventer.sln' -c Release --nologo
Check-Exit 'solution build'
dotnet publish 'File Conventer\File Conventer.csproj' -c Release -r win-x64 --self-contained true -o $output --nologo
Check-Exit 'desktop publish'
dotnet publish 'src\FileConverter.Worker\FileConverter.Worker.csproj' -c Release -r win-x64 --self-contained true -o "$output\worker" --nologo
Check-Exit 'worker publish'
dotnet publish 'src\FileConverter.Shell.NativeAot\FileConverter.Shell.NativeAot.csproj' -c Release -r win-x64 -o $shellOutput --nologo
Check-Exit 'AOT publish'
cl.exe /nologo /std:c++20 /EHsc /W4 /O2 /MT /LD 'src\FileConverter.Shell.NativeCpp\Shell.cpp' /Fo'artifacts\native\Shell.obj' /link /DEF:'src\FileConverter.Shell.NativeCpp\Shell.def' /OUT:"$shellOutput\FileConverter.Shell.NativeCpp.dll" /IMPLIB:'artifacts\native\Shell.lib' ole32.lib shell32.lib user32.lib gdi32.lib advapi32.lib uuid.lib
Check-Exit 'C++ shell'
cl.exe /nologo /std:c++20 /EHsc /W4 /O2 /MT 'tests\ShellHost\ShellHost.cpp' /Fo'artifacts\native\ShellHost.obj' /link /OUT:'artifacts\native\ShellHost.exe' ole32.lib shell32.lib user32.lib uuid.lib
Check-Exit 'native test host'
& "$PSScriptRoot\Test-Shell.ps1" -PreviousAot $PreviousAot
& "$PSScriptRoot\Fetch-Dependencies.ps1" -Destination "$output\tools"
Copy-Item -LiteralPath 'LICENSE.md','docs\THIRD-PARTY-NOTICES.md','docs\CONFIGURATION.md','docs\ACCEPTANCE.md' -Destination $output
Copy-Item -LiteralPath 'File Conventer\Assets\preset.bmp','File Conventer\Assets\folder.bmp' -Destination $shellOutput
if(!$SkipInstaller){
    dotnet build 'installer\FileConverter.Installer.wixproj' -c Release "-p:PublishDir=$output" --nologo
    Check-Exit 'MSI build'
}
Compress-Archive -Path "$output\*" -DestinationPath 'artifacts\FileConverter-3.0.2-win-x64.zip' -Force
Write-Host "Published: $output"
Write-Host 'Update source: %LOCALAPPDATA%\FileConverter\settings.json -> Updates.Repository'
