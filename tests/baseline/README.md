# tests/baseline — знімок поведінки ДО винесення (ЛР2, крок 0)

Знімок знімається з **моноліту напряму** (порт 5100) і комітиться в окремому коміті **до** змін у коді сервісу:

```powershell
cd tests\baseline
.\run-scenarios.ps1 -Base http://localhost:5100 -Out out      # 30+ сценаріїв: статус + тіло + заголовки
git add out && git commit -m "ЛР2: baseline поведінки моноліту (до винесення Schedule)"
```

Для кожного сценарію зберігається `NN-name.status`, `NN-name.json` (id нормалізовано в `<n>`), `NN-name.meta`
(Content-Type і Location без хоста). Після винесення і перемикання шлюзу:

```powershell
.\run-scenarios.ps1 -Base http://localhost:5000 -Out out-after   # ті самі сценарії, але через шлюз
.\compare.ps1 -Before out -After out-after -Markdown ..\..\docs\baseline-diff.md
```

Ручний сценарій (його не можна автоматизувати): зупиніть Catalog і повторіть створення тренування — у моноліті
це `503` з `{"message":"Каталог типів тренувань недоступний. Спробуйте пізніше."}`; запишіть результат у `out/manual-catalog-down.txt`
(до змін — з моноліту, після — зі Schedule).
