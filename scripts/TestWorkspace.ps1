# Общий жизненный цикл временных данных. Исторические папки здесь не чистим.
if(-not ('ClipShot.Testing.TestWorkspace' -as [type])){
    $workspaceSource=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TestWorkspace.cs'))
    # CodeDOM в Windows PowerShell 5.1 не понимает nullable-директиву даже
    # внутри отключённого #if. Она влияет только на анализ компилятором.
    if($PSVersionTable.PSVersion.Major -lt 7){$workspaceSource=$workspaceSource.Replace('#nullable disable','')}
    Add-Type -TypeDefinition $workspaceSource
}
function New-ClipShotTestScope {
    [pscustomobject]@{Id=[guid]::NewGuid().ToString('N');Roots=[Collections.Generic.List[object]]::new();Processes=[Collections.Generic.List[object]]::new()}
}
function Register-ClipShotTestProcess($Scope,[Diagnostics.Process]$Process) {
    $Scope.Processes.Add(@{Id=$Process.Id;Started=$Process.StartTime.ToUniversalTime().Ticks})
}
function New-ClipShotTestRoot($Scope) {
    $diagnostics=Join-Path (Split-Path $PSScriptRoot) ('.artifacts/test-diagnostics/'+$Scope.Id)
    $workspace=[ClipShot.Testing.TestWorkspace]::new($diagnostics)
    $Scope.Roots.Add($workspace)
    return $workspace.Root
}
function Complete-ClipShotTestScope($Scope) {
    $failures=[Collections.Generic.List[string]]::new()
    foreach($workspace in $Scope.Roots){
        try{
            # Root может быть передан дочернему приложению только в окружении.
            # Проверяем также сохранённые PID+время запуска, не только CommandLine.
            foreach($record in $Scope.Processes){
                $process=Get-Process -Id $record.Id -ErrorAction SilentlyContinue
                if($process -and $process.StartTime.ToUniversalTime().Ticks -eq $record.Started){throw 'Owned test process has not exited; scope retained.'}
            }
            $prefix=$workspace.Root+'\'
            $busy=@(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
                $_.ProcessId -ne $PID -and (($_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) -or
                ($_.CommandLine -and $_.CommandLine.IndexOf($workspace.Root,[StringComparison]::OrdinalIgnoreCase) -ge 0))
            })
            if($busy.Count){throw 'Test process still uses its root; retained without stopping it.'}
            $workspace.Dispose()
        }catch{$workspace.ReleaseLease();$failures.Add($workspace.Root);Write-Warning ('Temporary test data retained: '+$workspace.Root+'; '+$_.Exception.Message)}
    }
    if($failures.Count){throw ('Could not safely clean '+$failures.Count+' test roots; see warnings.')}
}
function Enter-ClipShotTestGate {
    # WinUI/legacy updater используют общие системные имена. Второй прогон
    # отказывается ДО запуска окон, а не отправляет команды первому.
    $gate=[Threading.Mutex]::new($false,'Local\ClipShot.TestSuite.v1')
    try{$held=$gate.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true}
    if(-not $held){$gate.Dispose();throw 'Another ClipShot integration suite is running.'}
    return $gate
}
