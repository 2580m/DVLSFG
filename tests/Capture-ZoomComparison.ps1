param(
    [string[]]$Stages=@('normal','hold','cycle','suppress','no-fg','native','fg-no-sr','repeat-cycle','repeat-suppress','repeat-normal'),
    [string]$Fixture='W:\SteamLibrary\steamapps\common\Derail Valley\Mods\DVLSFG.FsrFgWorldTest',
    [string]$OutputDirectory,
    [string]$PresentMon='W:\Application\MSI Afterburner\RTSS\RivaTuner Statistics Server\Plugins\Client\PresentMonDataProvider\PresentMon-1.10.0-x64.exe'
)
$ErrorActionPreference='Stop'
$run=if($OutputDirectory) {[IO.Path]::GetFullPath($OutputDirectory)} else {Join-Path (Split-Path $PSScriptRoot -Parent) 'build/zoom-regression'}
New-Item -ItemType Directory $run -Force | Out-Null
$game=Get-Process DerailValley
$ready=Join-Path $Fixture 'zoom-ready.txt'
if(-not (Test-Path $ready) -or (Get-Item $ready).LastWriteTimeUtc -lt $game.StartTime.ToUniversalTime()) {throw 'Zoom fixture is not ready in this game process'}
foreach($stage in $Stages) {
    $mode='cycle'; $enabled='True'; $sr=2; $suppress='False'; $limit=0
    switch($stage) {
        'normal' {$mode='steady'}
        'hold' {$mode='hold'}
        'suppress' {$suppress='True'}
        'no-fg' {$enabled='False'}
        'native' {$enabled='False'; $sr=0}
        'fg-no-sr' {$sr=0}
        'repeat-suppress' {$suppress='True'}
        'repeat-normal' {$mode='steady'}
        'cycle' {}
        'repeat-cycle' {}
        'cycle-30' {$limit=30}
        'cut' {$mode='cut'}
        default {throw "Unknown stage $stage"}
    }
    $command="$stage;$mode;$enabled;$sr;$suppress;$limit"
    [IO.File]::WriteAllText((Join-Path $Fixture 'zoom-command.txt'),$command)
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 200
        $ack=Get-Content (Join-Path $Fixture 'zoom-ack.txt') -Raw -ErrorAction SilentlyContinue
    } while($ack -ne $command -and [DateTime]::UtcNow -lt $deadline)
    if($ack -ne $command) {throw 'Zoom fixture did not acknowledge stage'}
    Start-Sleep -Seconds 3
    & $PresentMon -process_id $game.Id -session_name ('DVScalingZoom-'+$stage) -output_file (Join-Path $run "$stage-present.csv") -qpc_time_s -no_top -timed 8 -terminate_after_timed
    if($LASTEXITCODE) {throw 'PresentMon capture failed'}
    & "$PSScriptRoot/Read-RTSS.ps1" -ProcessId $game.Id | ConvertTo-Json | Set-Content (Join-Path $run "$stage-rtss.json")
    Write-Output "Captured $command"
}
# A new command flushes the preceding per-frame log without stopping the probe.
[IO.File]::WriteAllText((Join-Path $Fixture 'zoom-command.txt'),'flush;steady;True;2;False')
Start-Sleep -Seconds 1
