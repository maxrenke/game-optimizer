# full_test_pass.ps1 - Runs all gaming-optimizer test suites
$root = $PSScriptRoot

pwsh -ExecutionPolicy Bypass -File "$root\gaming-optimizer.tests.ps1"
pwsh -ExecutionPolicy Bypass -File "$root\gaming-optimizer-tests.ps1"

exit ($LASTEXITCODE)
