param(
    [ValidateSet('Synthetic','World','Ui','Pacing','Fsr3','Fsr3World','SceneUi','FsrFg','FsrFgWorld')][string]$Kind,
    [string]$GameDirectory='W:\SteamLibrary\steamapps\common\Derail Valley'
)
$ErrorActionPreference='Stop'
if(Get-Process DerailValley -ErrorAction SilentlyContinue) {throw 'Exit the game before installing a test fixture.'}
$project=Split-Path $PSScriptRoot -Parent
$identity=@{Synthetic='FsrTest';World='FsrWorldTest';Ui='GraphicsTest';Pacing='PacingTest';Fsr3='Fsr3Test';Fsr3World='Fsr3WorldTest';SceneUi='SceneUiTest';FsrFg='FsrFgTest';FsrFgWorld='FsrFgWorldTest'}[$Kind]
$source=@{Synthetic='FsrSmoke';World='FsrWorldSmoke';Ui='GraphicsUiSmoke';Pacing='PacingSmoke';Fsr3='Fsr3Smoke';Fsr3World='Fsr3WorldSmoke';SceneUi='SceneUiSmoke';FsrFg='FsrFrameGenerationSmoke';FsrFgWorld='FsrFrameGenerationWorldSmoke'}[$Kind]
$fixture=Join-Path $GameDirectory ('Mods/DVLSFG.'+$identity)
New-Item -ItemType Directory $fixture -Force | Out-Null
@{Id='DVLSFG.'+$identity;DisplayName='Temporary DVLSFG '+$Kind+' test';Version='1.0.0';ManagerVersion='0.27.3';Requirements=@('DVLSFG');AssemblyName=$identity+'.dll';EntryMethod='DVLSFGTests.'+$source+'.Load'} | ConvertTo-Json | Set-Content (Join-Path $fixture 'Info.json')
$managed=Join-Path $GameDirectory 'DerailValley_Data/Managed'
$arguments=@('/nologo','/target:library','/platform:x64',('/out:'+(Join-Path $fixture ($identity+'.dll'))))
foreach($ref in @('UnityModManager/UnityModManager.dll','UnityModManager/0Harmony.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','Unity.TextMeshPro.dll','DV.UI.dll','DV.UIFramework.dll','DV.Localization.dll','I2.Localization.dll')) {
    $arguments+=('/reference:'+(Join-Path $managed $ref).Replace('/','\'))
}
$arguments+=('/reference:'+(Join-Path $GameDirectory 'Mods/dv-scaling/DVLSFG.dll').Replace('/','\'))
$arguments+=(Join-Path $PSScriptRoot ($source+'.cs'))
if($Kind -eq 'SceneUi') {$arguments+=(Join-Path $PSScriptRoot 'Fsr3WorldSmoke.cs')}
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' @arguments
if($LASTEXITCODE) {throw 'Fixture compilation failed'}
Write-Output $fixture
