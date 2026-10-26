param(
    [string]$MySqlUser = "root",
    [string]$MySqlHost = "localhost",
    [int]$MySqlPort = 3307,
    [string]$MonolithDb = "sportclub_cs",
    [string[]]$Only = @("Auth", "Catalog", "Monolith", "Schedule", "Gateway"),
    [switch]$ResetDb,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$secure = Read-Host "Пароль MySQL для користувача '$MySqlUser'" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$password = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

function New-ConnectionString([string]$database) {
    "server=$MySqlHost;port=$MySqlPort;database=$database;user=$MySqlUser;password=$password;"
}

$env:ConnectionStrings__SportClub = New-ConnectionString $MonolithDb
$env:ConnectionStrings__AuthDb    = New-ConnectionString "miniproject_auth"
$env:ConnectionStrings__CatalogDb = New-ConnectionString "miniproject_catalog"
$env:ConnectionStrings__ScheduleDb = New-ConnectionString "miniproject_schedule"

if (-not $env:Jwt__Key) {
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $env:Jwt__Key = [Convert]::ToBase64String($bytes)
}

if ($ResetDb) {
    $env:RESET_DB = "1"
    Write-Host "RESET_DB=1: бази даних буде видалено й створено наново." -ForegroundColor Yellow
} else {
    Remove-Item Env:RESET_DB -ErrorAction SilentlyContinue
}

Write-Host "Збираю рішення (dotnet build)..." -ForegroundColor Cyan
dotnet build "$root\MiniProject.sln" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build завершився з помилкою - виправ її й запусти скрипт знову." }

$services = [ordered]@{
    Auth     = @{ Title = "AuthService (5001)";    Project = "src\AuthService";    Health = "http://localhost:5001/health"; Swagger = "http://localhost:5001/swagger" }
    Catalog  = @{ Title = "CatalogService (5002)"; Project = "src\CatalogService"; Health = "http://localhost:5002/health"; Swagger = "http://localhost:5002/swagger" }
    Monolith = @{ Title = "Monolith (5100)";       Project = "src\Monolith";       Health = "http://localhost:5100/health"; Swagger = "http://localhost:5100/swagger" }
    Schedule = @{ Title = "ScheduleService (5102)"; Project = "src\ScheduleService"; Health = "http://localhost:5102/health"; Swagger = "http://localhost:5102/swagger" }
    Gateway  = @{ Title = "ApiGateway (5000)";     Project = "src\ApiGateway";     Health = "http://localhost:5000/health"; Swagger = $null }
}

foreach ($name in $services.Keys) {
    if ($Only -notcontains $name) { continue }

    $svc = $services[$name]
    Write-Host "Запускаю $($svc.Title)..." -ForegroundColor Cyan

    $command = "`$Host.UI.RawUI.WindowTitle = '$($svc.Title)'; dotnet run --no-build --project `"$root\$($svc.Project)`""
    Start-Process powershell -ArgumentList "-NoExit", "-Command", $command -WorkingDirectory $root | Out-Null

    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        try {
            $response = Invoke-WebRequest -Uri $svc.Health -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
    }

    if ($ready) {
        Write-Host "  OK: $($svc.Title) відповідає на /health" -ForegroundColor Green
    } else {
        Write-Host "  УВАГА: $($svc.Title) не відповів за 60 с - подивись його вікно (там причина і підказка)." -ForegroundColor Red
    }
}

$password = $null
Write-Host ""
Write-Host "Готово. Адреси:" -ForegroundColor Green
Write-Host "  Шлюз (вхід для клієнта)  http://localhost:5000"
Write-Host "  AuthService   Swagger    http://localhost:5001/swagger"
Write-Host "  ScheduleService Swagger  http://localhost:5102/swagger"
Write-Host "  CatalogService Swagger   http://localhost:5002/swagger"
Write-Host "  Monolith      Swagger    http://localhost:5100/swagger"
Write-Host "  Адмін Auth: admin@miniproject.local / Admin123!   |   Адмін моноліту: admin / admin"

if (-not $NoBrowser) {
    foreach ($name in "Auth", "Catalog", "Monolith") {
        if (($Only -contains $name) -and $services[$name].Swagger) { Start-Process $services[$name].Swagger }
    }
}
