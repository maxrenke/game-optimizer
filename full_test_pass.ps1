# full_test_pass.ps1 - Runs all gaming-optimizer test suites
$root = $PSScriptRoot

pwsh -ExecutionPolicy Bypass -File "$root\gaming-optimizer.tests.ps1"
$unitExit = $LASTEXITCODE

pwsh -ExecutionPolicy Bypass -File "$root\gaming-optimizer-tests.ps1"
$integrationExit = $LASTEXITCODE

$total = $unitExit + $integrationExit
if ($total -gt 0) {
    Write-Host "`nFAILED: unit=$unitExit  integration=$integrationExit" -ForegroundColor Red
} else {
    Write-Host "`nAll suites passed." -ForegroundColor Green
}
exit $total
