param([Parameter(Mandatory=$true)][string]$PublishDir,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$sourceRoot=(Resolve-Path -LiteralPath $PublishDir).Path
$hash=[Security.Cryptography.SHA256]::Create()
function Id([string]$prefix,[string]$path){$prefix+([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($path.ToLowerInvariant()))).Replace('-','').Substring(0,24))}
function Escape([string]$text){[Security.SecurityElement]::Escape($text)}
$xml=[Collections.Generic.List[string]]::new()
$references=[Collections.Generic.List[string]]::new()
$xml.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment><DirectoryRef Id="INSTALLFOLDER">')
function EmitDirectory([string]$path,[string]$relative,[string]$directoryId){
    $componentId=Id 'Payload_' $relative
    $guidBytes=$hash.ComputeHash([Text.Encoding]::UTF8.GetBytes('FileConverterNext.Modern.Payload/'+$relative.ToLowerInvariant()))[0..15]
    $componentGuid=New-Object Guid (,[byte[]]$guidBytes)
    $xml.Add('<Component Id="'+$componentId+'" Guid="'+$componentGuid.ToString('B')+'" Bitness="always64">')
    $xml.Add('<RegistryValue Root="HKCU" Key="Software\FileConverterNext.Modern\Payload" Name="'+$componentId+'" Type="integer" Value="1" KeyPath="yes"/>')
    foreach($file in Get-ChildItem -LiteralPath $path -File | Where-Object {$_.Extension -notin @('.pdb','.lib','.exp')}){
        $fileRelative=if($relative){Join-Path $relative $file.Name}else{$file.Name}
        $fileId=Id 'File_' $fileRelative
        $language=if($file.Extension -eq '.ttf'){' DefaultLanguage="0"'}else{''}
        $xml.Add('<File Id="'+$fileId+'" Source="'+(Escape $file.FullName)+'" Name="'+(Escape $file.Name)+'"'+$language+'/>')
    }
    $xml.Add('<RemoveFolder Id="'+(Id 'Remove_' $relative)+'" Directory="'+$directoryId+'" On="uninstall"/>')
    if(!$relative){$xml.Add('<RemoveFolder Id="RemoveProgramsIfEmpty" Directory="ProgramsFolder" On="uninstall"/>')}
    $xml.Add('</Component>')
    $references.Add('<ComponentRef Id="'+$componentId+'"/>')
    foreach($folder in Get-ChildItem -LiteralPath $path -Directory){
        $child=if($relative){Join-Path $relative $folder.Name}else{$folder.Name}
        $childId=Id 'Dir_' $child
        $xml.Add('<Directory Id="'+$childId+'" Name="'+(Escape $folder.Name)+'">')
        EmitDirectory $folder.FullName $child $childId
        $xml.Add('</Directory>')
    }
}
EmitDirectory $sourceRoot '' 'INSTALLFOLDER'
$xml.Add('</DirectoryRef><ComponentGroup Id="Payload">')
$xml.AddRange($references)
$xml.Add('</ComponentGroup></Fragment></Wix>')
New-Item -ItemType Directory -Path (Split-Path -Parent $Output) -Force | Out-Null
[IO.File]::WriteAllText($Output,($xml -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$hash.Dispose()
