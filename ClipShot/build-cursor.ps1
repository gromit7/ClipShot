$ErrorActionPreference='Stop'
$stream=[IO.MemoryStream]::new(); $w=[IO.BinaryWriter]::new($stream)
# Classic 32-bit BGRA cursor with alpha and an AND mask; hotspot is the exact centre.
$w.Write([uint16]0);$w.Write([uint16]2);$w.Write([uint16]1)
$w.Write([byte]32);$w.Write([byte]32);$w.Write([byte]0);$w.Write([byte]0)
$w.Write([uint16]16);$w.Write([uint16]16);$w.Write([uint32](40+4096+128));$w.Write([uint32]22)
$w.Write([uint32]40);$w.Write([int32]32);$w.Write([int32]64);$w.Write([uint16]1);$w.Write([uint16]32)
$w.Write([uint32]0);$w.Write([uint32]4096);for($i=0;$i -lt 4;$i++){$w.Write([int32]0)}
for($y=31;$y -ge 0;$y--){for($x=0;$x -lt 32;$x++){
 $black=(($x -ge 14 -and $x -le 18 -and $y -ge 1 -and $y -le 31) -or ($y -ge 14 -and $y -le 18 -and $x -ge 1 -and $x -le 31))
 $white=(($x -ge 16 -and $x -le 16 -and $y -ge 2 -and $y -le 30) -or ($y -ge 16 -and $y -le 16 -and $x -ge 2 -and $x -le 30))
 $color=if($white){[byte]255}else{[byte]0};$alpha=if($black){[byte]255}else{[byte]0}
 $w.Write($color);$w.Write($color);$w.Write($color);$w.Write($alpha)
}}
for($y=31;$y -ge 0;$y--){for($b=0;$b -lt 4;$b++){
 $mask=0;for($bit=0;$bit -lt 8;$bit++){$x=$b*8+$bit;$opaque=(($x -ge 14 -and $x -le 18 -and $y -ge 1) -or ($y -ge 14 -and $y -le 18 -and $x -ge 1));if(-not $opaque){$mask=$mask -bor (128 -shr $bit)}};$w.Write([byte]$mask)
}}
$w.Flush();[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'Assets/Selection.cur'),$stream.ToArray());$w.Dispose()
