namespace FamilyConverter.Revit2021.UI
{
    internal static class ProgressStatusTextProvider
    {
        public const int StageCount = 4;

        private static readonly string[] ActivePhrases =
        {
            "Ищем вирусы в Revit...",
            "Гуглим что такое Сдвиг...",
            "Уговариваем контуры замкнуться...",
            "Ускоряем ваш ПК..."
        };

        private static readonly string[] CompletePhrases =
        {
            "Выпрямляем полилинии...",
            "Гуглим, что такое выдавливание...",
            "Проверяем, не обиделся ли Extrusion...",
            "Почти готово, держим допуски в узде..."
        };

        private static readonly string[] DefaultDetails =
        {
            "Анализируем выбранный импорт и считаем типы геометрии.",
            "Извлекаем Solid, Mesh и Curve с текущими настройками.",
            "Создаем элементы Revit, применяем fallback и собираем отчеты.",
            "Финализируем результат и готовим окно отчета."
        };

        public static int NormalizeStage(int stage)
        {
            if (stage < 0)
            {
                return 0;
            }

            if (stage >= StageCount)
            {
                return StageCount - 1;
            }

            return stage;
        }

        public static string GetActivePhrase(int stage)
        {
            return ActivePhrases[NormalizeStage(stage)];
        }

        public static string GetCompletePhrase(int stage)
        {
            return CompletePhrases[NormalizeStage(stage)];
        }

        public static string GetDefaultDetail(int stage)
        {
            return DefaultDetails[NormalizeStage(stage)];
        }

        public static double GetActivePercent(int stage)
        {
            return 6.0 + NormalizeStage(stage) * 25.0;
        }

        public static double GetCompletePercent(int stage)
        {
            return (NormalizeStage(stage) + 1) * 25.0;
        }
    }
}
