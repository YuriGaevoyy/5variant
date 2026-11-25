param(
    [string] $Configuration = 'Release',
    [switch] $SkipTests,
    [switch] $Package
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

dotnet build (Join-Path $root 'DeliveryService.slnx') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Сборка решения завершилась ошибкой.' }

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'DeliveryService.Tests/DeliveryService.Tests.csproj') -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Модульные и интеграционные тесты не прошли.' }
    dotnet test (Join-Path $root 'DeliveryService.UiTests/DeliveryService.UiTests.csproj') -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'UI-тесты не прошли.' }
}

if ($Package) {
    $publish = Join-Path $root 'output/publish/win-x64'
    dotnet publish (Join-Path $root 'DeliveryService.Desktop/DeliveryService.Desktop.csproj') `
        -c $Configuration -r win-x64 --self-contained true -o $publish --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Публикация завершилась ошибкой.' }
    $zip = Join-Path $root 'artifacts/DeliveryService-1.0.0-win-x64.zip'
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force
    Write-Host "Готов архив: $zip"
}
