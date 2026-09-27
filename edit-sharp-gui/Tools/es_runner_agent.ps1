# Runs in your own interactive session so windowed/real-input tests have a real desktop to draw on
# and real OS input to receive. Windows can't deliver mouse/keyboard input to a window created from a
# non-interactive session (like an SSH command), so this is what lets tests run without you pasting a
# command in by hand each time: start it once, leave it running, and jobs dropped into the queue folder
# run automatically, with an HTML report (screenshots included) opening for each one.
#
# Start it:
#   powershell -File C:\Users\Levi\Documents\GitHub\Slopmaxxing\edit-sharp-gui\Tools\es_runner_agent.ps1
# Stop it: Ctrl+C, any time; it never touches your other windows or takes the mouse except while a
# windowed test itself is actually running (the same way running it by hand would).

$ErrorActionPreference = "Stop"
$repo = "C:\Users\Levi\Documents\GitHub\Slopmaxxing\edit-sharp-gui"
$godot = "C:\tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
$queue = "C:\tmp\es-queue"
$results = "C:\tmp\es-results"

New-Item -ItemType Directory -Force -Path $queue, $results | Out-Null
Write-Host "EditSharp test runner agent watching $queue -- Ctrl+C to stop" -ForegroundColor Cyan

while ($true) {
    $jobs = Get-ChildItem "$queue\*.json" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime
    foreach ($jobFile in $jobs) {
        $job = Get-Content $jobFile.FullName -Raw | ConvertFrom-Json
        $id = $job.id
        $outDir = Join-Path $results $id
        Write-Host "`n[$id] running: $($job.args -join ' ')" -ForegroundColor Yellow

        Push-Location $repo
        try {
            $argList = @("Tools\run_tests.py", "--godot", $godot, "--out", $outDir) + $job.args
            & python @argList *>&1 | Tee-Object -FilePath (Join-Path $results "$id.log")
            $exitCode = $LASTEXITCODE
        } finally {
            Pop-Location
        }

        try {
            python (Join-Path $repo "Tools\test_report.py") $outDir --open
        } catch {
            Write-Host "report generation failed: $_" -ForegroundColor Red
        }

        $exitCode | Out-File -FilePath (Join-Path $results "$id.done") -Encoding ascii
        Remove-Item $jobFile.FullName -Force
        Write-Host "[$id] done, exit $exitCode" -ForegroundColor Cyan
    }
    Start-Sleep -Seconds 1
}
