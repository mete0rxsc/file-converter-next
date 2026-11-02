param([string]$PreviousAot)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$hostExe=Join-Path $repoRoot 'artifacts\native\ShellHost.exe'
foreach($mode in @('aot','cpp')){
    $name=if($mode -eq 'aot'){'NativeAot'}else{'NativeCpp'}
    $dll=Join-Path $repoRoot "artifacts\publish-next\shell\3.0.5\FileConverterNext.Shell.$name.dll"
    if($PreviousAot){ & $hostExe $dll $mode $PreviousAot }
    else { & $hostExe $dll $mode }
    if($LASTEXITCODE -ne 0){throw "Shell $name host validation failed."}
}
