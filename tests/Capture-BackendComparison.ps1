param(
    [Parameter(Mandatory=$true)][string]$Tag,
    [int[]]$Limits=@(30,60,90,999),
    [ValidateRange(2,4)][int]$Multiplier=2,
    [string]$Fixture='W:\SteamLibrary\steamapps\common\Derail Valley\Mods\DVLSFG.FsrFgWorldTest',
    [string]$PresentMon='W:\Application\MSI Afterburner\RTSS\RivaTuner Statistics Server\Plugins\Client\PresentMonDataProvider\PresentMon-1.10.0-x64.exe'
)
$ErrorActionPreference='Stop'
$run=Join-Path (Split-Path $PSScriptRoot -Parent) 'build/backend-comparison'
$game=Get-Process DerailValley
if(-not (Test-Path (Join-Path $Fixture 'live-ready.txt'))) {throw 'Live comparison fixture is not ready'}
if((Get-Item (Join-Path $Fixture 'live-ready.txt')).LastWriteTimeUtc -lt $game.StartTime.ToUniversalTime()) {throw 'Live readiness marker belongs to an older game process'}
foreach($limit in $Limits) {
    $command=[Guid]::NewGuid().ToString('N')+';'+$limit+';True;'+$Multiplier
    [IO.File]::WriteAllText((Join-Path $Fixture 'live-command.txt'),$command)
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 250
        $ack=Get-Content (Join-Path $Fixture 'live-ack.txt') -Raw -ErrorAction SilentlyContinue
    } while((-not $ack -or -not $ack.StartsWith($command)) -and [DateTime]::UtcNow -lt $deadline)
    if(-not $ack -or -not $ack.StartsWith($command)) {throw 'Game did not acknowledge the limit'}
    $ack | Set-Content (Join-Path $run "$Tag-$limit-ack.txt")
    Start-Sleep -Seconds 3
    & $PresentMon -process_id $game.Id -session_name ("DVScaling-"+$Tag+'-'+$limit) -output_file (Join-Path $run "$Tag-$limit.csv") -qpc_time_s -no_top -timed 8 -terminate_after_timed
    if($LASTEXITCODE) {throw 'PresentMon capture failed'}
    & "$PSScriptRoot/Read-RTSS.ps1" -ProcessId $game.Id | ConvertTo-Json | Set-Content (Join-Path $run "$Tag-$limit-rtss.json")
    Write-Output "Captured $Tag limit=${limit}: $ack"
}
