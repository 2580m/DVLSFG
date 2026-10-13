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
