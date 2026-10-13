param([string]$Output,[string]$Loader="$env:windir\System32\vulkan-1.dll")
$ErrorActionPreference='Stop'
New-Item -ItemType Directory $Output -Force | Out-Null
$data=[IO.File]::ReadAllBytes($Loader)
$nt=[BitConverter]::ToInt32($data,60); $optional=$nt+24
if([BitConverter]::ToUInt16($data,$optional) -ne 0x20b){throw 'Bootstrap requires an x64 Vulkan loader'}
$sections=$optional+[BitConverter]::ToUInt16($data,$nt+20)
$count=[BitConverter]::ToUInt16($data,$nt+6)
function Offset([uint32]$rva){
    for($i=0;$i -lt $count;$i++){
        $s=$sections+$i*40; $start=[BitConverter]::ToUInt32($data,$s+12)
        $size=[Math]::Max([BitConverter]::ToUInt32($data,$s+8),[BitConverter]::ToUInt32($data,$s+16))
        if($rva -ge $start -and $rva -lt $start+$size){return [int]($rva-$start+[BitConverter]::ToUInt32($data,$s+20))}
    }
    throw 'Invalid export RVA'
}
$exports=Offset ([BitConverter]::ToUInt32($data,$optional+112))
$base=[BitConverter]::ToUInt32($data,$exports+16)
$names=Offset ([BitConverter]::ToUInt32($data,$exports+32))
$ordinals=Offset ([BitConverter]::ToUInt32($data,$exports+36))
$n=[BitConverter]::ToUInt32($data,$exports+24)
$header=[Text.StringBuilder]::new(); $asm=[Text.StringBuilder]::new(); $def=[Text.StringBuilder]::new()
$null=$header.AppendLine('#pragma once').AppendLine('static const char* const bootstrapExports[] = {')
$null=$asm.AppendLine('EXTERN DVLSFG_Resolve:PROC').AppendLine('.code')
$null=$def.AppendLine('LIBRARY "vulkan-1"').AppendLine('EXPORTS')
for($i=0;$i -lt $n;$i++){
    $at=Offset ([BitConverter]::ToUInt32($data,$names+$i*4)); $end=$at
    while($data[$end] -ne 0){$end++}
    $name=[Text.Encoding]::ASCII.GetString($data,$at,$end-$at)
    if($name -notmatch '^vk[A-Za-z0-9_]+$'){throw "Unexpected loader export $name"}
    $ordinal=$base+[BitConverter]::ToUInt16($data,$ordinals+$i*2)
    $null=$header.AppendLine('    "'+$name+'",')
    $null=$def.AppendLine("    $name @$ordinal")
    # Preserve Windows x64 integer/SSE arguments and stack arguments; tail-call
    # the original loader. Resolution runs outside DllMain and the loader lock.
    $null=$asm.AppendLine("$name PROC FRAME").AppendLine('    sub rsp, 136').AppendLine('    .allocstack 136').AppendLine('    .endprolog')
    $null=$asm.AppendLine('    mov [rsp+32], rcx').AppendLine('    mov [rsp+40], rdx').AppendLine('    mov [rsp+48], r8').AppendLine('    mov [rsp+56], r9')
    $null=$asm.AppendLine('    movdqu [rsp+64], xmm0').AppendLine('    movdqu [rsp+80], xmm1').AppendLine('    movdqu [rsp+96], xmm2').AppendLine('    movdqu [rsp+112], xmm3')
    $null=$asm.AppendLine("    mov ecx, $i").AppendLine('    call DVLSFG_Resolve')
    $null=$asm.AppendLine('    mov rcx, [rsp+32]').AppendLine('    mov rdx, [rsp+40]').AppendLine('    mov r8, [rsp+48]').AppendLine('    mov r9, [rsp+56]')
    $null=$asm.AppendLine('    movdqu xmm0, [rsp+64]').AppendLine('    movdqu xmm1, [rsp+80]').AppendLine('    movdqu xmm2, [rsp+96]').AppendLine('    movdqu xmm3, [rsp+112]')
    $null=$asm.AppendLine('    add rsp, 136').AppendLine('    jmp rax').AppendLine("$name ENDP")
}
$null=$header.AppendLine('};'); $null=$asm.AppendLine('END')
[IO.File]::WriteAllText("$Output\bootstrap_exports.hpp",$header.ToString())
[IO.File]::WriteAllText("$Output\bootstrap.asm",$asm.ToString())
[IO.File]::WriteAllText("$Output\bootstrap.def",$def.ToString())
Write-Output "Bootstrap forwards $n system Vulkan exports; no system DLL is packaged."
