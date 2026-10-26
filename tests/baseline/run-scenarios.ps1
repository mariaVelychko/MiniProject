<#
.SYNOPSIS
  Знімок поведінки (characterization / baseline) API «розклад + записи».
.DESCRIPTION
  Виконує фіксований набір сценаріїв проти $Base і зберігає код статусу, тіло та ключові заголовки кожної відповіді
  в теку $Out. Динамічні id нормалізуються (<n>), щоб знімки можна було порівнювати між запусками.

  ЕТАП 0 ЛР2 — запускати ДО будь-яких змін у коді, проти МОНОЛІТУ напряму:
      .\run-scenarios.ps1 -Base http://localhost:5100 -Out out
  Після винесення — проти шлюзу (порт 5000):
      .\run-scenarios.ps1 -Base http://localhost:5000 -Out out-after
  і порівняти:
      .\compare.ps1 -Before out -After out-after -Markdown ..\..\docs\baseline-diff.md

  Вимоги: запущені Auth/Catalog (у Catalog є активна позиція з Id=1) і моноліт; скрипт сам створює тренера й трьох клієнтів
  (baseline.*@club.test), а наприкінці видаляє створені тренування.
#>
param(
    [string]$Base = "http://localhost:5100",
    [string]$Out = "out",
    [int]$CatalogItemId = 1,
    [switch]$NoCleanup
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$http = [System.Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(30)
$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Get-ChildItem $Out -File | Remove-Item -Force

function Call([string]$Method, [string]$Path, [string]$Body = $null, [hashtable]$Headers = @{}) {
    $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$Base$Path")
    if (-not [string]::IsNullOrEmpty($Body)) { $req.Content = [System.Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, "application/json") }
    foreach ($k in $Headers.Keys) { [void]$req.Headers.TryAddWithoutValidation($k, $Headers[$k]) }
    $resp = $http.SendAsync($req).GetAwaiter().GetResult()
    $text = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    [pscustomobject]@{
        Status      = [int]$resp.StatusCode
        Body        = $text
        Location    = $(if ($resp.Headers.Location) { $resp.Headers.Location.ToString() } else { "" })
        ContentType = $(if ($resp.Content.Headers.ContentType) { $resp.Content.Headers.ContentType.MediaType } else { "" })
    }
}

function Normalize([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return "" }
    $t = [regex]::Replace($text, '"(id|coachId)":\d+', '"$1":"<n>"')
    $t = [regex]::Replace($t, '"participantIds":\[[^\]]*\]', { param($m) [regex]::Replace($m.Value, '\d+', '<n>') })
    $t = [regex]::Replace($t, '"traceId":"[^"]*"', '"traceId":"<trace>"')
    return $t
}

$n = 0
function Save([string]$Name, $R) {
    $script:n++
    $base = Join-Path $Out ("{0:D2}-{1}" -f $script:n, $Name)
    $loc = [regex]::Replace($R.Location, '^https?://[^/]+', '')
    $loc = [regex]::Replace($loc, '/\d+$', '/<n>')
    [IO.File]::WriteAllText("$base.status", "$($R.Status)`n", $utf8)
    [IO.File]::WriteAllText("$base.json", (Normalize $R.Body), $utf8)
    [IO.File]::WriteAllText("$base.meta", "content-type: $($R.ContentType)`nlocation: $loc`n", $utf8)
    Write-Host ("{0:D2} {1,-38} -> {2}" -f $script:n, $Name, $R.Status)
}

function Ensure-User([string]$Role, [string]$Email, [string]$Name, [string]$Spec = $null) {
    $body = @{ role = $Role; fullName = $Name; email = $Email; phone = "+380500000000"; password = "Pass123!" }
    if ($Spec) { $body.specialization = $Spec }
    $r = Call POST "/api/users" ($body | ConvertTo-Json -Compress)
    if ($r.Status -eq 201) { return ($r.Body | ConvertFrom-Json).id }
    $all = (Call GET "/api/users").Body | ConvertFrom-Json
    return ($all | Where-Object { $_.email -eq $Email } | Select-Object -First 1).id
}

$coach = Ensure-User "trainer" "baseline.coach@club.test" "Baseline Coach" "Йога"
$c1 = Ensure-User "client" "baseline.client1@club.test" "Baseline Client 1"
$c2 = Ensure-User "client" "baseline.client2@club.test" "Baseline Client 2"
$c3 = Ensure-User "client" "baseline.client3@club.test" "Baseline Client 3"
Write-Host "coach=$coach clients=$c1,$c2,$c3 (id-шки в знімках нормалізуються)"

function WorkoutJson($item, $coachId, $hall, $max, $time = "2030-01-01T10:00:00") {
    $o = [ordered]@{ catalogItemId = $item; time = $time; coachId = $coachId }
    if ($null -ne $hall) { $o.hall = $hall }
    if ($null -ne $max) { $o.maxParticipants = $max }
    return ($o | ConvertTo-Json -Compress)
}
function BookJson($w, $c) { return (@{ workoutId = $w; clientId = $c } | ConvertTo-Json -Compress) }

$created = @()

# --- створення тренування ---
$r = Call POST "/api/workouts" (WorkoutJson $CatalogItemId $coach "Baseline Hall" 2)
Save "create-workout-valid" $r
$w = ($r.Body | ConvertFrom-Json).id; $created += $w
Save "create-workout-unknown-catalog-item" (Call POST "/api/workouts" (WorkoutJson 999999 $coach "Baseline Hall" 2))
Save "create-workout-unknown-coach" (Call POST "/api/workouts" (WorkoutJson $CatalogItemId 999999 "Baseline Hall" 2))
Save "create-workout-blank-hall" (Call POST "/api/workouts" (WorkoutJson $CatalogItemId $coach "   " 2))
Save "create-workout-missing-hall" (Call POST "/api/workouts" (WorkoutJson $CatalogItemId $coach $null 2))
Save "create-workout-zero-capacity" (Call POST "/api/workouts" (WorkoutJson $CatalogItemId $coach "Baseline Hall" 0))
Save "get-workout" (Call GET "/api/workouts/$w")
Save "get-workout-unknown" (Call GET "/api/workouts/999999")

# --- запис на тренування ---
Save "book-valid" (Call POST "/api/bookings" (BookJson $w $c1))
Save "book-duplicate" (Call POST "/api/bookings" (BookJson $w $c1))
Save "book-second-client" (Call POST "/api/bookings" (BookJson $w $c2))
Save "book-when-full" (Call POST "/api/bookings" (BookJson $w $c3))
Save "book-unknown-workout" (Call POST "/api/bookings" (BookJson 999999 $c1))
Save "book-unknown-client" (Call POST "/api/bookings" (BookJson $w 999999))
Save "book-trainer-as-client" (Call POST "/api/bookings" (BookJson $w $coach))

# --- токен: моноліт його не вимагав і не перевіряв ---
$r = Call POST "/api/workouts" (WorkoutJson $CatalogItemId $coach "Baseline Hall 2" 5 "2030-01-02T10:00:00")
$w2 = ($r.Body | ConvertFrom-Json).id; $created += $w2
Save "book-with-garbage-bearer-token" (Call POST "/api/bookings" (BookJson $w2 $c1) @{ Authorization = "Bearer not-a-real-token" })

# --- читання ---
Save "get-participants" (Call GET "/api/bookings/workout/$w")
Save "get-participants-unknown-workout" (Call GET "/api/bookings/workout/999999")
Save "get-client-bookings" (Call GET "/api/bookings/client/$c1")
Save "list-filter-coach-and-item" (Call GET "/api/workouts?coachId=$coach&catalogItemId=$CatalogItemId")
Save "profile-unknown-user" (Call GET "/api/profile/999999")

# --- зміна ---
Save "update-capacity-below-booked" (Call PUT "/api/workouts/$w" (WorkoutJson $CatalogItemId $coach "Baseline Hall" 1))
Save "update-valid" (Call PUT "/api/workouts/$w" (WorkoutJson $CatalogItemId $coach "Baseline Hall B" 3))
Save "update-unknown-workout" (Call PUT "/api/workouts/999999" (WorkoutJson $CatalogItemId $coach "Baseline Hall" 3))

# --- скасування ---
Save "cancel-booking" (Call DELETE "/api/bookings/$w/$c1")
Save "cancel-booking-repeat" (Call DELETE "/api/bookings/$w/$c1")
Save "cancel-unknown-workout" (Call DELETE "/api/bookings/999999/$c1")

# --- видалення ---
Save "delete-workout" (Call DELETE "/api/workouts/$w")
Save "delete-workout-repeat" (Call DELETE "/api/workouts/$w")

if (-not $NoCleanup) {
    foreach ($id in $created) { [void](Call DELETE "/api/workouts/$id") }
}

Write-Host "`nГотово: $n сценаріїв збережено в '$Out'."
Write-Host "Ручний сценарій (не автоматизується): зупиніть Catalog і повторіть create-workout-valid — запишіть код і тіло в $Out\manual-catalog-down.txt."
