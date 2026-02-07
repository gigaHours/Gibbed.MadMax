# XVM Assembler — План разработки

## Цель

Создать утилиту `Gibbed.MadMax.XvmAssemble`, которая принимает `.dis` файл (вывод дизассемблера) и собирает его обратно в `.xvmc` (ADF-контейнер с XVM-модулем). Полный round-trip: `.xvmc → .dis → .xvmc`.

---

## Формат входного файла (.dis)

На основе реального вывода дизассемблера:

```
; === XVM Module ===
; name: bullet_damage_handler
; name_hash: 0xC167E9CD
; source_hash: 0x3E12DA24
; flags: 0x0
; size: 0
; functions: 2
; constants: 39
; string_hashes: 36

== HitByBullet ==
; hash: 0xC9716879  args: 3  locals: 7  max_stack: 9
    0000: ldloc 0 ; arg0
    0001: ldglob "scriptgo"
    0002: ldattr "GetProperties"
    0003: call 1
    ...
label_20:
    0014: ldloc 2 ; arg2
    ...
    0046: ldbytes 17 21 32 F3
    ...
    0055: ret 0
```

### Токены, которые парсер должен понимать

| Токен | Пример | Описание |
|-------|--------|----------|
| Комментарий модуля | `; name: xxx` | Метаданные модуля (парсятся) |
| Комментарий строки | `; arg0` | Игнорируется при сборке |
| Заголовок функции | `== Name ==` | Имя функции |
| Метаданные функции | `; hash: 0x... args: N locals: N max_stack: N` | Параметры функции |
| Метка | `label_20:` | Цель перехода (резолвится в адрес) |
| Адрес | `0000:` | Игнорируется (пересчитывается) |
| Инструкция | `ldloc 0` | Опкод + операнд |

---

## Архитектура ассемблера

### Этап 1: Парсер (.dis → IR)

```
.dis текст
    ↓
[Lexer] → токены
    ↓
[Parser] → ModuleIR
    ↓ содержит:
    ├── ModuleInfo (name, hashes, flags)
    └── FunctionIR[]
         ├── name, hash, args, locals, max_stack
         ├── labels: Dictionary<string, int>
         └── InstructionIR[]
              ├── opcode: XvmOpcode
              ├── operand: (int | string | float | bytes | label_ref)
              └── source_line: int (для ошибок)
```

**Ключевые решения парсера:**

1. Адреса (`0000:`) при парсинге игнорируются — позиция определяется порядком инструкций
2. Комментарии (всё после `;`) отбрасываются
3. Метки сохраняются как имена, резолвятся на следующем этапе
4. Метаданные из заголовка модуля и функций парсятся из `;`-комментариев специального формата

### Этап 2: Построение таблиц (IR → Tables)

Из IR формируются:

#### 2.1 Таблица констант (Constants)

Каждая уникальная константа получает индекс. Источники:

| Инструкция | Что добавить в Constants |
|------------|--------------------------|
| `ldnone` | Constant { Type=0, Flags=0, Value=0 } |
| `ldfloat X` | Constant { Type=3, Value=float_bits(X) } |
| `ldstr "text"` | Constant { Type=4, Value=offset_in_StringBuffer } |
| `ldbytes XX YY` | Constant { Type=4, Value=offset_in_StringBuffer } |
| `ldattr "name"` | Constant { Type=4 } (строка-ключ) |
| `stattr "name"` | Constant { Type=4 } (строка-ключ) |
| `ldglob "name"` | Constant { Type=4 } (строка-модуль) |

**Дедупликация**: одинаковые константы не дублируются.

#### 2.2 StringBuffer

Буфер для хранения строковых данных. Для каждой строки:
- `[offset-3]` = индекс в StringHashes
- `[offset-2:offset-1]` = смещение в debug_strings (big-endian, 16 бит)
- `[offset:offset+len]` = сами байты строки

#### 2.3 StringHashes

Массив uint32 хешей всех строк (алгоритм: `hash = hash * 31 + byte`).

#### 2.4 Debug Strings (необязательно)

Если оригинал содержал debug_strings, собрать таблицу отладочных имён.

### Этап 3: Резолв меток

Двухпроходная сборка:

**Проход 1**: пронумеровать все инструкции, записать позиции меток.
```
label_20 → адрес 0x0014
label_31 → адрес 0x001F
```

**Проход 2**: заменить ссылки на метки реальными адресами.
```
jz label_20 → encode(Jz, 0x0014) → (0x0014 << 5) | 15
```

### Этап 4: Кодирование инструкций

Каждая инструкция → uint16:
```
encoded = (operand << 5) | opcode
```

Для инструкций с константами операнд = индекс в таблице Constants.
Для `ldloc`/`stloc` — индекс локальной переменной.
Для `call` — количество аргументов.
Для `jmp`/`jz` — адрес инструкции (из резолва меток).

### Этап 5: Сериализация в ADF

Собранный модуль упаковывается в ADF-контейнер:

1. Сериализовать `XvmModule` → бинарный блоб
2. (Опционально) сериализовать `debug_strings` → бинарный блоб
3. Упаковать в ADF:
   - Заголовок ADF (magic, version=4, таблицы)
   - InstanceInfo "module" → type_hash=0x41D02347
   - InstanceInfo "debug_strings" → type_hash=0xFEF3B589 (если есть)
   - Данные экземпляров

---

## Структура проекта

```
Gibbed.MadMax.XvmAssemble/
├── Program.cs              — точка входа, CLI
├── Lexer.cs                — токенизация .dis файла
├── Parser.cs               — парсинг токенов в IR
├── Assembler.cs            — построение таблиц + кодирование
├── AdfWriter.cs            — сериализация в ADF-контейнер
├── XvmModuleWriter.cs      — сериализация XvmModule
├── HashUtil.cs             — алгоритм хеширования строк
└── IR/
    ├── ModuleIR.cs         — промежуточное представление модуля
    ├── FunctionIR.cs       — промежуточное представление функции
    └── InstructionIR.cs    — промежуточное представление инструкции
```

---

## Зависимости от существующего кода

### Нужно реализовать (сейчас `NotImplementedException`):

1. **`XvmModule.Serialize()`** — запись модуля в поток. Обратная операция к `Deserialize()`. Нужно записать:
   - RawModule header (все поля с offset/count)
   - Functions (RawFunction headers + Instructions)
   - ImportHashes
   - Constants
   - StringHashes
   - StringBuffer
   - Name

2. **`AdfFile.Serialize()`** — запись ADF-контейнера. Обратная операция к `Deserialize()`. Нужно записать:
   - ADF header (magic, version, таблицы)
   - TypeDefinitions (пустые для xvmc)
   - InstanceInfos
   - Name table
   - Данные экземпляров

### Можно использовать как есть:

- `XvmOpcode` — enum опкодов
- `XvmModule.Constant` — структура константы
- `XvmModule.Function` — структура функции
- `Gibbed.IO` — все хелперы чтения/записи
- `NDesk.Options` — парсинг CLI
- `AdfFile.InstanceInfo` — метаданные экземпляра

---

## Порядок реализации (8 шагов)

### Шаг 1: Проект + HashUtil
Создать проект `Gibbed.MadMax.XvmAssemble`, добавить в solution.
Реализовать `HashUtil.HashString()`.

### Шаг 2: IR-модель
Классы `ModuleIR`, `FunctionIR`, `InstructionIR` — чистые данные без логики.

### Шаг 3: Lexer + Parser
Парсинг `.dis` → `ModuleIR`. Тест: парсим вывод дизассемблера, проверяем IR.

### Шаг 4: Assembler (таблицы + кодирование)
`ModuleIR` → `XvmModule` (заполненный). Включая:
- Построение Constants, StringBuffer, StringHashes
- Резолв меток → адреса
- Кодирование инструкций

### Шаг 5: XvmModule.Serialize()
Реализовать в `Gibbed.MadMax.FileFormats`. Записать все секции модуля.

### Шаг 6: AdfFile.Serialize()
Реализовать в `Gibbed.MadMax.FileFormats`. Упаковка в ADF-контейнер.

### Шаг 7: Program.cs (CLI)
Склеить: парсинг аргументов → парсинг .dis → сборка → запись .xvmc.

### Шаг 8: Верификация round-trip
```
original.xvmc → disasm → .dis → assemble → rebuilt.xvmc
```
Побайтовое сравнение `original.xvmc` и `rebuilt.xvmc`.

---

## Критичные нюансы

### 1. StringBuffer — нетривиальная сборка
Каждая строка в буфере имеет 3-байтный префикс:
```
[hash_index] [debug_offset_hi] [debug_offset_lo] [string_bytes...]
```
Ассемблер должен воспроизвести этот layout точно.

### 2. Constant.Flags кодирование
```
Flags = length | (allocated_length << 8) | (type << 16)
```
Для none: Flags=0, Value=0.
Для float: Flags=0x30000, Value=float_bits.
Для string: Flags=len | ((len | 0x400) << 8) | 0x40000, Value=offset.

### 3. Порядок констант
Для побайтового совпадения порядок констант в таблице должен совпадать с оригиналом. Дизассемблер ссылается на них по индексу (oparg), и этот порядок нужно сохранить. При сборке "с нуля" порядок определяется порядком первого появления в коде.

### 4. debug_strings round-trip
Если оригинальный файл содержал debug_strings, и в .dis есть строковые имена (в кавычках), нужно:
- Собрать таблицу debug_strings
- Записать смещения в StringBuffer
- Создать ADF instance "debug_strings"

Если debug_strings отсутствовали (в .dis хеши вместо имён), генерировать их не нужно.

### 5. Выравнивание
ADF может требовать выравнивание данных. Нужно проверить, как оригинальный файл выравнивает секции модуля.

---

## Тестовая стратегия

1. **Unit**: парсер на каждый тип инструкции отдельно
2. **Round-trip**: `disasm(original) → assemble → disasm(rebuilt)` — совпадение .dis файлов
3. **Binary round-trip**: побайтовое сравнение оригинала и пересобранного .xvmc
4. **Скрипты из игры**: прогнать все .xvmc из архивов через round-trip
