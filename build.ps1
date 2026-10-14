param(
    [string]$GameDirectory='W:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$VulkanSdk='C:\VulkanSDK\1.4.309.0',
    [string]$CMake,
    [switch]$SkipNative
)
$ErrorActionPreference='Stop'
$project=$PSScriptRoot
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
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managed=Join-Path $GameDirectory 'DerailValley_Data/Managed'
$output=Join-Path $project 'dist/dv-scaling'
New-Item -ItemType Directory $output -Force | Out-Null
Copy-Item "$project/package/*" $output -Force -Recurse
Copy-Item "$project/build/native/Release/DVLSFG.Native.dll" $output -Force
Copy-Item "$project/build/native/Release/DVLSFG.Bootstrap.dll" $output -Force
Copy-Item "$project/build/native/Release/DVLSFG.FSR.dll" $output -Force
Copy-Item "$project/vendor/fsr1/license.txt" "$output/FSR1-LICENSE.txt" -Force
Copy-Item "$project/vendor/lsfg-vk/LICENSE.md" "$output/LSFG-VK-LICENSE.md" -Force
Copy-Item "$project/vendor/lsfg-vk/thirdparty/dxbc/LICENSE.md" "$output/DXBC-LICENSE.md" -Force
Copy-Item "$project/vendor/lsfg-vk/thirdparty/volk/LICENSE.md" "$output/VOLK-LICENSE.md" -Force
Copy-Item "$project/UPSTREAM.md" $output -Force
Copy-Item "$project/VALIDATION.md" $output -Force
& $compiler /nologo /target:library /platform:x64 /optimize+ "/out:$output\DVLSFG.dll" "/reference:$managed\UnityModManager\UnityModManager.dll" "/reference:$managed\UnityModManager\0Harmony.dll" "/reference:$managed\UnityEngine.CoreModule.dll" "/reference:$managed\UnityEngine.IMGUIModule.dll" "/reference:$managed\UnityEngine.UIModule.dll" "/reference:$managed\UnityEngine.TextRenderingModule.dll" "/reference:$managed\UnityEngine.UI.dll" "/reference:$managed\Unity.TextMeshPro.dll" "/reference:$managed\DV.UI.dll" "/reference:$managed\DV.UIFramework.dll" "/reference:$managed\I2.Localization.dll" "$project\src\FsrUpscaling.cs" "$project\src\Main.cs" "$project\src\Settings.cs" "$project\src\GraphicsSettings.cs" "$project\src\AutoStartup.cs" "$project\src\BootstrapDeployment.cs"
if($LASTEXITCODE) { throw 'UMM compilation failed' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$output\DVLSFG.BootstrapWorker.exe" "$project\src\BootstrapWorker.cs" "$project\src\BootstrapDeployment.cs"
if($LASTEXITCODE) { throw 'Bootstrap worker compilation failed' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$output\DVLSFG.Launcher.exe" "$project\src\Launcher.cs" "$project\src\Settings.cs"
if($LASTEXITCODE) { throw 'Launcher compilation failed' }
$version=(Get-Content "$project/package/Info.json" -Raw | ConvertFrom-Json).Version
$zip=Join-Path $project "dist/dv-scaling-$version.zip"
Compress-Archive -Path $output -DestinationPath $zip -Force
Get-Item $zip | Select-Object FullName,Length
