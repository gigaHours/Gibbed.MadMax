# Gibbed.MadMax

Набор инструментов на C# для работы с файлами игры Mad Max (2015, Avalanche Studios).

## Возможности

- **Распаковка** игровых архивов (`.tab`/`.arc` и `.sarc`)
- **Конвертация** бинарных форматов данных (ADF, свойства) в XML и обратно
- **Дизассемблирование** байткода скриптов XVM в читаемый `.dis` ассемблер
- **Ассемблирование** `.dis` файлов обратно в `.xvmc` байткод (с поддержкой round-trip)
- **Просмотр** содержимого архивов через GUI-приложение
- **Упаковка** файлов в архивы `.sarc`

## Сборка

### Требования

- Visual Studio 2022 (или совместимая среда)
- .NET Framework 4.8 SDK
- .NET Standard 2.0 SDK (для Gibbed.IO и NDesk.Options)

### Сборка

```bash
# Visual Studio
Открыть "Mad Max.sln" -> Сборка -> Собрать решение

# Командная строка
msbuild "Mad Max.sln" /p:Configuration=Release
```

Все скомпилированные файлы помещаются в каталог `bin/`.

## Инструменты

### Работа с архивами

| Инструмент | Описание |
|------------|----------|
| `Gibbed.MadMax.Unpack` | Распаковка больших архивов (`.tab` + `.arc`) |
| `Gibbed.MadMax.SmallUnpack` | Распаковка малых архивов (`.sarc`) |
| `Gibbed.MadMax.SmallPack` | Упаковка файлов в архивы `.sarc` |

### Конвертация

| Инструмент | Описание |
|------------|----------|
| `Gibbed.MadMax.ConvertAdf` | Конвертация ADF файлов (бинарный <-> XML) |
| `Gibbed.MadMax.ConvertProperty` | Конвертация контейнеров свойств (бинарный <-> XML) |
| `Gibbed.MadMax.ConvertSpreadsheet` | Конвертация табличных данных |

### Скрипты XVM

| Инструмент | Описание |
|------------|----------|
| `Gibbed.MadMax.XvmDisassemble` | Дизассемблирование `.xvmc` байткода в `.dis` текст |
| `Gibbed.MadMax.XvmAssemble` | Ассемблирование `.dis` текста обратно в `.xvmc` байткод |

### Прочее

| Инструмент | Описание |
|------------|----------|
| `RebuildFileLists` | Перестроение хеш-листов имён файлов |
| `Gibbed.Avalanche.ArchiveViewer` | GUI-просмотрщик архивов (Windows Forms) |

## Модификация скриптов XVM

Инструменты XVM обеспечивают полный цикл работы со скриптами игры:

```
.xvmc  -->  XvmDisassemble  -->  .dis  -->  [правка]  -->  XvmAssemble  -->  .xvmc
```

1. **Дизассемблировать** `.xvmc` файл, чтобы получить читаемый `.dis`
2. **Отредактировать** `.dis` — изменить логику, константы, добавить функции
3. **Ассемблировать** `.dis` обратно в `.xvmc`
4. **Проверить** — дизассемблировать новый `.xvmc` и сравнить (должно быть идентично)

Полное руководство по языку ассемблера XVM:
- **[Руководство XVM Assembly (русский)](XVM_ASSEMBLY_GUIDE_RU.md)**
- **[XVM Assembly Guide (English)](XVM_ASSEMBLY_GUIDE.md)**

### Быстрый пример

```asm
== MyFunction ==
; hash: 0xABCD1234  args: 2  locals: 3  max_stack: 5

    ldloc 1              ; загрузить цель
    ldattr "Health"      ; получить атрибут Health
    ldfloat 10
    sub                  ; Health - 10
    stloc 2              ; сохранить во временную

    ldloc 2
    ldfloat 0
    cmpg                 ; temp > 0 ?
    jz label_dead

    ldloc 1
    ldloc 2
    stattr "Health"      ; target.Health = temp
    jmp label_end

label_dead:
    ldloc 1
    ldfloat 0
    stattr "Health"      ; target.Health = 0

label_end:
    ret 0
```

## Форматы файлов игры

Mad Max использует движок **Apex Engine** с собственными бинарными форматами:

| Формат | Расширение | Описание |
|--------|------------|----------|
| ADF | `.adf`, `.xvmc` и др. | Arbitrary Data Format — универсальный типизированный контейнер данных |
| TAB/ARC | `.tab` + `.arc` | Большие игровые архивы (индекс + данные) |
| SARC | `.sarc` | Малые несжатые архивы |
| RTPC/BIN | `.rtpc`, `.bin` | Контейнеры свойств для конфигурации игровых объектов |
| XVMC | `.xvmc` | Скомпилированный байткод скриптов XVM (обёртка ADF) |

## Структура проекта

```
Gibbed.MadMax/
+-- Gibbed.IO/                          # Библиотека бинарного I/O
+-- Gibbed.ProjectData/                 # Метаданные проекта, хеш-листы
+-- Gibbed.MadMax.FileFormats/          # Парсеры форматов (ADF, TAB, SARC, XVM)
+-- Gibbed.MadMax.PropertyFormats/      # Форматы контейнеров свойств
+-- NDesk.Options/                      # Парсинг аргументов командной строки
+-- Gibbed.MadMax.Unpack/              # Распаковщик архивов
+-- Gibbed.MadMax.SmallUnpack/         # Распаковщик малых архивов
+-- Gibbed.MadMax.SmallPack/           # Упаковщик малых архивов
+-- Gibbed.MadMax.ConvertAdf/          # Конвертер ADF
+-- Gibbed.MadMax.ConvertProperty/     # Конвертер свойств
+-- Gibbed.MadMax.ConvertSpreadsheet/  # Конвертер таблиц
+-- Gibbed.MadMax.XvmDisassemble/      # Дизассемблер XVM
+-- Gibbed.MadMax.XvmAssemble/         # Ассемблер XVM
+-- Gibbed.Avalanche.ArchiveViewer/    # GUI-просмотрщик архивов
+-- RebuildFileLists/                  # Перестройка хеш-листов
+-- bin/                               # Результаты сборки
```

## Документация

- **[README (English)](README.md)** — README на английском
- **[README (русский)](README_RU.md)** — Этот файл
- **[Руководство XVM Assembly (русский)](XVM_ASSEMBLY_GUIDE_RU.md)** — Полное руководство по написанию XVM ассемблера
- **[XVM Assembly Guide (English)](XVM_ASSEMBLY_GUIDE.md)** — Полное руководство по XVM ассемблеру на английском
- **[Документация проекта (русский)](DOCUMENTATION_RU.md)** — Подробная техническая документация

## Лицензия

BSD 3-Clause License.

Copyright (c) 2015, Rick (rick 'at' gibbed 'dot' us)

Разрешено использование кем угодно для любых целей, включая коммерческие,
с правом модификации и свободного распространения.

Полный текст лицензии — в исходных файлах.

## Авторы

- **Rick** — Оригинальный автор
- **Sir Kane** — Участник
- **SK83RJOSH** — Участник
