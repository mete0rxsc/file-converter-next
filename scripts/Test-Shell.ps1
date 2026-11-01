$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$hostExe=Join-Path $repoRoot 'artifacts\native\ShellHost.exe'
foreach($mode in @('aot','cpp')){
    $name=if($mode -eq 'aot'){'NativeAot'}else{'NativeCpp'}
    & $hostExe (Join-Path $repoRoot "artifacts\publish\shell\FileConverter.Shell.$name.dll") $mode
    if($LASTEXITCODE -ne 0){throw "Shell $name host validation failed."}
}
