<#
  Демо-дані для клієнтського застосунку (ЛР4). Усе робиться ЧЕРЕЗ ШЛЮЗ — так само, як це робить клієнт.
  Що створюється:
    1) обліковий запис клієнта в Auth (для входу в застосунок);
    2) профіль клієнта в довіднику моноліту з ТІЄЮ САМОЮ електронною поштою (шлюз зіставляє їх за email, див. ADR-0005);
    3) тренер (якщо в моноліті його ще немає);
    4) по два тренування для перших трьох позицій каталогу.
  Пароль не зберігається в репозиторії: його запитує скрипт.
  Запуск:  powershell -ExecutionPolicy Bypass -File .\scripts\seed-demo.ps1
#>
param(
    [string]$Gateway = "http://localhost:5000",
    [string]$Email = "anna@miniproject.local",
    [string]$DisplayName = "Анна Клієнт"
)

$ErrorActionPreference = "Stop"

$secure = Read-Host "Придумайте пароль для $Email (не менше 8 символів)" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$password = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

function Invoke-Api([string]$Method, [string]$Path, $Body = $null) {
    $request = @{ Uri = "$Gateway$Path"; Method = $Method; UseBasicParsing = $true }
    if ($null -ne $Body) {
        $request.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 5))
        $request.ContentType = "application/json; charset=utf-8"
    }
    Invoke-RestMethod @request
}

function Try-Step([string]$Title, [scriptblock]$Action) {
    try { & $Action | Out-Null; Write-Host "  OK: $Title" -ForegroundColor Green }
    catch { Write-Host "  пропущено ($Title): $($_.Exception.Message)" -ForegroundColor Yellow }
}

Write-Host "Створюю демо-дані через шлюз $Gateway" -ForegroundColor Cyan

Try-Step "обліковий запис Auth $Email" { Invoke-Api POST "/api/v1/auth/register" @{ email = $Email; password = $password; displayName = $DisplayName } }

Try-Step "профіль клієнта в моноліті ($Email)" {
    Invoke-Api POST "/api/users" @{ role = "client"; fullName = $DisplayName; email = $Email; phone = "+380501112233"; password = $password; specialization = $null }
}

$trainers = @(Invoke-Api GET "/api/users?role=trainer")
if ($trainers.Count -eq 0) {
    Try-Step "тренер" { Invoke-Api POST "/api/users" @{ role = "trainer"; fullName = "Іван Ковальчук"; email = "coach@miniproject.local"; phone = "+380509998877"; password = $password; specialization = "Загальна підготовка" } }
    $trainers = @(Invoke-Api GET "/api/users?role=trainer")
}

if ($trainers.Count -eq 0) { throw "У моноліті немає жодного тренера - тренування створити не можна." }
$coachId = $trainers[0].id

$items = (Invoke-Api GET "/api/v1/catalog/items?pageSize=3").items
$day = 2
foreach ($item in $items) {
    foreach ($slot in @(@{ Hour = 18; Hall = "Зал 1" }, @{ Hour = 10; Hall = "Зал 2" })) {
        $time = (Get-Date).Date.AddDays($day).AddHours($slot.Hour).ToString("yyyy-MM-ddTHH:mm:ss")
        Try-Step "тренування '$($item.title)' $time" { Invoke-Api POST "/api/workouts" @{ catalogItemId = $item.id; time = $time; hall = $slot.Hall; coachId = $coachId } }
        $day++
    }
}

$password = $null
Write-Host ""
Write-Host "Готово. Вхід у клієнт: $Email та пароль, який ви щойно ввели." -ForegroundColor Green
