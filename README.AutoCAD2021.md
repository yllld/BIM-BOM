# DWG Revit Optimizer для AutoCAD 2021

Версия AutoCAD 2021 использует тот же движок анализа и оптимизации, что и AutoCAD 2022, но собирается отдельно против Autodesk AutoCAD .NET API R24.0.

## Установка

1. Закройте AutoCAD 2021.
2. Запустите `dist/DWG-Revit-Optimizer-AutoCAD2021-Installer.exe`.
3. Запустите AutoCAD 2021. На вкладке `BIM BOM` появится панель `Revit Prep`.

Установщик создаёт отдельный bundle `%APPDATA%\Autodesk\ApplicationPlugins\DWGOptimizer2021.bundle`, поэтому может быть установлен одновременно с версией для AutoCAD 2022. Тихая установка: `/quiet`; удаление: `/uninstall`.

Доступны те же команды и профили, что описаны в [README.AutoCAD2022.md](README.AutoCAD2022.md):

- `DWGREVITREADY` — анализ и оптимизация текущего DWG;
- `DWGREVITREADYBATCH` — последовательная пакетная обработка через AutoCAD Core Console 2021;
- `DWGREVITREADYREPORTS` — открыть папку отчётов.

## Сборка

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' DWGOptimizer.AutoCAD2022.sln /restore /p:Configuration=Release
```

Сборка AutoCAD 2021 получает compile-time API R24.0 из официальных NuGet-пакетов Autodesk `AutoCAD.NET`, `AutoCAD.NET.Core` и `AutoCAD.NET.Model`. API DLL Autodesk не копируются в bundle: во время работы используются библиотеки установленного AutoCAD 2021.

## Ограничение проверки

Автоматическая сборка подтверждает совместимость кода с API R24.0. Перед выдачей пользователям нужен ручной smoke-test в установленном AutoCAD 2021: загрузка Ribbon, одиночная оптимизация и пакетная очередь через Core Console 2021.
