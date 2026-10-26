<#
.SYNOPSIS
  Порівнює два знімки поведінки (до/після винесення) і, за бажанням, генерує таблицю для docs/baseline-diff.md.
.EXAMPLE
  .\compare.ps1 -Before out -After out-after -Markdown ..\..\docs\baseline-diff.md
  Код виходу: 0 — розбіжностей немає, 1 — є розбіжності.
#>
param(
    [string]$Before = "out",
    [string]$After = "out-after",
    [string]$Markdown = ""
)

$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read([string]$p) { if (Test-Path $p) { [regex]::Replace([IO.File]::ReadAllText($p).Trim(), '(?<=\p{L} )(?!999999)\d+(?=\s)', '<n>') } else { "<немає файлу>" } }

$names = Get-ChildItem $Before -Filter *.status | ForEach-Object { $_.BaseName } | Sort-Object
$rows = @()
$diffs = 0

foreach ($name in $names) {
    $sb = Read (Join-Path $Before "$name.status"); $sa = Read (Join-Path $After "$name.status")
    $bb = Read (Join-Path $Before "$name.json");   $ba = Read (Join-Path $After "$name.json")
    $mb = Read (Join-Path $Before "$name.meta");   $ma = Read (Join-Path $After "$name.meta")

    $same = ($sb -eq $sa) -and ($bb -eq $ba) -and ($mb -eq $ma)

    if (-not $same) {
        $diffs++
        Write-Host "РІЗНИЦЯ: $name" -ForegroundColor Yellow
        Write-Host "  статус: $sb -> $sa"
        if ($bb -ne $ba) { Write-Host "  було : $bb"; Write-Host "  стало: $ba" }
        if ($mb -ne $ma) { Write-Host "  meta було : $($mb -replace "`n",' | ')"; Write-Host "  meta стало: $($ma -replace "`n",' | ')" }
    }

    $rows += "| $name | $sb | $sa | $(if ($same) { 'ні' } else { 'так' }) | $(if ($same) { '—' } else { 'ПОЯСНІТЬ' }) |"
}

Write-Host ("`nСценаріїв: {0}, з різницею: {1}" -f $names.Count, $diffs) -ForegroundColor $(if ($diffs -eq 0) { "Green" } else { "Yellow" })

if ($Markdown) {
    $md = @("<!-- згенеровано tests/baseline/compare.ps1; ручні рядки з розділу 'Свідомі відмінності' нижче збережіть -->",
        "",
        "## Автоматичне порівняння знімків (до/після)",
        "",
        "| Сценарій | Було (моноліт) | Стало (через шлюз) | Чи є різниця | Чому |",
        "|---|---|---|---|---|") + $rows
    [IO.File]::WriteAllText($Markdown, ($md -join "`n") + "`n", $utf8)
    Write-Host "Таблицю записано в $Markdown"
}

exit $(if ($diffs -eq 0) { 0 } else { 1 })
