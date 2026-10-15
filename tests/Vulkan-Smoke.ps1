param(
    [Parameter(Mandatory=$true)][string]$LosslessDll,
    [string]$Sdk = 'C:\VulkanSDK\1.4.309.0',
    [switch]$Validation,
    [int]$Multiplier = 2,
    [switch]$Performance,
    [switch]$Disabled,
    [switch]$WithOverlays,
    [switch]$Toggle,
    [switch]$Live,
    [switch]$LiveSettings,
    [switch]$RecoverySettings,
    [switch]$Recreate,
    [switch]$Metadata,
    [switch]$DeferredFence,
    [switch]$PresentWait,
    [switch]$Capture,
    [switch]$Secondary,
    [int]$Frames=12,
    [double]$InputRate=0,
    [switch]$Visible,
    [switch]$ApiDump,
    [switch]$DisplayProof,
    [switch]$ResizePresent
)
$smokeEnvironmentNames=@('DVLSFG_CONFIG','DVLSFG_TEST_HOST','DVLSFG_AUTO_LAYER','VK_ADD_IMPLICIT_LAYER_PATH','VK_ADD_LAYER_PATH','VK_INSTANCE_LAYERS','VK_LOADER_DEBUG','VK_LOADER_LAYERS_DISABLE','VK_LAYER_SETTINGS_PATH','VK_APIDUMP_DETAILED','VK_APIDUMP_FLUSH','VK_APIDUMP_TIMESTAMP','VK_APIDUMP_LOG_FILENAME')
$smokePreviousEnvironment=@{}
foreach($smokeName in $smokeEnvironmentNames){$smokePreviousEnvironment[$smokeName]=[Environment]::GetEnvironmentVariable($smokeName,'Process')}
try {
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot
$run=Join-Path $project ('build/smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $run | Out-Null
Copy-Item "$project/build/native/Release/DVLSFG.Native.dll", "$project/package/VkLayer_DVLSFG.json" $run
Copy-Item "$project/build/native/Release/DVLSFG.FSRFG.dll" $run
$enabledValue=if($Disabled){'0'}else{'1'}
$performanceValue=if($Performance){'1'}else{'0'}
[IO.File]::WriteAllText("$run/config.ini", "Enabled=$enabledValue`nDllPath=$LosslessDll`nMultiplier=$Multiplier`nPerformance=$performanceValue`nFlowScale=1`n", [Text.UTF8Encoding]::new($false))
$env:DVLSFG_CONFIG="$run/config.ini"
$env:DVLSFG_TEST_HOST='1'
$env:VK_ADD_LAYER_PATH=$run
$env:VK_INSTANCE_LAYERS='VK_LAYER_DVLSFG_frame_generation'
$env:VK_LOADER_DEBUG='layer'
if($WithOverlays) { Remove-Item Env:VK_LOADER_LAYERS_DISABLE -ErrorAction SilentlyContinue }
else { $env:VK_LOADER_LAYERS_DISABLE='~implicit~' }
if($Validation) {
    # Validation must sit below our layer to observe injected Vulkan commands.
    $env:VK_INSTANCE_LAYERS='VK_LAYER_DVLSFG_frame_generation;VK_LAYER_KHRONOS_validation'
    $env:VK_ADD_LAYER_PATH="$run;$Sdk/Bin"
    [IO.File]::WriteAllText("$run/vk_layer_settings.txt","khronos_validation.debug_action = VK_DBG_LAYER_ACTION_LOG_MSG`nkhronos_validation.report_flags = error,warn`nkhronos_validation.log_filename = $run/validation.log`nkhronos_validation.validate_sync = true`nkhronos_validation.thread_safety = true`n")
    $env:VK_LAYER_SETTINGS_PATH=$run
}
if($ApiDump) {
    $env:VK_INSTANCE_LAYERS+=';VK_LAYER_LUNARG_api_dump'
    $env:VK_ADD_LAYER_PATH+=';C:/VulkanSDK/1.4.309.0/Bin'
    $env:VK_APIDUMP_DETAILED='false'
    $env:VK_APIDUMP_FLUSH='true'
    $env:VK_APIDUMP_TIMESTAMP='true'
    $env:VK_APIDUMP_LOG_FILENAME="$run/api-dump.log"
}
$hostArgs=if($Toggle){'--toggle'}elseif($Recreate){'--recreate'}elseif($Metadata){'--metadata'}elseif($Capture){'--capture'}elseif($Secondary){'--secondary'}else{'--normal'}
if($ResizePresent){$hostArgs='--resize-present'}
if($DeferredFence){$hostArgs+=' --deferred-fence'}
if($PresentWait){$hostArgs+=' --present-wait'}
if($Live) { $hostArgs='--live'; if($ResizePresent) { $hostArgs+=' --resize-present' } }
if($LiveSettings) { $hostArgs='--live-settings'; if($ResizePresent) { $hostArgs+=' --resize-present' } }
if($RecoverySettings) { $hostArgs='--recovery-settings' }
$hostArgs+=" --frames=$Frames --rate=$InputRate"
if($Visible) { $hostArgs+=' --visible' }
if($DisplayProof) { $hostArgs+=' --display-proof' }
$process=Start-Process "$project/build/native/Release/DVLSFG.VulkanHost.exe" -ArgumentList $hostArgs -WorkingDirectory $run -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run/stdout.log" -RedirectStandardError "$run/stderr.log"
if(-not $process.WaitForExit(30000)) { $process.Kill(); throw "Vulkan smoke test timed out: $run" }
$process.Refresh()
Get-Content "$run/native.log" -ErrorAction SilentlyContinue
Get-Content "$run/stdout.log" -Tail 8
Write-Output "Exit=$($process.ExitCode); artifacts=$run"
if($process.ExitCode -ne 0) { throw 'Vulkan smoke failed' }
$expectedReal=if($Toggle){$Frames-2}else{$Frames}
$expected=$expectedReal*($Multiplier-1)
if($Live) { $expectedReal=11; $expected=20 }
if($LiveSettings) { $expectedReal=10; $expected=15 }
if(-not $Disabled -and -not $ResizePresent -and -not ((Get-Content "$run/stdout.log" -Raw) -match "real=$expectedReal generated=$expected\b")) { throw 'Unexpected generated frame count' }
if($Disabled -and -not ((Get-Content "$run/stdout.log" -Raw) -match 'real=0 generated=0\b')) { throw 'Disabled path generated frames' }
if($Live -or $LiveSettings -or $RecoverySettings) {
    $source=Get-ChildItem "$run/captures" -Filter '*-input.bmp' | Select-Object -First 1
    $synth=Get-ChildItem "$run/captures" -Filter '*-generated.bmp' | Select-Object -First 1
    $values=@()
    foreach($file in @($source,$synth)) {
        if(-not $file) { throw 'Live-switch framebuffer capture absent' }
        $data=[IO.File]::ReadAllBytes($file.FullName)
        $offset=[BitConverter]::ToInt32($data,10); $width=[BitConverter]::ToInt32($data,18); $height=[Math]::Abs([BitConverter]::ToInt32($data,22))
        $center=$offset+(([int]($height/2)*$width+[int]($width/2))*4)
        $values+=[int]$data[$center+2]
    }
    $current=if($LiveSettings -or $RecoverySettings){234}else{212}; $previous=if($LiveSettings -or $RecoverySettings){212}else{191}
    if($values[0] -ne $current -or $values[1] -le $previous -or $values[1] -ge $current) { throw "Live-switch interpolation failed: $values" }
    Write-Output "Live pixel proof: previous=$previous, generated=$($values[1]), current=$($values[0])."
}
if($Validation -and (Test-Path "$run/validation.log") -and (Get-Item "$run/validation.log").Length -ne 0) { throw "Vulkan validation messages: $run/validation.log" }
if($ApiDump) {
    $dumpPath=if(Test-Path "$run/api-dump.log.txt"){"$run/api-dump.log.txt"}elseif(Test-Path "$run/api-dump.log"){"$run/api-dump.log"}else{"$run/stderr.log"}
    $actualPresents=@(Select-String -Path $dumpPath -Pattern 'vkQueuePresentKHR\(').Count
    $expectedPresents=if($Disabled){$Frames}else{$expectedReal*$Multiplier+($Frames-$expectedReal)}
    if($Live -or $LiveSettings) { $expectedPresents=$Frames+$expected }
    if($actualPresents -ne $expectedPresents){throw "Independent Vulkan trace: expected $expectedPresents presents, got $actualPresents ($dumpPath)"}
    Write-Output "Independent Vulkan trace passed: $actualPresents vkQueuePresentKHR calls."
}
if($Capture) {
    $source=Get-ChildItem "$run/captures" -Filter '*-input.bmp' | Select-Object -First 1
    $synth=Get-ChildItem "$run/captures" -Filter '*-generated.bmp' | Select-Object -First 1
    if(-not $source -or -not $synth) { throw 'Framebuffer captures absent' }
    $values=@()
    foreach($file in @($source,$synth)) {
        $data=[IO.File]::ReadAllBytes($file.FullName)
        $offset=[BitConverter]::ToInt32($data,10); $width=[BitConverter]::ToInt32($data,18); $height=[Math]::Abs([BitConverter]::ToInt32($data,22))
        $center=$offset+(([int]($height/2)*$width+[int]($width/2))*4)
        $values+=[int]$data[$center+2]
    }
    if($values[0] -ne 64 -or $values[1] -le 43 -or $values[1] -ge 64) { throw "Output was not an intermediate frame: input=$($values[0]), generated=$($values[1])" }
    Write-Output "Pixel proof passed: current red=$($values[0]), generated red=$($values[1]), previous red=43."
}
} finally {
    foreach($smokeName in $smokeEnvironmentNames){[Environment]::SetEnvironmentVariable($smokeName,$smokePreviousEnvironment[$smokeName],'Process')}
}
