# DWG Converter - Family Geometry

MVP Revit Add-in на C# / WPF для Autodesk Revit 2021. Плагин работает внутри редактора семейств и помогает получать нативную геометрию Revit из уже импортированных DWG.

Плагин не импортирует DWG сам. Пользователь импортирует DWG стандартными средствами Revit, выбирает `ImportInstance` и запускает нужную команду на панели:

```text
BIM BOM -> DWG Converter - Family Geometry
```

## Команды

- `Simple Convert` - существующий режим для 3D DWG.
- `Turbo FreeForm` - быстрый fallback-режим для тяжёлых 3D импортов.
- `2D Drawing to Family` - инженерный MVP для 2D DWG без AI, OCR, ML и внешних API.
- `Reports Folder` - открывает папку отчётов.

Кнопка проверки масштаба для 2D режима не используется и не возвращается.

## 2D Drawing to Family

`2D Drawing to Family` строит черновую 3D RFA-заготовку из линий импортированного 2D DWG. Режим работает только с геометрией, которую отдаёт Revit API через выбранный `ImportInstance`.

Сценарий:

1. Откройте или создайте семейство Revit.
2. Импортируйте 2D DWG.
3. Выберите один `ImportInstance`.
4. Запустите `2D Drawing to Family`.
5. Проверьте таблицу слоёв DWG.
6. При необходимости поменяйте роль слоя и включение слоя.
7. Нажмите `Выбрать вид сверху` и укажите 4 точки рамки в Revit.
8. Нажмите `Выбрать вид спереди` и укажите 4 точки.
9. При необходимости выберите `Вид сбоку`.
10. Нажмите `Переанализировать`.
11. Нажмите `Построить`.

После `Построить` команда больше не создает геометрию сразу. Сначала запускается пошаговый мастер:

- шаг 1: показывает количество замкнутых, открытых и невалидных контуров в `Plan View`;
- шаг 1A: если есть разомкнутые линии, спрашивает, замыкать ли их автоматически в пределах допуска;
- шаг 2: показывает начало/конец выдавливания и размеры из `Plan`, `Front`, `Side`;
- шаг 3: показывает найденные формы по линиям DWG: прямоугольники, квадраты, круги/почти круги, отверстия;
- шаг 4: предлагает `только отчет`, `построить 1 самое крупное тело` или `построить все тела поэтапно`.

Окно 2D режима показывает:

- имя и ID импортированного DWG;
- количество считанных объектов;
- количество слоёв;
- общий габарит DWG в мм;
- таблицу слоёв с визуальной цветовой плашкой;
- статус выбранных областей Plan / Front / Side;
- количество объектов и размер каждой выбранной области;
- сводку найденных контуров.

В основном экране оставлены только две настройки:

- `Геометрический допуск, мм` - по умолчанию `2`.
- `Минимальный размер элемента, мм` - по умолчанию `10`.

## Как строится 2D геометрия

Новый режим больше не выбирает только один самый крупный контур. Он пытается построить максимум доступной геометрии:

- читает `Line`, `Arc`, `Ellipse`, `NurbSpline`, `PolyLine` и другие `Curve`-объекты, которые Revit отдаёт из DWG;
- группирует объекты по слоям;
- предварительно классифицирует слои по имени, но пользователь может изменить роль вручную;
- берёт линии внутри выбранных пользователем 4-точечных областей проекций;
- находит все простые замкнутые и почти замкнутые контуры;
- назначает `solid` и `void` по even-odd вложенности;
- открытые линии в crash-safe режиме не отправляются в `NewModelCurve`, а фиксируются в отчёте как reference candidates;
- сопоставляет Plan / Front / Side по ширине, глубине и высоте;
- создаёт список `BuildCandidate`, а не один общий extrusion;
- crash-safe MVP режим строит простые bbox `Extrusion` только по solid-кандидатам из `Plan View`;
- `Front View` и `Side View` используются только для снятия высоты/глубины и сверки размеров, но не создают самостоятельные тела;
- построение идет от крупных тел к меньшим; при выборе тестового режима создается только один самый крупный кандидат;
- точные DWG-контуры, `FreeFormElement` и `ModelCurve` пока не передаются в Revit model creation, потому что эти вызовы могут вызывать fatal crash на грязной CAD-геометрии;
- найденные void/отверстия и открытые контуры записываются в отчёт, но не режут тела в текущем аварийно-устойчивом режиме;
- использует аварийный `FALLBACK Plan bounding box` только если вообще ничего не удалось построить.

Правила проекций:

- `Plan View` даёт ширину и глубину.
- `Front View` даёт ширину и высоту.
- `Side View` опционален и помогает уточнить глубину/высоту.

## Отчёты и логи

Для команды `2D Drawing to Family` создаются два файла:

```text
%APPDATA%\DWG_Converter\Family_Geometry\logs\DrawingToFamily_Report_yyyyMMdd_HHmmss.txt
%APPDATA%\DWG_Converter\Family_Geometry\logs\DrawingToFamily_Log_yyyyMMdd_HHmmss.log
```

Отчёт содержит:

- Revit version, имя файла и семейства;
- ID `ImportInstance`;
- количество считанных объектов;
- объекты внутри Plan / Front / Side;
- таблицу слоёв с цветом, ролью и включением;
- выбранные области проекций;
- найденные `solid`, `void`, `open/reference`, `invalid` контуры;
- build candidates и confidence;
- сколько создано solid extrusion;
- сколько создано FreeForm elements;
- сколько создано reference/model lines;
- сколько кандидатов пропущено;
- build coverage;
- предупреждения, ошибки и факт использования `FALLBACK`.

## Масштаб

Отдельной проверки масштаба нет. Режим использует фактические координаты импортированной DWG-геометрии в Revit и показывает размеры в мм.

Если итоговые размеры меньше `10 мм` или больше `100000 мм`, в отчёт добавляется предупреждение:

```text
Проверьте единицы импорта DWG. Отдельная проверка масштаба в MVP не используется.
```

## Ограничения 2D MVP

MVP не читает текстовые размеры, MTEXT, выноски, OCR, ML и внешние AI API. Он не является полноценным CAD SDK и не обещает идеальную производственную BIM-модель.

Сложные NURBS/сплайны аппроксимируются сегментами, если это возможно. Самопересекающиеся, плохие или незамкнутые контуры могут быть сохранены как reference lines или попасть в отчёт как skipped с причиной.

## Требования

- Autodesk Revit 2021.
- .NET Framework 4.8.
- Windows.
- Visual Studio с поддержкой .NET Framework/WPF.
- `RevitAPI.dll`, `RevitAPIUI.dll`, `NewtonSoft.Json.dll` из установленного Revit 2021.

Ожидаемый путь Revit API:

```text
C:\Program Files\Autodesk\Revit 2021\
```

## Сборка

Откройте:

```text
src\FamilyConverter.Revit2021.sln
```

Соберите solution в конфигурации `Release`.

Готовый installer:

```text
DWGConverter.FamilyGeometry.Installer\bin\Release\DWGConverter-FamilyGeometry-Installer.exe
```

Копия для выдачи:

```text
dist\DWGConverter-FamilyGeometry-Installer.exe
```

Installer устанавливает add-in в:

```text
%APPDATA%\Autodesk\Revit\Addins\2021\
```

Тихая установка:

```text
DWGConverter-FamilyGeometry-Installer.exe /quiet
```

## AI

Новый режим `2D Drawing to Family` полностью исключает AI.

AI-конфигурация может использоваться только существующим 3D режимом, если пользователь явно включает соответствующую опцию. DWG/RFA в AI не отправляются.

## License

DWG Converter - Family Geometry is source-available, not open source.

The public repository is licensed under the PolyForm Noncommercial License 1.0.0 unless a separate written license is granted by the copyright holder.

Commercial use, including use in paid BIM/design/engineering workflows, redistribution, resale, sublicensing, or use in a competing product, requires a separate written commercial or beta license.

See [LICENSE.md](LICENSE.md).
