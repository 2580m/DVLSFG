param([string]$UnityEditor=(Join-Path $PSScriptRoot 'build/UnityEditor/Editor/Unity.exe'),[switch]$SkipShaders)
$ErrorActionPreference='Stop'
$upstream=Join-Path $PSScriptRoot 'vendor/FSR3Unity'
$revision='71879e71aa72465a79bdf009d7d71f9f7ab83066'
if(-not (Test-Path "$upstream/Packages/fidelityfx.fsr")) {throw 'Fetch ndepoel/FSR3Unity at the pinned revision documented in UPSTREAM.md first.'}
$actual=git -C $upstream rev-parse HEAD
if($actual -ne $revision) {throw 'Unexpected FSR3Unity revision'}
$runtime=Join-Path $PSScriptRoot 'build/fsr3/runtime'
New-Item -ItemType Directory $runtime -Force | Out-Null
Copy-Item "$upstream/Packages/fidelityfx.fsr/Runtime/FSR3/*.cs" $runtime -Force
Copy-Item "$upstream/Packages/fidelityfx.fsr/Runtime/Common/ResourceView.cs" $runtime -Force
foreach($file in Get-ChildItem $runtime -Filter '*.cs') {
    $data=[IO.File]::ReadAllText($file.FullName)
    $data=$data.Replace('commandBuffer.SetComputeBufferData(computeBuffer, data);','computeBuffer.SetData(data);')
    $data=[regex]::Replace($data,'SetComputeConstantBufferParam\(ComputeShader,\s*','SetGlobalConstantBuffer(')
    $data=[regex]::Replace($data,'SetGlobalConstantBuffer\((Fsr3ShaderIDs\.\w+),\s*(\w+),','SetGlobalConstantBuffer($2, $1,')
    $data=$data.Replace('cmd.BeginSample(_sampler);','cmd.BeginSample("FSR3");').Replace('cmd.EndSample(_sampler);','cmd.EndSample("FSR3");')
    $data=[regex]::Replace($data,'resource.Release\(\);\s*resource = null;','resource.Release(); UnityEngine.Object.Destroy(resource); resource = null;')
    $data=[regex]::Replace($data,'(?m)^\s*if\s*\([^\r\n]+\) ComputeShader.EnableKeyword\([^\r\n]+\);','')
    # Unity 2019 has no compute shader keyword permutations. Bake the matching
    # SDR/reversed-depth/low-resolution-MV variants and select sharpening kernels.
    $data=$data.Replace('commandBuffer.EnableShaderKeyword(SharpeningKeyword);','KernelIndex = ComputeShader.FindKernel("CS_SHARPEN");').Replace('commandBuffer.DisableShaderKeyword(SharpeningKeyword);','KernelIndex = ComputeShader.FindKernel("CS");')
    if($file.Name -eq 'Fsr3UpscalerResources.cs') {
        $data=$data.Replace('class Fsr3UpscalerResources','partial class Fsr3UpscalerResources')
        $data=[regex]::Replace($data,'(?s)public static void (?:Create|Destroy)AliasableResources\(.*?\n        \}','')
    }
    if($file.Name -eq 'Fsr3UpscalerPass.cs') {
        $data=$data.Replace('BeginSample(commandBuffer);','Fsr3Compatibility.BindScratchUavs(commandBuffer, ComputeShader, Resources.Aliases); BeginSample(commandBuffer);')
        $data=[regex]::Replace($data,'public virtual void Dispose\(\)\s*\{\s*\}','public virtual void Dispose() { Fsr3Compatibility.Forget(ComputeShader); }')
    }
    if($file.Name -eq 'Fsr3UpscalerContext.cs') {
        $data=$data.Replace('Fsr3UpscalerResources.CreateAliasableResources','_resources.CreateAliasableResources').Replace('Fsr3UpscalerResources.DestroyAliasableResources','_resources.DestroyAliasableResources').Replace('_resources.Destroy();','_resources.DisposeAliases(); _resources.Destroy();')
    }
    foreach($alias in @('UavIntermediate','UavShadingChange','UavNewLocks','UavFarthestDepthMip1','UavDilatedReactiveMasks')) {
        if($file.Name -eq 'Fsr3UpscalerPass.cs') {$data=$data.Replace(', Fsr3ShaderIDs.'+$alias+');',', Resources.Aliases[Fsr3ShaderIDs.'+$alias+']);')}
        if($file.Name -eq 'Fsr3UpscalerContext.cs') {$data=$data.Replace('SetRenderTarget(Fsr3ShaderIDs.'+$alias+')','SetRenderTarget(_resources.Aliases[Fsr3ShaderIDs.'+$alias+'])')}
    }
    [IO.File]::WriteAllText($file.FullName,$data,[Text.UTF8Encoding]::new($false))
}
if($SkipShaders) {return}
if(-not (Test-Path $UnityEditor)) {throw 'Unity 2019.4.40f1 editor required to build the FSR3 shader bundle'}
$project=Join-Path $PSScriptRoot 'build/fsr3/unity'
New-Item -ItemType Directory "$project/Assets/Editor","$project/ProjectSettings","$project/Packages" -Force | Out-Null
[IO.File]::WriteAllText("$project/ProjectSettings/ProjectVersion.txt","m_EditorVersion: 2019.4.40f1`n")
[IO.File]::WriteAllText("$project/Packages/manifest.json",'{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}')
Copy-Item "$upstream/Packages/fidelityfx.fsr/Shaders" "$project/Assets" -Recurse -Force
foreach($shader in Get-ChildItem "$project/Assets/Shaders" -Filter '*fsr3upscaler*.compute') {
    $data=[IO.File]::ReadAllText($shader.FullName)
    $data=[regex]::Replace($data,'(?m)^#pragma multi_compile[^\r\n]*','')
    $defines="#define FFX_FSR3UPSCALER_OPTION_LOW_RESOLUTION_MOTION_VECTORS 1`n#define FFX_FSR3UPSCALER_OPTION_INVERTED_DEPTH 1`n"
    $data=$data.Replace('#include "ffx_fsr_unity_common.cginc"',$defines+'#include "ffx_fsr_unity_common.cginc"')
    if($shader.Name -eq 'ffx_fsr3upscaler_accumulate_pass.compute') {
        $data=$data.Replace('#pragma kernel CS',"#pragma kernel CS FFX_FSR3UPSCALER_OPTION_APPLY_SHARPENING=0`n#pragma kernel CS_SHARPEN CS=CS_SHARPEN FFX_FSR3UPSCALER_OPTION_APPLY_SHARPENING=1")
    }
    [IO.File]::WriteAllText($shader.FullName,$data,[Text.UTF8Encoding]::new($false))
}
Copy-Item "$PSScriptRoot/native/Fsr3BundleBuilder.cs" "$project/Assets/Editor" -Force
New-Item -ItemType Directory "$PSScriptRoot/build/fsr3/bundle" -Force | Out-Null
$log="$PSScriptRoot/build/fsr3/editor.log"
$arguments=@('-batchmode','-nographics','-quit','-projectPath',('"'+$project+'"'),'-executeMethod','Fsr3BundleBuilder.Build','-logFile',('"'+$log+'"'))
$process=Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if($process.ExitCode -ne 0 -or -not (Test-Path "$PSScriptRoot/build/fsr3/bundle/dv-scaling-fsr3")) {throw "FSR3 shader build failed; see $log"}
