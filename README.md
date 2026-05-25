# DWG Converter - Family Geometry

DWG Converter - Family Geometry — MVP Revit Add-in на C# для Autodesk Revit 2021. Плагин работает внутри редактора семейств и преобразует уже импортированную пользователем 3D DWG-геометрию в геометрию Revit.

Плагин не импортирует DWG сам. Пользователь импортирует 3D DWG стандартными средствами Revit, вручную позиционирует его в семействе, выбирает ImportInstance и запускает команду `BIM BOM -> DWG Converter - Family Geometry -> Simple Convert`.

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
3. Соберите solution в конфигурации `Release`.
4. Для ручной установки скопируйте DLL и `FamilyConverter.addin` в каталог add-in Revit 2021.

Каталог установки манифеста:

```text
%APPDATA%\Autodesk\Revit\Addins\2021\
```

В манифесте `FamilyConverter.addin` указан путь:

```text
%APPDATA%\Autodesk\Revit\Addins\2021\FamilyConverter.Revit2021.dll
```

## Установщик

Release-сборка создает единый EXE-установщик:

```text
DWGConverter.FamilyGeometry.Installer\bin\Release\DWGConverter-FamilyGeometry-Installer.exe
```

Готовый собранный файл для скачивания хранится в:

```text
dist\DWGConverter-FamilyGeometry-Installer.exe
```

Установщик не требует прав администратора. Он записывает DLL и `.addin` в:

```text
%APPDATA%\Autodesk\Revit\Addins\2021\
```

Для тихой установки можно запустить:

```text
DWGConverter-FamilyGeometry-Installer.exe /quiet
```

## Использование

1. Откройте или создайте семейство Revit.
2. Импортируйте 3D DWG стандартными средствами Revit.
3. Переместите/поверните DWG как нужно.
4. Выберите импортированный DWG-элемент.
5. Запустите `BIM BOM -> DWG Converter - Family Geometry -> Simple Convert`.
6. Проверьте профиль в основном окне. Настройки конвертации открываются через иконку шестеренки, AI-настройки - через иконку со звездой.
7. Нажмите `Преобразовать`.
8. Проверьте созданные формы и отчет.

В обычном режиме длительные этапы показываются в отдельном небольшом окне прогресса.

На панели также есть кнопка `Reports Folder`: она открывает папку отчетов для
текущего семейства, а для несохраненного семейства - временную папку отчетов.
Кнопки `Donate` и `Tech Support` пока являются заглушками под будущие ссылки.

## Turbo FreeForm

Для очень тяжелых импортированных DWG используйте:

```text
BIM BOM -> DWG Converter - Family Geometry -> Turbo FreeForm
```

Режим намеренно грубее обычного, но быстрее:

- без окна предварительного анализа всей геометрии;
- без попыток `Extrusion`;
- без AI;
- без диагностики `Mesh`/`Curve`;
- без чтения слоев DWG;
- `Solid` создаются сразу как `FreeFormElement`;
- отчеты JSON/CSV можно оставить включенными.

Перед запуском Turbo открывается окно настроек. Главные пороги для упрощения
семейства:

- `Минимальный объем Solid, мм³` - Solid с меньшим объемом не создается.
- `Минимальный максимальный габарит, мм` - Solid, у которого самый большой
  размер меньше порога, не создается.
- значение `0` отключает соответствующий порог.

Проверку созданной геометрии по допускам габаритов/объема можно включить в этом
же окне, но по умолчанию она выключена для скорости.

Этот режим полезен, когда Revit зависает на больших DWG. Во время работы Revit
может показывать `Не отвечает`; дождитесь завершения операции.

## AI-конфиг

AI-советник отключен по умолчанию. Плагин полноценно работает локально без AI.

Путь по умолчанию:

```text
%APPDATA%\DWG_Converter\Family_Geometry\ai_config.json
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
%TEMP%\DWG_Converter_Family_Geometry\
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
%APPDATA%\DWG_Converter\Family_Geometry\logs\
```

API-ключи не логируются.

## License

DWG Converter - Family Geometry is source-available, not open source.

The public repository is licensed under the PolyForm Noncommercial License 1.0.0
unless a separate written license is granted by the copyright holder.

Commercial use, including use in paid BIM/design/engineering workflows,
redistribution, resale, sublicensing, or use in a competing product, requires a
separate written commercial or beta license.

See [LICENSE.md](LICENSE.md).
