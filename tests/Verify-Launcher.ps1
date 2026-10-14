param([Parameter(Mandatory=$true)][string]$LosslessDll)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot
$fixture=Join-Path $project ('build/launcher-' + [Guid]::NewGuid().ToString('N'))
$mod=Join-Path $fixture 'Mods/dv-scaling'
New-Item -ItemType Directory $mod -Force | Out-Null
Copy-Item "$project/dist/dv-scaling/*" $mod -Force
New-Item -ItemType Directory "$fixture/Mods/DVVulkan/dxvk" -Force | Out-Null
foreach($name in @('d3d11.dll','dxgi.dll')) {
    Copy-Item "$project/../DVVulkan/vendor/dxvk-3.1.1/x64/$name" $fixture
    Copy-Item "$project/../DVVulkan/vendor/dxvk-3.1.1/x64/$name" "$fixture/Mods/DVVulkan/dxvk"
}
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 "/out:$fixture\DerailValley.exe" "$PSScriptRoot\FakeGame.cs"
if($LASTEXITCODE) { throw 'Fixture compile failed' }
$config="$mod/config.ini"
$text="Enabled=1`nDllPath=$LosslessDll`nMultiplier=2`nPerformance=0`nFlowScale=1`n"
[IO.File]::WriteAllText($config,$text,[Text.UTF8Encoding]::new($false))
$env:VK_INSTANCE_LAYERS='TestPreservedLayer'
$env:VK_ADD_LAYER_PATH='C:\Test preserved path'
& "$mod/DVLSFG.Launcher.exe"
if($LASTEXITCODE) { throw 'Launcher rejected valid fixture' }
$capture="$fixture/launch-capture.txt"
for($i=0;$i -lt 20 -and -not (Test-Path $capture);$i++) { Start-Sleep -Milliseconds 100 }
$actual=Get-Content $capture -Raw
if($actual -notmatch 'TestPreservedLayer;VK_LAYER_DVLSFG_frame_generation' -or $actual -notmatch 'C:\\Test preserved path;' -or $actual -notmatch '-force-d3d11') { throw 'Child process environment was not preserved/configured' }
Remove-Item -LiteralPath $capture
[IO.File]::WriteAllText($config,$text.Replace('Enabled=1','Enabled=0'),[Text.UTF8Encoding]::new($false))
& "$mod/DVLSFG.Launcher.exe"
if($LASTEXITCODE -eq 0 -or (Test-Path $capture)) { throw 'Disabled launch was not rejected' }
& "$mod/DVLSFG.Launcher.exe" --without-lsfg
if($LASTEXITCODE) { throw 'Recovery launch failed' }
for($i=0;$i -lt 20 -and -not (Test-Path $capture);$i++) { Start-Sleep -Milliseconds 100 }
$actual=Get-Content $capture -Raw
if($actual -match 'VK_LAYER_DVLSFG_frame_generation' -or $actual -notmatch 'DVLSFG_CONFIG=\r?\n' -or $actual -notmatch 'DVLSFG_NO_FRAMEGEN=1') { throw 'Recovery left LSFG enabled' }
Remove-Item -LiteralPath $capture
[IO.File]::WriteAllText($config,"Enabled=1`nMultiplier=broken`n")
& "$mod/DVLSFG.Launcher.exe" --without-lsfg
if($LASTEXITCODE) { throw 'Damaged config prevented recovery launch' }
for($i=0;$i -lt 20 -and -not (Test-Path $capture);$i++) { Start-Sleep -Milliseconds 100 }
if(-not (Test-Path $capture)) { throw 'Damaged-config recovery did not start game' }
Remove-Item -LiteralPath $capture
[IO.File]::WriteAllText($config,$text,[Text.UTF8Encoding]::new($false))
$mismatch=Join-Path $fixture 'dxgi.dll'
[IO.File]::WriteAllText($mismatch,'test mismatch')
& "$mod/DVLSFG.Launcher.exe"
if($LASTEXITCODE -eq 0 -or (Test-Path $capture)) { throw 'DXVK mismatch accepted' }
Write-Output "Launcher fixture checks passed: child env, disabled guard, recovery, DLL conflict. $fixture"
exit 0 # The last native invocation deliberately failed to verify the DXVK guard.
