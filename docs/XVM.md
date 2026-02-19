# XVM Scripting System - Mad Max

## Overview

XVM (eXtensible Virtual Machine) is the bytecode scripting engine used by Mad Max (Avalanche Studios, 2015). Game scripts are compiled into `.xvmc` files — ADF containers wrapping XVM bytecode modules. This toolkit provides full decompilation, compilation, disassembly, and assembly of XVM scripts.

The scripting language is Python-like: dynamically typed, indentation-based blocks, with access to engine globals like `vehicle`, `input`, `character`, etc.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Toolchain](#toolchain)
3. [XVM Language Reference](#xvm-language-reference)
4. [Compiler Directives](#compiler-directives)
5. [Engine Globals](#engine-globals)
6. [Bytecode Architecture](#bytecode-architecture)
7. [ADF Container Format](#adf-container-format)
8. [Project Structure](#project-structure)
9. [Modding Workflow](#modding-workflow)
10. [Known Differences](#known-differences)
11. [Roadmap](#roadmap)

---

## Quick Start

### Decompile a script
```
Gibbed.MadMax.XvmDecompile.exe script.xvmc script.xvm
```

### Compile a script
```
Gibbed.MadMax.XvmCompile.exe script.xvm script.xvmc
```

### Disassemble to bytecode listing
```
Gibbed.MadMax.XvmDisassemble.exe script.xvmc script.dis
```

### Assemble from bytecode listing
```
Gibbed.MadMax.XvmAssemble.exe script.dis script.xvmc
```

### Full round-trip example
```bash
# 1. Extract .xvmc from game archives
# 2. Decompile to readable source
Gibbed.MadMax.XvmDecompile.exe veh_player_input.xvmc veh_player_input.xvm

# 3. Edit the .xvm source file

# 4. Compile back to bytecode
Gibbed.MadMax.XvmCompile.exe veh_player_input.xvm veh_player_input.xvmc

# 5. Place in dropzone/scripts/gameobjects/vehicle/ to mod the game
```

---

## Toolchain

### Gibbed.MadMax.XvmDecompile

Converts `.xvmc` bytecode to high-level `.xvm` source code.

```
Usage: Gibbed.MadMax.XvmDecompile.exe [OPTIONS] input_xvmc_or_directory [output.xvm]

Options:
  --hashes    Emit #! hash/source_hash directives for exact round-trip
  -h, --help  Show help
```

**Batch mode:** If a directory is passed instead of a file, all `.xvmc` files are decompiled recursively. You can drag-and-drop a folder onto the `.exe` in Windows Explorer.

**Without `--hashes` (default):** Produces clean, readable source code. Function hashes are auto-computed from names during recompilation. Suitable for most modding use cases.

**With `--hashes`:** Emits `#! hash:` and `#! source_hash:` directives to preserve original binary hashes. Required when a function's hash in the original binary doesn't match Jenkins hash of its name (rare, happens when Avalanche renamed functions without recompiling).

### Gibbed.MadMax.XvmCompile

Converts `.xvm` source code to `.xvmc` bytecode.

```
Usage: Gibbed.MadMax.XvmCompile.exe [OPTIONS] input.xvm [output.xvmc]

Options:
  -g, --globals=PATH  Path to xvm_globals.txt (default: next to exe)
  -h, --help          Show help
```

**Features:**
- Auto-computes function name hashes via Jenkins lookup3
- Auto-computes module `source_hash` from source text if not specified
- Auto-computes `max_stack` via dataflow analysis
- Auto-computes `locals` count from variable usage
- Generates debug_info (line:col per instruction)
- Constant folding for negative float literals (`-1.0` as single constant)

### Gibbed.MadMax.XvmDisassemble

Converts `.xvmc` to low-level `.dis` assembly listing showing individual bytecode instructions.

```
Usage: Gibbed.MadMax.XvmDisassemble.exe input.xvmc [output.dis]
```

### Gibbed.MadMax.XvmAssemble

Converts `.dis` assembly listing back to `.xvmc` bytecode.

```
Usage: Gibbed.MadMax.XvmAssemble.exe input.dis [output.xvmc]
```

---

## XVM Language Reference

XVM source (`.xvm` files) uses a Python-like syntax with significant whitespace.

### Module Declaration

```python
module my_script
import @58351C01
import @3BDB5A3B
```

- `module <name>` — declares the module name (used for hash computation)
- `import @XXXXXXXX` — imports a dependency module by hash

### Functions

```python
def FunctionName(self, arg1, arg2):
    # function body
    return value
```

- First parameter is conventionally `self` (the game object instance)
- All functions receive arguments via positional parameters
- Functions without explicit `return` implicitly return nothing

### Data Types

| Type | Syntax | Example |
|------|--------|---------|
| Float | Number with optional decimal | `0`, `1.5`, `-3.14`, `0.001` |
| Bool | `true` / `false` | `true` |
| String | Double-quoted | `"hello"` |
| None | `none` | `none` |
| Bytes | `@` + hex digits | `@3B91D694` |
| List | Square brackets | `[1, 2, 3]` |

**Note:** XVM has no integer type. All numbers are 32-bit floats. The value `0` is `0.0`, `1` is `1.0`, etc.

**Bytes literals** (`@XXXXXXXX`) are typically used for hashed event/property names that the engine resolves at runtime.

### Variables

Variables are dynamically typed and don't need declarations. First assignment creates the variable:

```python
def Example(self):
    local1 = 42            # creates local variable
    local2 = "hello"       # another local
    local1 = local2        # reassignment
```

### Operators

**Arithmetic:** `+`, `-`, `*`, `/`, `%`

**Comparison:** `==`, `!=`, `>`, `>=`

**Logical:** `and`, `or`, `not`

**Unary:** `-` (negation)

```python
result = (a + b) * 2
is_valid = x > 0 and y >= 0
flag = not condition
```

### Control Flow

**if / elif / else:**
```python
if condition:
    do_something()
elif other_condition:
    do_other()
else:
    do_default()
```

**while / break:**
```python
while condition:
    do_work()
    if done:
        break
```

**return:**
```python
def Compute(self, x):
    if x > 0:
        return x * 2
    return -1
```

**pass:**
```python
def Placeholder(self):
    pass
```

**assert:**
```python
assert value > 0
```

### Attribute Access

```python
props = scriptgo.GetProperties(self)
props.rearViewEnabled = true
value = props.boost_enabled
```

### Index Access

```python
list = [1, 2, 3]
item = list[0]
list[1] = 42
```

### Method Calls

```python
# Engine API calls
result = vehicle.IsPlayerVehicle(car)
input.GetButtonInput(@3B91D694)

# Module function calls
DisableBoost(self)

# Method chaining
character.GetPlayer().GetName()
```

### Comments

```python
# This is a comment
x = 5  # inline comment
```

---

## Compiler Directives

Compiler directives use `#!` prefix and are processed before lexing. They are only recognized at zero indentation (top-level).

### source_hash

```python
module my_script
#! source_hash: 0x088AECE6
```

Forces the module's `source_hash` field to a specific value. If omitted, the compiler auto-computes it via Jenkins hash of the source text.

### hash (function)

```python
#! hash: 0xBE988D2F
def DisableBoost(self):
    # ...
```

Forces the function's name hash to a specific value. If omitted, the compiler auto-computes it via Jenkins hash of the function name.

**When to use:** Only needed when a function's original hash doesn't match `Jenkins(name)`. This is rare and happens when the original Avalanche compiler used a different name that was later changed in debug strings. Use `--hashes` flag during decompilation to detect and preserve these cases.

---

## Engine Globals

XVM scripts access engine functionality through global objects. The compiler needs to know which identifiers are globals (loaded via `ldglob`) versus local variables (loaded via `ldloc`).

Globals are defined in `xvm_globals.txt` (placed next to the compiler executable, or specified via `--globals`):

```
# xvm_globals.txt

# Core engine
scriptgo        # Script runtime, GetProperties(), event management
game            # Game state, IsObjectEqualTo(), etc.
input           # Input system, GetButtonInput(), GetAnalogInput()
gui             # GUI system

# Entity systems
character       # Character management, GetPlayer()
vehicle         # Vehicle system, IsPlayerVehicle(), GetVehicleDriver()
animation       # Animation state machine, GetStateBit()
physics         # Physics engine

# Math / utility
math            # Math functions
vector          # Vector operations

# UI / camera / audio
ui              # UI system
camera          # Camera control
sound           # Audio system
hud             # HUD elements

# World / network / timer
world           # World management
network         # Network/multiplayer
timer           # Timer system
debug           # Debug output

# Library modules (script-side globals)
lib_entity_proxy
lib_player_input
lib_vehicle
```

### Common API Patterns

```python
# Get script properties for a game object
props = scriptgo.GetProperties(self)

# Check button input by hash
if input.GetButtonInput(@3B91D694) > 0:
    # button pressed

# Vehicle operations
if vehicle.IsPlayerVehicle(props.car):
    driver = vehicle.GetVehicleDriver(props.car)

# Player checks
player = character.GetPlayer()
if game.IsObjectEqualTo(player, driver):
    # player is driving

# Animation state
state = animation.GetStateBit(anim_obj, 10.0)
```

---

## Bytecode Architecture

### Instruction Format

XVM uses 16-bit instructions:
- **Bits 15-11:** Opcode (5 bits, 0-31)
- **Bits 10-0:** Operand (11 bits, 0-2047)

```
[OOOOO|FFFFFFFFFFF]
  op     operand
```

### Stack Machine

XVM is a stack-based VM. Operations push/pop values on an evaluation stack.

### Opcodes

| Code | Mnemonic | Operand | Stack Effect | Description |
|------|----------|---------|-------------|-------------|
| 0 | `assert` | — | -1 | Assert TOS is truthy |
| 1 | `and` | — | -1 | Logical AND: pop 2, push result |
| 2 | `or` | — | -1 | Logical OR: pop 2, push result |
| 3 | `add` | — | -1 | Addition: pop 2, push sum |
| 4 | `div` | — | -1 | Division: pop 2, push quotient |
| 5 | `mod` | — | -1 | Modulo: pop 2, push remainder |
| 6 | `mul` | — | -1 | Multiplication: pop 2, push product |
| 7 | `sub` | — | -1 | Subtraction: pop 2, push difference |
| 8 | `mklist N` | count | -(N-1) | Pop N items, push list |
| 9 | `call N` | argc | -N | Pop callable + N args, push result |
| 10 | `cmpeq` | — | -1 | Equal: pop 2, push bool |
| 11 | `cmpge` | — | -1 | Greater-equal: pop 2, push bool |
| 12 | `cmpg` | — | -1 | Greater: pop 2, push bool |
| 13 | `cmpne` | — | -1 | Not-equal: pop 2, push bool |
| 14 | `jmp` | target | 0 | Unconditional jump |
| 15 | `jz` | target | -1 | Jump if TOS is falsy |
| 18 | `ldattr` | const | 0 | Load attribute: pop obj, push obj.attr |
| 19 | `ldconst` | const | +1 | Push constant (float/string/bytes/none) |
| 20 | `ldbool N` | 0/1 | +1 | Push boolean |
| 21 | `ldglob` | const | +1 | Push global by name |
| 22 | `ldloc N` | slot | +1 | Push local variable |
| 23 | `lditem` | — | -1 | Index access: pop obj+idx, push obj[idx] |
| 24 | `pop` | — | -1 | Discard TOS |
| 25 | `dbgout N` | — | variable | Debug output |
| 26 | `ret N` | count | -N | Return (0=void, 1=with value) |
| 27 | `stattr` | const | -2 | Store attribute: pop obj+val, set obj.attr=val |
| 28 | `stloc N` | slot | -1 | Store local variable |
| 29 | `stitem` | — | -3 | Index store: pop obj+idx+val, set obj[idx]=val |
| 30 | `not` | — | 0 | Logical NOT: replace TOS |
| 31 | `neg` | — | 0 | Arithmetic negate: replace TOS |

**Note:** Opcodes 16 and 17 are unused/reserved.

### Calling Convention

```
push arg0          # first argument
push arg1          # second argument
push callable      # function/method reference
call 2             # call with 2 arguments
```

For method calls (`obj.method(args)`):
```
push arg0
push obj
ldattr "method"    # resolve method
call 1             # call with 1 argument
```

### Store Attribute Convention

```
push value         # value to store
push obj           # target object
stattr "name"      # obj.name = value
```

### Store Item Convention

```
push value         # value to store
push obj           # target object (list/array)
push index         # index into the object
stitem             # obj[index] = value
```

### Constant Table

Constants are referenced by operand index. Types:
- **Float:** 32-bit IEEE 754
- **String:** UTF-8 text with Jenkins hash
- **Bytes:** Raw byte sequences (typically hashed names)
- **None:** Singleton null value

### String Buffer

A byte buffer storing string data and attribute name metadata. Each entry includes:
- Hash index (reference into StringHashes table)
- Debug string offsets (for debug builds)
- Actual string bytes (null-terminated)

### Hash Algorithm

XVM uses **Jenkins lookup3** (`hashlittle`) with seed=0 for all name hashing:
- Module name hash
- Function name hashes
- String constant hashes
- Attribute/global name hashes

---

## ADF Container Format

`.xvmc` files use the ADF (Avalanche Data Format) container:

### ADF Header (0x40 bytes)

| Offset | Size | Field |
|--------|------|-------|
| 0x00 | 4 | Signature: `0x41444620` ("ADF ") |
| 0x04 | 4 | Version: `4` |
| 0x08 | 4 | Instance count |
| 0x0C | 4 | Instance info offset |
| 0x10 | 4 | Type definition count (0 for XVM) |
| 0x14 | 4 | Type definition offset |
| 0x18 | 8 | Reserved (0) |
| 0x20 | 4 | Name table count |
| 0x24 | 4 | Name table offset |
| 0x28 | 4 | Total size |
| 0x2C | 20 | Reserved (0) |

### ADF Instances

Each `.xvmc` contains 3 instances:

| Instance | Type Hash | Description |
|----------|-----------|-------------|
| `module` | `0x41D02347` | XVM bytecode module |
| `debug_info` | `0xDCB06466` | Per-instruction line/column numbers |
| `debug_strings` | `0xFEF3B589` | Debug string table |

### Instance Info Entry (24 bytes)

| Offset | Size | Field |
|--------|------|-------|
| 0x00 | 4 | Name hash |
| 0x04 | 4 | Type hash |
| 0x08 | 4 | Data offset |
| 0x0C | 4 | Data size |
| 0x10 | 8 | Name index |

---

## Project Structure

```
Gibbed.MadMax/
  Gibbed.MadMax.FileFormats/     # Core format definitions
    XvmModule.cs                 #   XVM module reader/data model
    XvmOpcode.cs                 #   Opcode enum (0-31)
    AdfFile.cs                   #   ADF container reader
    StringHelpers.cs             #   Jenkins lookup3 hash implementation

  Gibbed.MadMax.XvmScript/       # Shared AST library
    Ast.cs                       #   AST node hierarchy (Expr, Stmt, Module)
    AstPrinter.cs                #   AST -> source code printer

  Gibbed.MadMax.XvmDisassemble/  # Bytecode disassembler (.xvmc -> .dis)
    Program.cs                   #   CLI entry point

  Gibbed.MadMax.XvmAssemble/     # Bytecode assembler (.dis -> .xvmc)
    DisParser.cs                 #   .dis file parser -> ParsedModule IR
    Assembler.cs                 #   IR -> XvmModule (constant table, labels)
    XvmModuleWriter.cs           #   XvmModule binary serialization
    AdfWriter.cs                 #   ADF container writer
    HashUtil.cs                  #   Hash utility wrapper

  Gibbed.MadMax.XvmDecompile/    # Decompiler (.xvmc -> .xvm)
    Program.cs                   #   CLI entry point
    InstructionDecoder.cs        #   Raw bytecode -> decoded instructions
    CfgBuilder.cs                #   Control flow graph builder
    ExpressionRecovery.cs        #   Stack simulation -> AST expressions
    StructuralAnalysis.cs        #   CFG -> if/while/else recovery

  Gibbed.MadMax.XvmCompile/      # Compiler (.xvm -> .xvmc)
    Program.cs                   #   CLI entry point + directive pre-parsing
    Lexer.cs                     #   Source -> tokens (indent-based)
    Token.cs                     #   Token types
    Parser.cs                    #   Tokens -> AST (recursive descent)
    SemanticAnalysis.cs          #   Variable scope resolution
    CodeGenerator.cs             #   AST -> assembler IR (ParsedModule)

  bin/
    xvm_globals.txt              # Engine globals definition file
```

### Compilation Pipeline

```
.xvm source
    |
    v
[Lexer] -> tokens
    |
    v
[Parser] -> AST (ScriptModule)
    |
    v
[SemanticAnalysis] -> variable scopes
    |
    v
[CodeGenerator] -> ParsedModule (assembler IR)
    |
    v
[Assembler] -> XvmModule + debug data
    |
    v
[AdfWriter] -> .xvmc binary
```

### Decompilation Pipeline

```
.xvmc binary
    |
    v
[AdfFile] -> ADF instances
    |
    v
[XvmModule.Deserialize] -> module + functions + constants
    |
    v
[InstructionDecoder] -> decoded instructions
    |
    v
[CfgBuilder] -> basic blocks + edges
    |
    v
[ExpressionRecovery] -> AST expressions per block
    |
    v
[StructuralAnalysis] -> if/while/else recovery
    |
    v
[AstPrinter] -> .xvm source
```

---

## Modding Workflow

### Installing Mods

Mad Max supports the `dropzone` folder for file overrides:

```
<Mad Max Install>/dropzone/scripts/gameobjects/vehicle/veh_player_input.xvmc
```

Files in `dropzone/` override the corresponding files from the game archives.

### Typical Workflow

1. **Extract** the `.xvmc` file from game archives using `Gibbed.MadMax.SmallUnpack`
2. **Decompile** to `.xvm` source:
   ```
   Gibbed.MadMax.XvmDecompile.exe original.xvmc script.xvm
   ```
3. **Edit** the `.xvm` file with any text editor
4. **Compile** back:
   ```
   Gibbed.MadMax.XvmCompile.exe script.xvm modified.xvmc
   ```
5. **Place** the `.xvmc` in the appropriate `dropzone/` subdirectory

### Tips

- Use `--hashes` when decompiling if you need exact binary reproduction
- The `xvm_globals.txt` file must be next to the compiler executable, or specified with `--globals`
- After compiling, you can verify with the disassembler:
  ```
  Gibbed.MadMax.XvmDisassemble.exe modified.xvmc check.dis
  ```
- The compiler generates debug_info automatically — the game uses it for error reporting

---

## Known Differences

When round-tripping through decompile/compile, the following non-functional differences may occur compared to the original binary:

| Aspect | Original | Round-tripped | Impact |
|--------|----------|---------------|--------|
| Constant ordering | Binary order | First-encounter order | None |
| StringBuffer layout | Overlapping entries | Non-overlapping | None (smaller file) |
| max_stack | Over-estimated | Precisely computed | None (game needs >= actual) |
| String hashes count | Includes overlap artifacts | Only used hashes | None |
| source_hash | Original value | Jenkins(source text) | None (unless `#! source_hash:` specified) |

All differences are **functionally equivalent** — the game behavior is identical.

---

## Roadmap

### Planned: VSCode Extension

A Visual Studio Code extension for XVM development is planned with the following features:

**Syntax Highlighting**
- Full grammar for `.xvm` files
- Keyword highlighting (`def`, `if`, `elif`, `else`, `while`, `break`, `return`, `assert`, `pass`, `and`, `or`, `not`, `true`, `false`, `none`, `module`, `import`)
- String, number, bytes literal, and comment highlighting
- `#!` directive highlighting

**Quick Compilation**
- Compile `.xvm` to `.xvmc` directly from the editor
- Keybinding for one-click compile (e.g. `Ctrl+Shift+B`)
- Inline error display from compiler output
- Auto-deploy to `dropzone/` folder

**Built-in API Reference**
- IntelliSense/autocomplete for engine globals and their methods:
  - `scriptgo.GetProperties()`, `scriptgo.SetProperties()`, ...
  - `vehicle.IsPlayerVehicle()`, `vehicle.GetVehicleDriver()`, ...
  - `input.GetButtonInput()`, `input.GetAnalogInput()`, ...
  - `character.GetPlayer()`, ...
  - `animation.GetStateBit()`, ...
  - `game.IsObjectEqualTo()`, ...
- Hover documentation for API methods
- Parameter hints and type information
- Global attribute completions (e.g., `props.rearViewEnabled`, `props.boost_enabled`)

**Extended `xvm_globals.txt` Format**
Planned enhancement to support method and attribute definitions:

```
# Future xvm_globals.txt format
[scriptgo]
  .GetProperties(obj) -> Properties
  .SetProperties(obj, props)

[vehicle]
  .IsPlayerVehicle(car) -> bool
  .GetVehicleDriver(car) -> Entity
  .GetVehiclePassenger(car, seat) -> Entity

[input]
  .GetButtonInput(hash) -> float
  .GetAnalogInput(hash) -> float

[character]
  .GetPlayer() -> Entity

[game]
  .IsObjectEqualTo(a, b) -> bool

[animation]
  .GetStateBit(obj, bit) -> float
```

This will enable the compiler to validate method calls and provide meaningful error messages for incorrect API usage.
