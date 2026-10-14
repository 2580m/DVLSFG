$ErrorActionPreference='Stop'
$dest=Join-Path $PSScriptRoot 'vendor/lsfg-vk'
if(-not (Test-Path $dest)) {
    git clone --depth 1 --branch v1.0.0 https://git.lsfg-vk.dev/lsfg-vk-archive.git $dest
    if($LASTEXITCODE) { throw 'Clone failed' }
}
$revision=git -C $dest rev-parse HEAD
if($revision -ne '7113d7d02da9fc9df5cb3b03230d1f7de86f7056') { throw "Unexpected upstream revision: $revision" }
git -C $dest submodule update --init --depth 1 thirdparty/dxbc thirdparty/volk
if($LASTEXITCODE) { throw 'Dependency fetch failed' }
$fsr3=Join-Path $PSScriptRoot 'vendor/FSR3Unity'
$fsr3Revision='71879e71aa72465a79bdf009d7d71f9f7ab83066'
if(-not (Test-Path $fsr3)) {
    git clone --no-checkout --depth 1 https://github.com/ndepoel/FSR3Unity.git $fsr3
    if($LASTEXITCODE) {throw 'FSR3 clone failed'}
    git -C $fsr3 fetch --depth 1 origin $fsr3Revision
    if($LASTEXITCODE) {throw 'FSR3 revision fetch failed'}
    git -C $fsr3 checkout --detach $fsr3Revision
    if($LASTEXITCODE) {throw 'FSR3 checkout failed'}
}
if((git -C $fsr3 rev-parse HEAD) -ne $fsr3Revision) {throw 'Unexpected FSR3Unity revision'}
