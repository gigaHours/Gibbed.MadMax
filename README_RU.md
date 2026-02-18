# Gibbed.MadMax

Набор инструментов на C# для работы с файлами игры Mad Max (2015, Avalanche Studios).

## Возможности

- **Распаковка** игровых архивов (`.tab`/`.arc` и `.sarc`)
- **Конвертация** бинарных форматов данных (ADF, свойства) в XML и обратно
- **Декомпиляция** байткода скриптов XVM в высокоуровневый Python-подобный исходный код `.xvm`
- **Компиляция** исходного кода `.xvm` обратно в байткод `.xvmc`
- **Дизассемблирование** байткода скриптов XVM в читаемый `.dis` ассемблер (с отладочной информацией)
- **Ассемблирование** `.dis` файлов обратно в `.xvmc` байткод (с поддержкой round-trip, включая debug_info)
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
| `Gibbed.MadMax.XvmDecompile` | Декомпиляция `.xvmc` байткода в высокоуровневый `.xvm` исходный код (пакетный режим для папок) |
| `Gibbed.MadMax.XvmCompile` | Компиляция `.xvm` исходного кода в `.xvmc` байткод (пакетный режим для папок) |
| `Gibbed.MadMax.XvmDisassemble` | Дизассемблирование `.xvmc` байткода в `.dis` текст |
| `Gibbed.MadMax.XvmAssemble` | Ассемблирование `.dis` текста обратно в `.xvmc` байткод |

### Прочее

| Инструмент | Описание |
|------------|----------|
| `RebuildFileLists` | Перестроение хеш-листов имён файлов |
| `Gibbed.Avalanche.ArchiveViewer` | GUI-просмотрщик архивов (Windows Forms) |

## Модификация скриптов XVM

Инструменты XVM предоставляют два уровня модификации скриптов:

### Высокоуровневый: Декомпиляция / Компиляция (рекомендуется)

```
.xvmc  -->  XvmDecompile  -->  .xvm  -->  [правка]  -->  XvmCompile  -->  .xvmc
```

1. **Декомпилировать** `.xvmc` в читаемый Python-подобный исходный код `.xvm`
2. **Отредактировать** файл `.xvm` — изменять логику в привычном высокоуровневом синтаксисе
3. **Скомпилировать** `.xvm` обратно в `.xvmc`

Оба инструмента поддерживают **пакетный режим**: перетащите папку на `.exe` (или передайте путь к каталогу) для рекурсивной обработки всех файлов `.xvmc`/`.xvm`.

Полный справочник скриптового языка XVM:
- **[Скриптовая система XVM (русский)](docs/XVM_RU.md)**
- **[XVM Scripting System (English)](docs/XVM.md)**

### Низкоуровневый: Дизассемблирование / Ассемблирование

```
.xvmc  -->  XvmDisassemble  -->  .dis  -->  [правка]  -->  XvmAssemble  -->  .xvmc
```

1. **Дизассемблировать** `.xvmc` в читаемый файл `.dis`
2. **Отредактировать** `.dis` — изменять отдельные инструкции байткода
3. **Ассемблировать** `.dis` обратно в `.xvmc`

Отладочная информация (`debug_info` и `debug_strings`) полностью сохраняется при round-trip.

Руководство по ассемблеру XVM:
- **[Руководство XVM Assembly (русский)](XVM_ASSEMBLY_GUIDE_RU.md)**
- **[XVM Assembly Guide (English)](XVM_ASSEMBLY_GUIDE.md)**

### Быстрый пример (высокоуровневый .xvm)

```python
module veh_player_input
import @58351C01

def PreInit(self):
    props = scriptgo.GetProperties(self)
    props.boost_enabled = true
    props.rearViewEnabled = false

def CheckBoost(self):
    props = scriptgo.GetProperties(self)
    if input.GetButtonInput(@3B91D694) > 0:
        if props.boost_enabled == true:
            vehicle.SetBoostInput(props.car, 1.0)

def DisableBoost(self):
    props = scriptgo.GetProperties(self)
    props.boost_enabled = false
```

### Быстрый пример (низкоуровневый .dis)

```asm
== MyFunction ==
; hash: 0xABCD1234  args: 2  locals: 3  max_stack: 5

    ldloc 1              ; загрузить цель @7:5
    ldattr "Health"      ; получить атрибут Health @7:18
    ldfloat 10           ; @8:5
    sub                  ; Health - 10 @8:12
    stloc 2              ; сохранить во временную @8:5

    ldloc 2              ; @10:5
    ldfloat 0            ; @10:12
    cmpg                 ; temp > 0 ? @10:9
    jz label_dead        ; @10:5

    ldloc 1              ; @11:5
    ldloc 2              ; @11:22
    stattr "Health"      ; target.Health = temp @11:5
    jmp label_end        ; @11:5

label_dead:
    ldloc 1              ; @13:5
    ldfloat 0            ; @13:22
    stattr "Health"      ; target.Health = 0 @13:5

label_end:
    ret 0                ; @15:5
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
+-- Gibbed.MadMax.XvmScript/           # Общая библиотека AST для XVM
+-- Gibbed.MadMax.XvmDecompile/        # Декомпилятор XVM (.xvmc -> .xvm)
+-- Gibbed.MadMax.XvmCompile/          # Компилятор XVM (.xvm -> .xvmc)
+-- Gibbed.MadMax.XvmDisassemble/      # Дизассемблер XVM (.xvmc -> .dis)
+-- Gibbed.MadMax.XvmAssemble/         # Ассемблер XVM (.dis -> .xvmc)
+-- Gibbed.Avalanche.ArchiveViewer/    # GUI-просмотрщик архивов
+-- RebuildFileLists/                  # Перестройка хеш-листов
+-- bin/                               # Результаты сборки
```

## Документация

- **[README (English)](README.md)** — README на английском
- **[README (русский)](README_RU.md)** — Этот файл
- **[Скриптовая система XVM (русский)](docs/XVM_RU.md)** — Полный справочник языка XVM, инструменты, архитектура байткода
- **[XVM Scripting System (English)](docs/XVM.md)** — Complete XVM language reference, toolchain, bytecode architecture
- **[Руководство XVM Assembly (русский)](XVM_ASSEMBLY_GUIDE_RU.md)** — Низкоуровневое руководство по XVM ассемблеру
- **[XVM Assembly Guide (English)](XVM_ASSEMBLY_GUIDE.md)** — Low-level XVM assembly guide
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
