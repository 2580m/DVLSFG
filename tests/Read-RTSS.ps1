param([int]$ProcessId=0)
$ErrorActionPreference='Stop'
# Read-only fields from RTSS SDK RTSSSharedMemory.h (v2 header and stable app prefix).
$taskMap=[IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting('RTSSSharedMemoryV2',[IO.MemoryMappedFiles.MemoryMappedFileRights]::Read)
try {
    $taskView=$taskMap.CreateViewAccessor(0,0,[IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
    try {
        if($taskView.ReadUInt32(0) -ne 0x52545353){throw 'RTSS mapping is not initialized'}
        $taskVersion=$taskView.ReadUInt32(4)
        if(($taskVersion -shr 16) -ne 2){throw 'Unsupported RTSS mapping version'}
        $taskEntrySize=$taskView.ReadUInt32(8); $taskOffset=$taskView.ReadUInt32(12); $taskCount=$taskView.ReadUInt32(16)
        if($taskEntrySize -lt 284 -or $taskCount -gt 4096){throw 'Invalid RTSS layout'}
        for($taskIndex=0;$taskIndex -lt $taskCount;$taskIndex++) {
            $taskEntry=[long]$taskOffset+[long]$taskIndex*$taskEntrySize
            $taskId=$taskView.ReadUInt32($taskEntry)
            if(-not $taskId -or ($ProcessId -and $taskId -ne $ProcessId)){continue}
            $taskBytes=New-Object byte[] 260
            $taskView.ReadArray[byte]($taskEntry+4,$taskBytes,0,260) | Out-Null
            $taskName=[Text.Encoding]::Default.GetString($taskBytes).Split([char]0)[0]
            $taskBegin=$taskView.ReadUInt32($taskEntry+268); $taskEnd=$taskView.ReadUInt32($taskEntry+272); $taskFrames=$taskView.ReadUInt32($taskEntry+276)
            $taskFrameUs=$taskView.ReadUInt32($taskEntry+280)
            [pscustomobject]@{ProcessId=$taskId;Name=$taskName;Flags=$taskView.ReadUInt32($taskEntry+264);FPS=if($taskEnd -gt $taskBegin){1000.0*$taskFrames/($taskEnd-$taskBegin)}else{0};FrameTimeMs=$taskFrameUs/1000.0}
        }
    } finally {$taskView.Dispose()}
} finally {$taskMap.Dispose()}
