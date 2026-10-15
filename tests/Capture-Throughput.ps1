param(
    [string[]]$Stages=@('off','on','frozen','on-repeat','off-repeat'),
    [string]$Fixture='W:/SteamLibrary/steamapps/common/Derail Valley/Mods/DVLSFG.FsrFgWorldTest',
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$run=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory $run -Force | Out-Null
$game=Get-Process DerailValley
$ready=Join-Path $Fixture 'throughput-ready.txt'
if(-not (Test-Path $ready) -or (Get-Item $ready).LastWriteTimeUtc -lt $game.StartTime.ToUniversalTime()) {throw 'Throughput probe not ready'}
$presentMon='W:/Application/MSI Afterburner/RTSS/RivaTuner Statistics Server/Plugins/Client/PresentMonDataProvider/PresentMon-1.10.0-x64.exe'
foreach($stage in $Stages) {
    $enabled=if($stage.StartsWith('off')) {'False'} else {'True'}
    $mode=if($stage.StartsWith('frozen')) {'freeze'} else {'normal'}
    $limit=if($stage.EndsWith('-60')) {60} else {999}
    $command="$stage;$enabled;$mode;$limit"
    [IO.File]::WriteAllText((Join-Path $Fixture 'throughput-command.txt'),$command)
    $deadline=[DateTime]::UtcNow.AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 200
        $ack=Get-Content (Join-Path $Fixture 'throughput-ack.txt') -Raw -ErrorAction SilentlyContinue
    } while($ack -ne $command -and [DateTime]::UtcNow -lt $deadline)
    if($ack -ne $command) {throw 'Throughput command not acknowledged'}
    Start-Sleep -Seconds 3
    & $presentMon -process_id $game.Id -session_name ('DVScalingThroughput-'+$stage) -output_file (Join-Path $run "$stage-present.csv") -qpc_time_s -no_top -timed 8 -terminate_after_timed
    if($LASTEXITCODE) {throw 'PresentMon capture failed'}
    & "$PSScriptRoot/Read-RTSS.ps1" -ProcessId $game.Id | ConvertTo-Json | Set-Content (Join-Path $run "$stage-rtss.json")
    Copy-Item 'W:/SteamLibrary/steamapps/common/Derail Valley/Mods/dv-scaling/metrics.ini' (Join-Path $run "$stage-metrics.ini")
    Write-Output "Captured $command"
}
[IO.File]::WriteAllText((Join-Path $Fixture 'throughput-command.txt'),'flush;True;normal;999')
Start-Sleep -Seconds 1
