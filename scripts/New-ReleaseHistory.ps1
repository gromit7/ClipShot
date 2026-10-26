#Requires -Version 5.1
param([string]$OutputPath)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$projectPath=Join-Path $repo 'ClipShot/ClipShot.csproj'
$notesDirectory=Join-Path $repo 'docs'
if(-not $OutputPath){$OutputPath=Join-Path $repo 'ClipShot/Assets/ReleaseHistory.json'}

function ConvertTo-ReleaseVersion([string]$Value){
    $parsed=$null
    if($Value -cnotmatch '^\d+\.\d+\.\d+$' -or -not [version]::TryParse($Value,[ref]$parsed) -or $parsed.ToString() -cne $Value){throw 'Версия истории должна иметь канонический формат X.Y.Z.'}
    return $parsed
}
function ConvertTo-Change([string]$Text){
    $text=$Text -replace '\[([^\]]+)\]\([^)]+\)','$1' -replace '[*`_]',''
    $text=([regex]::Replace($text,'\s+',' ')).Trim()
    if(-not $text -or $text -notmatch '\p{IsCyrillic}'){return}
    # Проверка обновлений — функция приложения. Исключаем технические отчёты,
    # а не все предложения со словом «проверка».
    if($text -match '(?i)SHA[- ]?256|\bCI\b|GitHub Actions|\bxUnit\b|\bdigest\b|\bhash\b|[0-9a-f]{40,64}|автотест|модульн\w* тест|коммит|исходник|https?://'){return}
    $sentence=[regex]::Match($text,'^(.+?[.!?])(?:\s|$)')
    if($sentence.Success){$text=$sentence.Groups[1].Value}
    if($text.Length -gt 160){
        $prefix=$text.Substring(0,159);$space=$prefix.LastIndexOf(' ')
        if($space -gt 100){$prefix=$prefix.Substring(0,$space)}
        $text=$prefix.TrimEnd([char[]]' ,;:-')+'…'
    }
    return $text
}

# Старые RELEASE-файлы написаны для страницы выпуска: длинные абзацы смешивают
# функции, детали реализации и отчёты о проверках. Эти короткие редакторские
# формулировки основаны на них и не являются новыми заявлениями о совместимости.
# Явный раздел «Что изменилось» в будущем имеет приоритет над этой таблицей.
$editorial=@{
    '0.7.6'=@(
        'Исправлено автоматическое обновление с версии 0.7.4.'
        'Ручная проверка позволяет повторить ранее неудачное обновление.'
    )
    '0.7.5'=@(
        'Поиск разворачивается только по нажатию лупы.'
        'Выбор клипа или раздела больше не переводит фокус в поиск.'
        'Первый Esc очищает и сворачивает поиск; следующий скрывает панель.'
    )
    '0.7.4'=@(
        'Непустые разделы раскрываются при наведении мыши.'
        'Добавлена необязательная розовая тема с котиками.'
        'Обновлятор выбирает установщик для Windows 10 или Windows 11.'
    )
    '0.7.3'=@(
        'Обновление из трея запускается одним нажатием.'
        'Обновления докачиваются после обрыва связи и используют быстрое зеркало.'
        'Режим «Всегда поверх окон» сохраняется между запусками.'
    )
    '0.7.2'=@(
        'Компактная панель без верхней строки; шаблоны закреплены снизу.'
        'Разметка меняет исходный снимок в истории без создания дубликата.'
        'Снимки можно экспортировать в PNG и JPEG.'
    )
}
function Get-ReleaseChanges([string]$Version,[string]$Content){
    $lines=@($Content -split '\r?\n');$section=$false;$level=0;$selected=@()
    # Берём первый раздел с человеческим кратким описанием, если он существует.
    for($index=0;$index -lt $lines.Count;$index++){
        if($lines[$index] -match '^\s{0,3}(#{1,6})\s+Что изменилось(?:\s|:|$)'){
            $section=$true;$level=$Matches[1].Length
            for($next=$index+1;$next -lt $lines.Count;$next++){
                if($lines[$next] -match '^\s{0,3}(#{1,6})\s+' -and $Matches[1].Length -le $level){break}
                $selected+=$lines[$next]
            }
            break
        }
    }
    if(-not $section -and $editorial.ContainsKey($Version)){return $editorial[$Version]}
    if(-not $section){$selected=$lines}
    $changes=@();$fence=$false
    foreach($line in $selected){
        if($line -match '^\s*(```|~~~)'){$fence=-not $fence;continue}
        if($fence -or $line -notmatch '^\s{0,3}[-*+]\s+(.+)$'){continue}
        $change=ConvertTo-Change $Matches[1]
        if($change -and $change -cnotin $changes){$changes+=@($change)}
        if($changes.Count -eq 3){break}
    }
    if($changes.Count -eq 0){throw ('В заметках версии '+$Version+' нет кратких русских функциональных пунктов. Добавьте раздел «Что изменилось» с маркированным списком.')}
    return $changes
}

$project=[xml](Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8)
$current=ConvertTo-ReleaseVersion ([string]$project.Project.PropertyGroup.Version)
$notes=@(Get-ChildItem -LiteralPath $notesDirectory -Filter 'RELEASE-*.md' -File | ForEach-Object {
    if($_.Name -cmatch '^RELEASE-(\d+\.\d+\.\d+)\.md$'){
        $name=$Matches[1];$version=ConvertTo-ReleaseVersion $name
        if($version -le $current){[pscustomobject]@{Version=$version;Name=$name;Path=$_.FullName}}
    }
} | Sort-Object -Property Version -Descending | Select-Object -First 5)
if($notes.Count -eq 0){throw 'Нет заметок RELEASE-X.Y.Z.md для текущей или более ранней версии. История не создана.'}
$releases=@(foreach($note in $notes){
    $file=Get-Item -LiteralPath $note.Path
    if($file.Length -lt 1 -or $file.Length -gt 262144){throw ('Заметки версии '+$note.Name+' пусты или превышают 256 КиБ.')}
    $content=Get-Content -LiteralPath $note.Path -Raw -Encoding UTF8
    if([string]::IsNullOrWhiteSpace($content)){throw ('Заметки версии '+$note.Name+' пусты.')}
    $changes=@(Get-ReleaseChanges $note.Name $content)
    if($changes.Count -lt 1 -or $changes.Count -gt 3 -or @($changes|Where-Object {$_.Length -lt 1 -or $_.Length -gt 160}).Count){throw ('Некорректное краткое описание версии '+$note.Name+'.')}
    [ordered]@{version=$note.Name;changes=$changes}
})
$json=([ordered]@{schema=1;releases=$releases}|ConvertTo-Json -Depth 6)+[Environment]::NewLine
$encoding=New-Object Text.UTF8Encoding($false)
if($encoding.GetByteCount($json) -gt 32768){throw 'История превышает 32 КиБ.'}
$target=[IO.Path]::GetFullPath($OutputPath)
$parent=[IO.Path]::GetDirectoryName($target)
New-Item -ItemType Directory -Path $parent -Force|Out-Null
# До полной проверки входных данных старый готовый JSON не затрагивается.
[IO.File]::WriteAllText($target,$json,$encoding)
Write-Output ('История выпусков: '+$releases.Count+' версий, до '+$current+'.')
