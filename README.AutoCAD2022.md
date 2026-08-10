# DWG Revit Optimizer для AutoCAD 2022

Отдельный AutoCAD-плагин ветки `feature/autocad-revit-ready-2022`. Он анализирует 3D-модель DWG, оценивает готовность к импорту в Revit и создаёт оптимизированную копию. Исходный файл никогда не перезаписывается.

## Установка

1. Закройте AutoCAD 2022.
2. Запустите `dist/DWG-Revit-Optimizer-AutoCAD2022-Installer.exe`.
3. Запустите AutoCAD 2022. На вкладке `BIM BOM` появится панель `Revit Prep`.

Установщик размещает Autodesk Application Bundle в `%APPDATA%\Autodesk\ApplicationPlugins\DWGOptimizer.bundle`. Тихая установка: `/quiet`; удаление: `/uninstall`.

## Команды

- `DWGREVITREADY` / **Analyze & Optimize** — анализ текущего DWG или предварительно выбранных объектов, выбор профиля и выпуск копии.
- `DWGREVITREADYBATCH` / **Batch Queue** — последовательная обработка набора DWG через AutoCAD Core Console. Основной AutoCAD остаётся доступным.
- `DWGREVITREADYREPORTS` / **Reports** — открыть общий каталог HTML-отчётов.

Результаты записываются рядом с источником в `RevitReady`:

- `<имя>_RevitReady_<профиль>.dwg`;
- `<имя>_RevitReady_<профиль>.revitprep.json` (`schemaVersion: 1`);
- `<имя>_RevitReady_<профиль>.html`.

При совпадении имени автоматически добавляется числовой суффикс.

## Профили

- **Safe** — сохраняет геометрию, включает разрешённые XREF, очищает неиспользуемые записи.
- **Balanced** — дополнительно оставляет полезную для Revit 3D-геометрию, выполняет `CleanBody` и безопасную попытку Mesh→Solid.
- **Aggressive** — раскрывает блоки, упрощает subdivided Mesh и объединяет пересекающиеся Solid одного слоя/материала с проверкой допусков.

При `INSUNITS=0` единицы обязательны. Перенос модели к WCS 0,0,0 выполняется только после отдельного выбора. Отсутствующий или циклический XREF блокирует выпуск, пока пользователь явно не разрешит продолжить без него.

Для тяжёлых наборов свыше 250 000 Mesh-граней Balanced сохраняет Mesh без автоматического Mesh→Solid: это защищает пакетную обработку от резкого роста времени и памяти. Преобразование Mesh остаётся на стороне Revit-плагина.

## Сборка

Требуются AutoCAD 2022, .NET Framework 4.8 Developer Pack и Visual Studio Build Tools 2022:

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' DWGOptimizer.AutoCAD2022.sln /restore /p:Configuration=Release
```

Ссылки на `AcCoreMgd`, `AcDbMgd`, `AcMgd`, `AcDbMgdBrep` и `AdWindows` берутся из AutoCAD 2022 и не копируются в bundle. Для AutoCAD 2021 предусмотрены отдельные сборки и установщик, описанные в [README.AutoCAD2021.md](README.AutoCAD2021.md).

## Текущие ограничения MVP

- Поддерживается только AutoCAD 2022 (R24.1), x64.
- Полный интерактивный `AUDIT` нельзя безопасно выполнить над side database публичным .NET API. Плагин проверяет `Database.NeedsRecovery`, анализирует чтение объектов и прекращает безопасную обработку повреждённых файлов; для них сначала требуется `RECOVER` в AutoCAD. PURGE выполняется над рабочей копией.
- Пакетный MVP использует профиль Balanced; файлы с неизвестными единицами завершаются блокером и требуют одиночного запуска для назначения единиц.
- Revit-плагин пока не читает JSON; отчёт подготовлен для будущей интеграции.
- Для интеграционных тестов Core Console профиль AutoCAD 2022 должен быть инициализирован хотя бы одним запуском AutoCAD 2022.
