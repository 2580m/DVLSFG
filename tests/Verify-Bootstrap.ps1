param([Parameter(Mandatory=$true)][string]$LosslessDll)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot
$run=Join-Path $project ('build/bootstrap-' + [Guid]::NewGuid().ToString('N'))
$mod=Join-Path $run 'Mods/dv-scaling'
New-Item -ItemType Directory $mod -Force | Out-Null
Copy-Item "$project/dist/dv-scaling/*" $mod -Force
Copy-Item "$project/build/native/Release/DVLSFG.VulkanHost.exe" "$run/DerailValley.exe"
Copy-Item "$project/build/native/Release/DVLSFG.VulkanHost.exe" $run
New-Item -ItemType Directory "$run/Mods/DVVulkan/dxvk" -Force | Out-Null
foreach($name in @('d3d11.dll','dxgi.dll')) {
    Copy-Item "$project/../DVVulkan/vendor/dxvk-3.1.1/x64/$name" $run
    Copy-Item "$project/../DVVulkan/vendor/dxvk-3.1.1/x64/$name" "$run/Mods/DVVulkan/dxvk"
}
$params=Join-Path $run 'DerailValley_Data/Managed/UnityModManager/Params.xml'
New-Item -ItemType Directory (Split-Path $params) -Force | Out-Null
$config="Enabled=1`nDllPath=$LosslessDll`nMultiplier=2`nPerformance=0`nFlowScale=1`n"
[IO.File]::WriteAllText("$mod/config.ini",$config,[Text.UTF8Encoding]::new($false))
Remove-Item Env:DVLSFG_CONFIG,Env:DVLSFG_TEST_HOST,Env:DVLSFG_BOOTSTRAP_TEST -ErrorAction SilentlyContinue
$env:DVLSFG_TEST_HOST='1'
$env:DVLSFG_BOOTSTRAP_TEST='1'
$env:VK_LOADER_DEBUG='layer'
$env:VK_INSTANCE_LAYERS='VK_LAYER_KHRONOS_validation'
$env:VK_ADD_LAYER_PATH="$project/build/validation-1.4.363/Bin"
$env:VK_LOADER_LAYERS_DISABLE='~implicit~'
$env:VK_LAYER_SETTINGS_PATH=$run
[IO.File]::WriteAllText("$run/vk_layer_settings.txt","khronos_validation.debug_action = VK_DBG_LAYER_ACTION_LOG_MSG`nkhronos_validation.report_flags = error,warn`nkhronos_validation.log_filename = $run/validation.log`nkhronos_validation.validate_sync = true`nkhronos_validation.thread_safety = true`n")
Copy-Item "$mod/DVLSFG.Bootstrap.dll" "$run/vulkan-1.dll"
foreach($case in @('Enabled','Disabled','UMMDisabled','DependencyDisabled','MissingMod','DXVKConflict','Recovery')) {
    Remove-Item Env:DVLSFG_NO_FRAMEGEN -ErrorAction SilentlyContinue
    [IO.File]::WriteAllText("$mod/config.ini",$config)
    [IO.File]::WriteAllText($params,'<Param><ModParams><Mod Id="DVLSFG" Enabled="true"/><Mod Id="DVVulkan" Enabled="true"/></ModParams></Param>')
    Copy-Item "$project/package/Info.json" $mod -Force
    Copy-Item "$run/Mods/DVVulkan/dxvk/d3d11.dll" $run -Force
    if($case -eq 'Disabled'){[IO.File]::WriteAllText("$mod/config.ini",$config.Replace('Enabled=1','Enabled=0'))}
    if($case -eq 'UMMDisabled'){[IO.File]::WriteAllText($params,"<Param><ModParams><Mod Enabled='false' Id='DVLSFG'/></ModParams></Param>")}
    if($case -eq 'DependencyDisabled'){[IO.File]::WriteAllText($params,'<Param><ModParams><Mod Id="DVVulkan" Enabled="false"/></ModParams></Param>')}
    if($case -eq 'MissingMod'){Remove-Item -LiteralPath "$mod/Info.json"}
    if($case -eq 'DXVKConflict'){[IO.File]::WriteAllText("$run/d3d11.dll",'Foreign payload')}
    if($case -eq 'Recovery'){$env:DVLSFG_NO_FRAMEGEN='1'}
    Remove-Item -LiteralPath "$run/validation.log" -ErrorAction SilentlyContinue
    $mode=if($case -eq 'Enabled'){'--capture'}else{'--normal'}
    $process=Start-Process "$run/DVLSFG.VulkanHost.exe" -ArgumentList $mode -WorkingDirectory $run -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run/$case.stdout.log" -RedirectStandardError "$run/$case.stderr.log"
    if(-not $process.WaitForExit(30000)){$process.Kill();throw "Bootstrap $case timed out"}
    $process.Refresh()
    $stdout=Get-Content "$run/$case.stdout.log" -Raw
    if($process.ExitCode -ne 0){Get-Content "$run/$case.stderr.log" -Tail 20;throw "Bootstrap $case failed"}
    if($case -eq 'Enabled' -and $stdout -notmatch 'real=12 generated=12'){throw 'Normal launch did not generate frames'}
    if($case -eq 'Enabled') {
        $input=Get-ChildItem "$mod/captures" -Filter '*-input.bmp' | Select-Object -First 1
        $generated=Get-ChildItem "$mod/captures" -Filter '*-generated.bmp' | Select-Object -First 1
        $red=@()
        foreach($file in @($input,$generated)) {
            if(-not $file){throw 'Automatic-startup framebuffer capture missing'}
            $bytes=[IO.File]::ReadAllBytes($file.FullName); $offset=[BitConverter]::ToInt32($bytes,10); $width=[BitConverter]::ToInt32($bytes,18); $height=[Math]::Abs([BitConverter]::ToInt32($bytes,22))
            $center=$offset+(([int]($height/2)*$width+[int]($width/2))*4); $red+=[int]$bytes[$center+2]
        }
        if($red[0] -ne 64 -or $red[1] -le 43 -or $red[1] -ge 64){throw 'Automatic-startup generated output was not interpolated'}
        Write-Output "Automatic-startup pixel proof: previous=43, current=$($red[0]), generated=$($red[1])"
    }
    if($case -ne 'Enabled' -and $stdout -match 'Native counters:'){throw "$case unexpectedly loaded LSFG layer"}
    if((Test-Path "$run/validation.log") -and (Get-Item "$run/validation.log").Length -gt 0){throw "Bootstrap validation issues: $case"}
    Copy-Item "$mod/bootstrap.log" "$run/$case.bootstrap.log"
    Write-Output "PASS automatic bootstrap: $case"
}
Write-Output "Bootstrap fixture artifacts: $run"
