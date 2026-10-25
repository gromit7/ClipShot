param([Parameter(Mandatory=$true)][string]$Setup,[string]$Destination='D:/install/Codex Software')
$ErrorActionPreference='Stop'
$source=Get-Item -LiteralPath $Setup
if($source.Name -notmatch '^ClipShot-Setup-(\d+\.\d+\.\d+)\.exe$'){throw 'Нужен legacy-пакет с версией в имени и два соседних OS-пакета.'}
$version=$Matches[1]
$sources=@(foreach($flavor in 'Win10','Win11'){Get-Item -LiteralPath (Join-Path $source.DirectoryName "ClipShot-$flavor-Setup-$version.exe")})
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$root=[IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
if($sources | Where-Object {$_.DirectoryName -eq $root}){throw 'Источники не должны находиться внутри каталога выдачи.'}
$names=@($sources.Name)
# Сначала копируем и сверяем оба новых файла. Старые выпуски пока остаются на D.
$staged=@()
try{
    foreach($item in $sources){
        $temporary=Join-Path $root ($item.Name+'.new-'+[guid]::NewGuid().ToString('N'))
        $staged+=@{Source=$item;Path=$temporary;Final=(Join-Path $root $item.Name)}
        Copy-Item -LiteralPath $item.FullName -Destination $temporary
        if((Get-FileHash -LiteralPath $item.FullName).Hash -ne (Get-FileHash -LiteralPath $temporary).Hash){throw 'Не совпадает SHA-256 копии установщика.'}
    }
    $previous=@(Get-ChildItem -LiteralPath $root -File | Where-Object {$_.Name -match '^(ClipShot|ClipNest)(-Win10|-Win11)?-Setup-\d+\.\d+\.\d+\.exe$'})
    # Включая одноимённый файл: ни одна прежняя копия не удаляется и не заменяется
    # до подтверждения её архива. Другие программы в каталоге не затрагиваются.
    foreach($old in $previous){
        $path=[IO.Path]::GetFullPath($old.FullName)
        if([IO.Path]::GetDirectoryName($path) -ne $root){throw 'Неожиданный путь предыдущего установщика.'}
        $hash=(Get-FileHash -LiteralPath $path).Hash
        $archive=Join-Path $source.DirectoryName $old.Name
        if((Test-Path -LiteralPath $archive) -and (Get-FileHash -LiteralPath $archive).Hash -ne $hash){
            $archive=Join-Path $source.DirectoryName ($old.BaseName+'-sha256-'+$hash.ToLowerInvariant()+'.exe')
        }
        if(-not(Test-Path -LiteralPath $archive)){Copy-Item -LiteralPath $path -Destination $archive}
        if((Get-FileHash -LiteralPath $archive).Hash -ne $hash){throw 'Архив предыдущего установщика не совпадает с оригиналом.'}
    }
    foreach($item in $staged){Move-Item -LiteralPath $item.Path -Destination $item.Final -Force}
    foreach($item in $staged){
        if((Get-FileHash -LiteralPath $item.Source.FullName).Hash -ne (Get-FileHash -LiteralPath $item.Final).Hash){throw 'Не совпадает итоговый установщик.'}
    }
    foreach($old in $previous){if($old.Name -notin $names){Remove-Item -LiteralPath $old.FullName}}
    foreach($item in $staged){Write-Output ('Готов к проверке: '+$item.Final)}
}finally{
    foreach($item in $staged){if(Test-Path -LiteralPath $item.Path){Remove-Item -LiteralPath $item.Path}}
}
