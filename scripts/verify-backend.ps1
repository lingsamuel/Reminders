param([Parameter(Mandatory = $true)][string]$BackendPath)

# Safe on a shared desktop: every child is --headless, with redirected stdio.
$ErrorActionPreference = 'Stop'
$directory = Join-Path $env:TEMP ('Reminders-ipc-' + [Guid]::NewGuid().ToString('N'))
$script:sequence = 0
$process = $null
$second = $null

function Start-Backend([string]$DataDirectory) {
    $info = New-Object Diagnostics.ProcessStartInfo
    if ($BackendPath.EndsWith('.dll')) {
        $info.FileName = 'C:\Program Files\dotnet\dotnet.exe'
        $info.Arguments = '"' + $BackendPath + '" --headless --data-dir "' + $DataDirectory + '"'
    } else {
        $info.FileName = $BackendPath
        $info.Arguments = '--headless --data-dir "' + $DataDirectory + '"'
    }
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $child = New-Object Diagnostics.Process
    $child.StartInfo = $info
    [void]$child.Start()
    return $child
}

function Request([string]$Method, [object]$Payload = @{}) {
    $script:sequence++
    $id = $script:sequence
    $line = @{id=$id;method=$Method;payload=$Payload} | ConvertTo-Json -Depth 12 -Compress
    $process.StandardInput.WriteLine($line)
    $process.StandardInput.Flush()
    do {
        $read = $process.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(10000)) { throw "Backend timed out: $Method" }
        $text = $read.Result
        if ($null -eq $text) { throw "Backend stopped: $($process.StandardError.ReadToEnd())" }
        $response = $text | ConvertFrom-Json
    } while ($response.id -ne $id)
    if ($response.error) { throw $response.error }
    return $response.result
}

function Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAILED: $Description" }
    Write-Output "PASS: $Description"
}

try {
    $process = Start-Backend $directory
    $snapshot = Request 'snapshot'
    Check ($snapshot.ready -and $snapshot.tasks.Count -eq 0) 'headless startup returns empty snapshot'
    $taskId = [Guid]::NewGuid().ToString()
    $task = @{
        id=$taskId;title='IPC task';revision=1;order=0;enabled=$true;archived=$false;createdAt=[DateTimeOffset]::UtcNow.ToString('O')
        schedule=@{kind='Once';resetTime='05:00:00';weekday='Thursday';everyDays=2;anchorDate=(Get-Date).ToString('yyyy-MM-dd');dueDate=(Get-Date).AddDays(1).ToString('yyyy-MM-dd');dueTime=$null}
    }
    $snapshot = Request 'saveTask' @{task=$task}
    Check ($snapshot.tasks.Count -eq 1 -and $snapshot.tasks[0].task.id -eq $taskId) 'JSONL saves and returns task'
    $snapshot = Request 'complete' @{id=$taskId}
    Check ($snapshot.tasks[0].isCompleted -and $snapshot.events.Count -eq 1) 'completion and history share persisted state'
    $snapshot = Request 'undo' @{id=$taskId}
    Check (-not $snapshot.tasks[0].isCompleted -and $snapshot.events.Count -eq 2) 'undo retains audit event'
    $second = Start-Backend ($directory + '\')
    Check ($second.WaitForExit(5000) -and $second.ExitCode -ne 0) 'directory alias cannot bypass exclusive data lock'
    $process.StandardInput.Close()
    Check ($process.WaitForExit(5000) -and $process.ExitCode -eq 0) 'stdin EOF exits backend cleanly'
    $process.Dispose()
    $process = Start-Backend $directory
    $snapshot = Request 'snapshot'
    Check ($snapshot.tasks.Count -eq 1 -and $snapshot.events.Count -eq 2) 'restart restores file and releases previous lock'
    $process.StandardInput.Close()
    Check ($process.WaitForExit(5000)) 'restarted backend exits without windows'
    Write-Output "Headless process verification passed. Isolated data: $directory"
} finally {
    foreach ($child in @($process, $second)) {
        if ($child) {
            if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
            $child.Dispose()
        }
    }
}
