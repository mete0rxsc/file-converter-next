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
$output=Join-Path $repoRoot 'artifacts\publish-next'
$shellOutput=Join-Path $output 'shell\3.0.5'
# Build into a fresh payload so renamed assemblies and old files cannot ship together.
$publishRoot=[IO.Path]::GetFullPath($output)
if($publishRoot -ne [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\publish-next')) -or !$publishRoot.StartsWith([IO.Path]::GetFullPath($repoRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Publish output is outside the intended workspace directory.'}
if(Test-Path -LiteralPath $publishRoot){Remove-Item -LiteralPath $publishRoot -Recurse -Force}
New-Item -ItemType Directory -Path $output,"$output\worker",$shellOutput,'artifacts\native' -Force | Out-Null
dotnet build 'File Converter Next.sln' -c Release --nologo
Check-Exit 'solution build'
dotnet publish 'File Converter Next\File Converter Next.csproj' -c Release -r win-x64 --self-contained true -o $output --nologo
Check-Exit 'desktop publish'
dotnet publish 'src\FileConverterNext.Worker\FileConverterNext.Worker.csproj' -c Release -r win-x64 --self-contained true -o "$output\worker" --nologo
Check-Exit 'worker publish'
dotnet publish 'src\FileConverterNext.Shell.NativeAot\FileConverterNext.Shell.NativeAot.csproj' -c Release -r win-x64 -o $shellOutput --nologo
Check-Exit 'AOT publish'
cl.exe /nologo /std:c++20 /EHsc /W4 /O2 /MT /LD 'src\FileConverterNext.Shell.NativeCpp\Shell.cpp' /Fo'artifacts\native\Shell.obj' /link /DEF:'src\FileConverterNext.Shell.NativeCpp\Shell.def' /OUT:"$shellOutput\FileConverterNext.Shell.NativeCpp.dll" /IMPLIB:'artifacts\native\Shell.lib' ole32.lib shell32.lib user32.lib gdi32.lib advapi32.lib uuid.lib
Check-Exit 'C++ shell'
cl.exe /nologo /std:c++20 /EHsc /W4 /O2 /MT 'tests\ShellHost\ShellHost.cpp' /Fo'artifacts\native\ShellHost.obj' /link /OUT:'artifacts\native\ShellHost.exe' ole32.lib shell32.lib user32.lib uuid.lib
Check-Exit 'native test host'
& "$PSScriptRoot\Test-Shell.ps1" -PreviousAot $PreviousAot
& "$PSScriptRoot\Fetch-Dependencies.ps1" -Destination "$output\tools"
Copy-Item -LiteralPath 'README.md','CHANGELOG.md','LICENSE.md','docs\THIRD-PARTY-NOTICES.md','docs\CONFIGURATION.md','docs\ACCEPTANCE.md','docs\RELEASING.md' -Destination $output
Copy-Item -LiteralPath 'installer\License.rtf' -Destination $output
Copy-Item -LiteralPath 'File Converter Next\Assets\preset.bmp','File Converter Next\Assets\folder.bmp' -Destination $shellOutput
if(!$SkipInstaller){
    dotnet build 'installer\FileConverterNext.Installer.wixproj' -c Release "-p:PublishDir=$output" --nologo
    Check-Exit 'MSI build'
}
$releaseDirectory=Join-Path $repoRoot 'artifacts\release'
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Compress-Archive -Path "$output\*" -DestinationPath (Join-Path $releaseDirectory 'FileConverterNext-3.0.5-win-x64.zip') -Force
if(!$SkipInstaller){Copy-Item -LiteralPath 'installer\bin\x64\Release\FileConverterNext-3.0.5-win-x64.msi' -Destination $releaseDirectory -Force}
$releaseAssets=@((Join-Path $releaseDirectory 'FileConverterNext-3.0.5-win-x64.zip'))
if(!$SkipInstaller){$releaseAssets+=(Join-Path $releaseDirectory 'FileConverterNext-3.0.5-win-x64.msi')}
$checksums=foreach($asset in $releaseAssets){((Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+[IO.Path]::GetFileName($asset)}
[IO.File]::WriteAllLines((Join-Path $releaseDirectory 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath 'CHANGELOG.md','docs\RELEASING.md' -Destination $releaseDirectory -Force
Write-Host "Published: $output"
Write-Host "Release assets: $releaseDirectory"
Write-Host 'Update source: %LOCALAPPDATA%\FileConverterNext\settings.json -> Updates.Repository'
