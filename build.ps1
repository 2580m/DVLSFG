param(
    [string]$GameDirectory='W:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$VulkanSdk='C:\VulkanSDK\1.4.309.0',
    [string]$CMake,
    [switch]$SkipNative
)
$ErrorActionPreference='Stop'
$project=$PSScriptRoot
& "$project/tests/Validate-Translations.ps1" -GameDirectory $GameDirectory
if(-not $CMake) {
    $candidate=Get-Command cmake -ErrorAction SilentlyContinue
    if($candidate) { $CMake=$candidate.Source }
    else {
        $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        $vsPath=& $vswhere -latest -products '*' -property installationPath
        $CMake=Join-Path $vsPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
    }
}
if(-not (Test-Path "$project/vendor/lsfg-vk/thirdparty/dxbc/include")) { throw 'Run fetch-upstream.ps1 first.' }
if(-not $SkipNative) {
    & "$project/port.ps1"
    & $CMake -S "$project/native" -B "$project/build/native" -G 'Visual Studio 17 2022' -A x64 "-DVulkan_ROOT=$VulkanSdk"
    if($LASTEXITCODE) { throw 'Native configuration failed' }
    & $CMake --build "$project/build/native" --config Release --parallel 6
    if($LASTEXITCODE) { throw 'Native compilation failed' }
}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsPath=& $vswhere -latest -products '*' -property installationPath
$compiler=Join-Path $vsPath 'MSBuild/Current/Bin/Roslyn/csc.exe'
& "$project/Build-Fsr3.ps1" -SkipShaders
if(-not $SkipNative -or -not (Test-Path "$project/build/fsr3/bundle/dv-scaling-fsr3")) { & "$project/Build-Fsr3.ps1" }
$fsr3Sources=(Get-ChildItem "$project/build/fsr3/runtime" -Filter '*.cs').FullName
$managed=Join-Path $GameDirectory 'DerailValley_Data/Managed'
$output=Join-Path $project 'dist/dv-scaling'
New-Item -ItemType Directory $output -Force | Out-Null
Copy-Item "$project/package/*" $output -Force -Recurse
Copy-Item "$project/build/fsr3/bundle/dv-scaling-fsr3" $output -Force
Copy-Item "$project/vendor/FSR3Unity/LICENSE.txt" "$output/FSR3UNITY-LICENSE.txt" -Force
Copy-Item "$project/vendor/FSR3Unity/Packages/fidelityfx.fsr/LICENSE.md" "$output/FSR3-LICENSE.md" -Force
Copy-Item "$project/vendor/FidelityFX-SDK/LICENSE.txt" "$output/FIDELITYFX-LICENSE.txt" -Force
Copy-Item "$project/build/native/Release/DVLSFG.Native.dll" $output -Force
Copy-Item "$project/build/native/Release/DVLSFG.FSRFG.dll" $output -Force
Copy-Item "$project/build/native/Release/DVLSFG.Bootstrap.dll" $output -Force
Copy-Item "$project/build/native/Release/DVLSFG.FSR.dll" $output -Force
Copy-Item "$project/vendor/fsr1/license.txt" "$output/FSR1-LICENSE.txt" -Force
Copy-Item "$project/vendor/lsfg-vk/LICENSE.md" "$output/LSFG-VK-LICENSE.md" -Force
Copy-Item "$project/vendor/lsfg-vk/thirdparty/dxbc/LICENSE.md" "$output/DXBC-LICENSE.md" -Force
Copy-Item "$project/vendor/lsfg-vk/thirdparty/volk/LICENSE.md" "$output/VOLK-LICENSE.md" -Force
Copy-Item "$project/UPSTREAM.md" $output -Force
Copy-Item "$project/VALIDATION.md" $output -Force
& $compiler /nologo /target:library /platform:x64 /optimize+ /langversion:8 "/out:$output\DVLSFG.dll" "/reference:$managed\UnityModManager\UnityModManager.dll" "/reference:$managed\UnityModManager\0Harmony.dll" "/reference:$managed\UnityEngine.CoreModule.dll" "/reference:$managed\UnityEngine.IMGUIModule.dll" "/reference:$managed\UnityEngine.UIModule.dll" "/reference:$managed\UnityEngine.ScreenCaptureModule.dll" "/reference:$managed\UnityEngine.TextRenderingModule.dll" "/reference:$managed\UnityEngine.UI.dll" "/reference:$managed\Unity.TextMeshPro.dll" "/reference:$managed\DV.UI.dll" "/reference:$managed\DV.UIFramework.dll" "/reference:$managed\I2.Localization.dll" "/reference:$managed\Unity.Postprocessing.Runtime.dll" "/reference:$managed\UnityEngine.AssetBundleModule.dll" "/reference:$managed\DV.Localization.dll" "/reference:$GameDirectory\Mods\DVLangHelper\DVLangHelper.Runtime.dll" "/reference:$GameDirectory\Mods\DVLangHelper\DVLangHelper.Data.dll" "$project\src\Localization.cs" "$project\src\FsrUpscaling.cs" "$project\src\Fsr3Upscaling.cs" "$project\src\Fsr3Compatibility.cs" "$project\src\SceneUi.cs" "$project\src\TemporalCapture.cs" @fsr3Sources "$project\src\Main.cs" "$project\src\Settings.cs" "$project\src\GraphicsSettings.cs" "$project\src\AutoStartup.cs" "$project\src\BootstrapDeployment.cs"
if($LASTEXITCODE) { throw 'UMM compilation failed' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$output\DVLSFG.BootstrapWorker.exe" "$project\src\BootstrapWorker.cs" "$project\src\BootstrapDeployment.cs"
if($LASTEXITCODE) { throw 'Bootstrap worker compilation failed' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$output\DVLSFG.Launcher.exe" "$project\src\Launcher.cs" "$project\src\Settings.cs"
if($LASTEXITCODE) { throw 'Launcher compilation failed' }
$version=(Get-Content "$project/package/Info.json" -Raw | ConvertFrom-Json).Version
$zip=Join-Path $project "dist/dv-scaling-$version.zip"
Compress-Archive -Path $output -DestinationPath $zip -Force
Get-Item $zip | Select-Object FullName,Length
