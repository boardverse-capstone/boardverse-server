# BoardVerse Integration Tests Runner
# Chạy tất cả integration tests hoặc chỉ flow cụ thể

param(
    [string]$TestFilter = "",
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"
$projectPath = "c:\Users\ASUS\source\repos\BoardVerse\BoardVerse.Tests"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "BoardVerse Integration Tests" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

if ($Verbose) {
    Write-Host "Running in verbose mode..." -ForegroundColor Yellow
}

# Filter mặc định: chỉ chạy Booking/Matchmaking/POS flow tests
if ([string]::IsNullOrWhiteSpace($TestFilter)) {
    $TestFilter = "FullyQualifiedName~BookingMatchmakingPosFlowIntegrationTests"
    Write-Host "Using default filter: $TestFilter" -ForegroundColor Green
} else {
    Write-Host "Using custom filter: $TestFilter" -ForegroundColor Green
}

Write-Host ""
Write-Host "Running tests..." -ForegroundColor Cyan
Write-Host ""

# Chạy tests với coverlet (coverage report)
$testArguments = @(
    "test",
    $projectPath,
    "--filter", $TestFilter,
    "--logger", "console;verbosity=detailed",
    "--collect:XPlat Code Coverage"
)

if ($Verbose) {
    $testArguments += "--logger", "console;verbosity=detailed"
}

& dotnet @testArguments

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "All tests passed!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "Some tests failed!" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    exit $LASTEXITCODE
}
