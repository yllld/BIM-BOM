# Family Converter

Family Converter — MVP Revit Add-in на C# для Autodesk Revit 2021. Плагин работает внутри редактора семейств и преобразует уже импортированную пользователем 3D DWG-геометрию в геометрию Revit.

Плагин не импортирует DWG сам. Пользователь импортирует 3D DWG стандартными средствами Revit, вручную позиционирует его в семействе, выбирает ImportInstance и запускает команду `ENECA_MEP -> DWG Converter -> Family Geometry`.

## Ограничения MVP

- Простые призматические Solid преобразуются в нативные Revit Extrusion.
- Сложные Solid могут быть перенесены как FreeFormElement, если включен fallback.
- Mesh и Curve в MVP не преобразуются, но попадают в отчет как unsupported/skipped.
- Нет пакетной обработки нескольких DWG.
- Нет самостоятельного импорта DWG.
- Нет параметризации размеров, создания типов семейств или параметров.
- AI опционален и не нужен для локальной работы.

Важное ограничение: плагин не гарантирует преобразование любой 3D DWG-геометрии в редактируемые Revit Extrusion. Простые призматические тела преобразуются в нативные формы Revit. Сложные тела могут быть перенесены как FreeFormElement. Mesh-геометрия в MVP не преобразуется.

## Требования

- Autodesk Revit 2021.
- .NET Framework 4.8.
- Windows.
- Visual Studio с поддержкой .NET Framework/WPF.
- Newtonsoft.Json из установленного Revit 2021.

Ссылки на `RevitAPI.dll`, `RevitAPIUI.dll` и `NewtonSoft.Json.dll` настроены на:

```text
C:\Program Files\Autodesk\Revit 2021\
```

`Copy Local` для Revit API выключен.

## Сборка

1. Откройте `src/FamilyConverter.Revit2021.sln` в Visual Studio.
2. Восстановите NuGet-пакеты.
3. Соберите проект `FamilyConverter.Revit2021` под .NET Framework 4.8.
4. Скопируйте DLL, `FamilyConverter.addin` и папку `Resources`, если используется иконка, в каталог add-in Revit 2021.

Каталог установки манифеста:

```text
%APPDATA%\Autodesk\Revit\Addins\2021\
```

В манифесте `FamilyConverter.addin` указан путь:

```text
%APPDATA%\Autodesk\Revit\Addins\2021\FamilyConverter.Revit2021.dll
```

## Использование

1. Откройте или создайте семейство Revit.
2. Импортируйте 3D DWG стандартными средствами Revit.
3. Переместите/поверните DWG как нужно.
4. Выберите импортированный DWG-элемент.
5. Запустите `ENECA_MEP -> DWG Converter -> Family Geometry`.
6. Проверьте настройки преобразования.
7. Нажмите `Преобразовать`.
8. Проверьте созданные формы и отчет.

## AI-конфиг

AI-советник отключен по умолчанию. Плагин полноценно работает локально без AI.

Путь по умолчанию:

```text
%APPDATA%\ENECA_MEP\FamilyConverter\ai_config.json
```

Пример находится в `src/ai_config.example.json`. В нем нет реальных ключей. Реальный `ai_config.json` не должен попадать в репозиторий.

Поддерживаются режимы:

- `openai-compatible-chat-completions`
- `generic-json-post`

В AI отправляется только geometry passport: габариты, объем, сводка граней/ребер, локальная классификация и предупреждения. DWG и RFA не отправляются.

## Отчеты

Если семейство сохранено, отчеты создаются рядом с `.rfa` в папке:

```text
DWG_Conversion_Reports
```

Если семейство не сохранено:

```text
%TEMP%\Family_Converter\
```

Создаются файлы:

```text
DWG_Conversion_Report_yyyyMMdd_HHmmss.json
DWG_Conversion_Report_yyyyMMdd_HHmmss.csv
```

CSV использует разделитель `;` для удобства в русской локали Excel.

## Логи

Логи пишутся в:

```text
%APPDATA%\ENECA_MEP\FamilyConverter\logs\
```

API-ключи не логируются.

## License

MEP Converter is source-available, not open source.

The public repository is licensed under the PolyForm Noncommercial License 1.0.0
unless a separate written license is granted by the copyright holder.

Commercial use, including use in paid BIM/design/engineering workflows,
redistribution, resale, sublicensing, or use in a competing product, requires a
separate written commercial or beta license.

See [LICENSE.md](LICENSE.md).
