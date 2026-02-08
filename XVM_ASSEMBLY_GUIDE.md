# XVM Assembly — Guide to Writing and Editing Scripts

## Table of Contents

1. [Overview](#overview)
2. [.dis File Format](#dis-file-format)
3. [Module Header](#module-header)
4. [Function Declarations](#function-declarations)
5. [Instructions — Complete Reference](#instructions--complete-reference)
6. [Labels and Jumps](#labels-and-jumps)
7. [Stack Machine — How XVM Works](#stack-machine--how-xvm-works)
8. [Data Types](#data-types)
9. [Comments](#comments)
10. [Practical Examples](#practical-examples)
11. [Workflow: Disassemble, Edit, Assemble](#workflow-disassemble-edit-assemble)
12. [Limitations](#limitations)
13. [Common Mistakes](#common-mistakes)

---

## Overview

**XVM** (Apex eXtensible Virtual Machine) is the scripting virtual machine of the Apex Engine
used in the game Mad Max (2015). Scripts are compiled into `.xvmc` bytecode and control
game logic: damage handling, AI behavior, UI, vehicles, etc.

**Toolchain:**

```
.xvmc (binary)  ──XvmDisassemble──>  .dis (text)
                                          |
                                      [editing]
                                          |
.xvmc (binary)  <──XvmAssemble─────  .dis (text)
```

The `.dis` file is a human-readable assembly format that can be opened and edited
in any text editor.

---

## .dis File Format

A `.dis` file consists of three parts:

```
+-------------------------+
|   Module header         |  ; name: ..., ; name_hash: ..., etc.
+-------------------------+
|   Function 1            |  == FuncName ==  + instructions
+-------------------------+
|   Function 2            |  == FuncName ==  + instructions
+-------------------------+
|   ...                   |
+-------------------------+
```

---

## Module Header

The module header consists of comment lines at the beginning of the file:

```asm
; === XVM Module ===
; name: bullet_damage_handler
; name_hash: 0xC167E9CD
; source_hash: 0x3E12DA24
; flags: 0x0
; size: 0
; functions: 2
; constants: 39
; string_hashes: 36
```

### Required Fields

| Field | Format | Description |
|-------|--------|-------------|
| `name` | string | Module name (for debugging) |
| `name_hash` | `0xHEX` | Hash of the module name (**optional** — auto-computed from `name`) |
| `source_hash` | `0xHEX` | Hash of the original source file |
| `flags` | `0xHEX` | Module flags (usually `0x0`) |
| `size` | number | Module size (usually `0`) |

### Informational Fields (ignored by assembler)

| Field | Description |
|-------|-------------|
| `functions` | Number of functions (computed automatically) |
| `constants` | Number of constants (computed automatically) |
| `string_hashes` | Number of string hashes (computed automatically) |

> **Tip:** `name_hash` is optional — if omitted, the assembler computes it
> automatically from the module name using Jenkins hash. When creating a new module
> from scratch, you only need to provide `name` and `source_hash`.

---

## Function Declarations

Each function begins with a header and metadata:

```asm
== HitByBullet ==
; hash: 0xC9716879  args: 3  locals: 7  max_stack: 9
```

### Function Header

```
== FunctionName ==
```

The function name is an arbitrary identifier. It is used for debugging and
for calling from other scripts via `ldglob`.

### Function Metadata

```
; hash: 0xHASH  args: N  locals: N  max_stack: N
```

| Field | Description |
|-------|-------------|
| `hash` | Hash of the function name (**optional** — auto-computed from name) |
| `args` | Number of function arguments |
| `locals` | Total number of local variables (**auto-computed** — value in .dis is ignored) |
| `max_stack` | Maximum stack depth (**auto-computed** — value in .dis is ignored) |

> **Important:** `locals` includes arguments! If a function takes 3 arguments
> and uses 4 local variables, then `locals = 7`.
>
> Local variables 0..args-1 are arguments, args..locals-1 are locals.

### Automatic hash, locals, and max_stack

**hash** — if omitted, the assembler automatically computes the Jenkins hash of
the function name. For example, `HashJenkins("PreInit")` = `0xAD92A91E`. This means
you can write new functions without manually computing hashes.

**locals** — always computed automatically. The assembler scans all `ldloc`/`stloc`
instructions to find the highest variable index used, then sets
`locals = max(highestIndex + 1, args)`. Any `locals` value in the `.dis` file is ignored.

**max_stack** — always computed automatically using forward dataflow analysis.
The assembler traces all execution paths (branches, loops) and determines the precise
peak stack depth. Any `max_stack` value in the `.dis` file is ignored.

This means the minimal function metadata is just the argument count:

```asm
== MyNewFunction ==
; args: 2
```

---

## Instructions — Complete Reference

Each instruction is written on a separate line:

```
    ADDR: mnemonic [operand]
```

The address `ADDR` is a four-digit hex value (e.g. `0000:`, `001F:`) — it is optional,
the assembler ignores it. You can write just the mnemonic:

```asm
    ldloc 0
    ldglob "scriptgo"
    ldattr "GetProperties"
    call 1
    stloc 3
```

### Debug Annotations (@line:col)

Instructions may have a `@line:col` annotation at the end of the line (after any comments):

```asm
    ldloc 0 ; arg0 @7:42
    ldglob "scriptgo" @7:52
    ldattr "GetProperties" @7:63
```

These annotations record the source line and column from the original script and are
part of the `debug_info` ADF instance. The disassembler outputs them automatically when
the `.xvmc` contains `debug_info`. The assembler reads them back to rebuild `debug_info`.

- Format: `@LINE:COL` where LINE and COL are decimal numbers (uint16)
- Position: at the very end of the line, after the comment
- Optional: if omitted or all zeros, no `debug_info` instance is generated

### Group 1: Loading Values onto the Stack

| Mnemonic | Operand | Stack | Description |
|----------|---------|-------|-------------|
| `ldloc N` | index | -> val | Load local variable / argument |
| `ldfloat X` | number | -> float | Load floating-point number |
| `ldstr "text"` | string | -> string | Load string constant |
| `ldbool N` | 0 or 1 | -> bool | Load boolean value |
| `ldnone` | — | -> none | Load None |
| `ldbytes XX YY..` | hex bytes | -> bytes | Load raw bytes (event hash, etc.) |
| `ldglob "name"` | string | -> val | Load global object |
| `ldattr "name"` | string | val -> val | Load object attribute |
| `lditem` | — | obj, idx -> val | Load element by index |

### Group 2: Storing Values

| Mnemonic | Operand | Stack | Description |
|----------|---------|-------|-------------|
| `stloc N` | index | val -> | Store to local variable |
| `stattr "name"` | string | obj, val -> | Set object attribute |
| `stitem` | — | obj, idx, val -> | Set element by index |

### Group 3: Arithmetic and Comparisons

| Mnemonic | Stack | Description |
|----------|-------|-------------|
| `add` | a, b -> result | Addition (a + b) |
| `sub` | a, b -> result | Subtraction (a - b) |
| `mul` | a, b -> result | Multiplication (a * b) |
| `div` | a, b -> result | Division (a / b) |
| `mod` | a, b -> result | Modulo (a % b) |
| `neg` | a -> result | Unary negation (-a) |
| `cmpeq` | a, b -> bool | Equal (a == b) |
| `cmpne` | a, b -> bool | Not equal (a != b) |
| `cmpg` | a, b -> bool | Greater than (a > b) |
| `cmpge` | a, b -> bool | Greater or equal (a >= b) |

### Group 4: Logical Operations

| Mnemonic | Stack | Description |
|----------|-------|-------------|
| `and` | a, b -> bool | Logical AND |
| `or` | a, b -> bool | Logical OR |
| `not` | a -> bool | Logical NOT |

### Group 5: Control Flow

| Mnemonic | Operand | Stack | Description |
|----------|---------|-------|-------------|
| `jmp label_N` | label | — | Unconditional jump |
| `jz label_N` | label | val -> | Jump if value is falsy (0 / false / None) |
| `call N` | arg count | obj, arg1..argN -> result | Call method/function |
| `ret N` | 0 or 1 | [val] -> | Return from function (0 = no value, 1 = with value) |

### Group 6: Miscellaneous

| Mnemonic | Operand | Stack | Description |
|----------|---------|-------|-------------|
| `pop` | — | val -> | Remove top value from stack |
| `mklist N` | count | elem1..elemN -> list | Create list from N elements |
| `assert` | — | val -> | Assertion check (crashes if false) |
| `dbgout N` | number | val -> | Debug output |

---

## Labels and Jumps

Labels define jump targets for `jmp` and `jz` instructions:

```asm
    ldloc 4
    ldfloat 1
    cmpeq
    jz label_skip         ; if not equal — jump to label_skip

    ; this code executes if equal
    ldfloat 42
    stloc 5

label_skip:
    ; continuation — both paths reach here
    ldloc 5
    ret 1
```

### Label Rules

- Label name is arbitrary, ends with a colon `:`
- Convention: `label_XX` where XX is the target instruction address
- Labels must be on their own line
- Labels do not occupy an instruction slot (do not increment the address counter)
- Labels are scoped to their function

```
label_10:              <-- label definition (not an instruction)
    0007: ldloc 3      <-- instruction at address 7
```

---

## Stack Machine — How XVM Works

XVM is a **stack-based** virtual machine. All operations work through the stack:

```
Example: computing (a + b) * c

    ldloc 0        ; stack: [a]
    ldloc 1        ; stack: [a, b]
    add            ; stack: [a+b]
    ldloc 2        ; stack: [a+b, c]
    mul            ; stack: [(a+b)*c]
    stloc 3        ; stack: []  -> result stored in variable 3
```

### Method Calls

Method calls follow a specific pattern. First, push arguments onto the stack,
then the object and method, then `call N`:

```asm
; Equivalent Python: game.SetHitByPlayer(target)

    ldloc 1              ; stack: [target]         — argument
    ldglob "game"        ; stack: [target, game]   — object with method
    ldattr "SetHitByPlayer" ; stack: [target, game.SetHitByPlayer] — method
    call 1               ; stack: [result]          — call with 1 argument
    pop                   ; stack: []               — discard result
```

> **Important:** Stack order — arguments are pushed BEFORE the object/method.
> `call N` pops: the callable + N arguments, and pushes the result.

### Calls with Multiple Arguments

```asm
; Equivalent: vehicle.GetPartByShapeKey(target, shapeKey)

    ldloc 1              ; stack: [target]          — arg1
    ldloc 2              ; stack: [target, shapeKey] — arg2 (from attribute)
    ldattr "ShapeKey"    ; stack: [target, shapeKey_val]
    ldglob "vehicle"     ; stack: [target, shapeKey_val, vehicle]
    ldattr "GetPartByShapeKey"  ; stack: [target, shapeKey_val, method]
    call 2               ; stack: [result]
    stloc 5              ; stack: []  -> result in variable 5
```

### Reading an Attribute (Property)

```asm
; Equivalent: x = obj.SomeProperty

    ldloc 0              ; stack: [obj]
    ldattr "SomeProperty" ; stack: [obj.SomeProperty]  — ldattr replaces the top
    stloc 1              ; stack: []  -> x = result
```

> **ldattr** — pops the object from the top of the stack, pushes the value of its attribute.
> This is NOT an addition to the stack, but a REPLACEMENT of the top element.

---

## Data Types

### Floating-Point Numbers (float)

```asm
    ldfloat 0           ; 0.0
    ldfloat 1           ; 1.0
    ldfloat 3.14        ; 3.14
    ldfloat -5          ; -5.0
    ldfloat 0.001       ; 0.001
```

XVM uses float everywhere — including for integers (0, 1, 2...).
There is no separate integer type for values.

### Strings

```asm
    ldstr "Hello World"           ; regular string
    ldstr "Line1\nLine2"          ; with escape sequences
    ldstr "She said \"hi\""       ; with quotes
    ldstr "Tab\there"             ; with tab
```

Supported escape sequences: `\"`, `\\`, `\n`, `\r`, `\t`.

### Boolean Values

```asm
    ldbool 0             ; false
    ldbool 1             ; true
```

### None

```asm
    ldnone               ; None / null
```

### Raw Bytes

```asm
    ldbytes 17 21 32 F3  ; 4 bytes in hex
```

Used for event hashes, identifiers, etc.

### Global Objects

Built-in engine global objects (accessible via `ldglob`):

| Object | Description |
|--------|-------------|
| `"game"` | Game context |
| `"vehicle"` | Current vehicle |
| `"physics"` | Physics engine |
| `"debug"` | Debug output |
| `"scriptgo"` | Script game object |

---

## Comments

Comments start with `;` and continue to the end of the line:

```asm
    ldloc 0        ; load first argument (self)
    ldloc 1        ; arg1 — hit target
    ldfloat 5      ; ammunition type: sniper
```

Comments inside strings are not processed:

```asm
    ldstr "Hello ; World"   ; this is one comment, string = "Hello ; World"
```

---

## Practical Examples

### Example 1: if Condition

```python
# Pseudocode:
# if ammo_type == 5:
#     is_sniper = 1
```

```asm
    ; if ammo_type == 5
    ldloc 2                    ; bullet_info (arg2)
    ldattr "AmmunitionType"    ; bullet_info.AmmunitionType
    ldfloat 5                  ; 5 (sniper type)
    cmpeq                      ; AmmunitionType == 5 ?
    jz label_skip_sniper       ; if not — skip

    ; is_sniper = 1
    ldfloat 1
    stloc 4                    ; is_sniper = 1

label_skip_sniper:
    ; continue...
```

### Example 2: if-else with Two Branches

```python
# if health > 50:
#     status = 1
# else:
#     status = 0
```

```asm
    ldloc 0
    ldattr "health"
    ldfloat 50
    cmpg                       ; health > 50 ?
    jz label_else              ; if not — go to else

    ; then: status = 1
    ldfloat 1
    stloc 1
    jmp label_endif

label_else:
    ; else: status = 0
    ldfloat 0
    stloc 1

label_endif:
    ; continue...
```

### Example 3: Compound Condition (AND)

```python
# if broadcastSniperHits == 1 and AmmunitionType == 5:
#     is_sniper = 1
```

```asm
    ; First condition
    ldloc 3                        ; properties
    ldattr "broadcastSniperHits"
    ldfloat 1
    cmpeq                          ; broadcastSniperHits == 1

    ; Second condition
    ldloc 2                        ; bullet_info
    ldattr "AmmunitionType"
    ldfloat 5
    cmpeq                          ; AmmunitionType == 5

    ; Logical AND
    and                            ; condition1 AND condition2
    jz label_not_sniper            ; if false — skip

    ldfloat 1
    stloc 4                        ; is_sniper = 1

label_not_sniper:
```

### Example 4: Method Call with Formatted Output

```python
# debug.LogInfo("Damage: %f", bullet.Damage)
```

```asm
    ldstr "Damage: %f"        ; format string
    ldloc 0                   ; bullet_info
    ldattr "Damage"           ; bullet_info.Damage
    mklist 1                  ; create list from 1 argument: [Damage]
    ldglob "debug"            ; global debug object
    ldattr "LogInfo"          ; debug.LogInfo
    call 2                    ; call (2 args: format + list)
    pop                       ; discard result
```

### Example 5: Null Check and Nested Conditions

```python
# part = vehicle.GetPartByShapeKey(target, bullet.ShapeKey)
# if part is not None:
#     weakspot = part.LinkedGameObject
#     if weakspot is not None:
#         physics.WeakspotOnHitByBullet(weakspot, bullet)
```

```asm
    ; part = vehicle.GetPartByShapeKey(target, shapeKey)
    ldloc 1                    ; target (arg1)
    ldloc 2                    ; bullet_info (arg2)
    ldattr "ShapeKey"          ; bullet_info.ShapeKey
    ldglob "vehicle"
    ldattr "GetPartByShapeKey"
    call 2
    stloc 5                    ; part = result

    ; if part is not None
    ldloc 5
    jz label_no_part           ; jz jumps if None/false/0

    ; weakspot = part.LinkedGameObject
    ldloc 5
    ldattr "LinkedGameObject"
    stloc 6

    ; if weakspot is not None
    ldloc 6
    jz label_no_weakspot

    ; physics.WeakspotOnHitByBullet(weakspot, bullet)
    ldloc 6                    ; weakspot — arg1
    ldloc 2                    ; bullet_info — arg2
    ldglob "physics"
    ldattr "WeakspotOnHitByBullet"
    call 2
    pop

label_no_weakspot:
label_no_part:
    ; continue...
```

### Example 6: Sending an Event

```asm
    ; scriptgo.SendEvent(self, event_hash)
    ldloc 0                    ; self (arg0)
    ldbytes 17 21 32 F3        ; event hash (4 bytes)
    ldglob "scriptgo"
    ldattr "SendEvent"
    call 2
    pop
```

### Example 7: Creating a New Function

```asm
== MyCustomFunction ==
; args: 2  locals: 3

    ; arg0 = self, arg1 = target
    ; local 2 = temporary

    ldloc 1              ; target
    ldattr "Health"      ; target.Health
    ldfloat 10
    sub                  ; Health - 10
    stloc 2              ; temp = Health - 10

    ldloc 2
    ldfloat 0
    cmpg                 ; temp > 0 ?
    jz label_dead

    ; Target is alive — set new health
    ldloc 1
    ldloc 2
    stattr "Health"      ; target.Health = temp
    jmp label_end

label_dead:
    ; Target is dead
    ldloc 1
    ldfloat 0
    stattr "Health"      ; target.Health = 0

label_end:
    ret 0                ; return without value
```

---

## Workflow: Disassemble, Edit, Assemble

### Step 1: Extract the Script from the Game

`.xvmc` scripts are stored inside ADF containers. Use Gibbed.MadMax tools
for extraction.

### Step 2: Disassemble

```
Gibbed.MadMax.XvmDisassemble.exe bullet_damage_handler.xvmc
```

This creates `bullet_damage_handler.dis`.

### Step 3: Edit the .dis File

Open the `.dis` file in a text editor. An editor with ASM syntax highlighting
is recommended (VSCode, Notepad++, Sublime Text).

**What you can change:**
- Instructions within functions
- Constant values (`ldfloat`, `ldstr`, `ldbool`)
- Conditions (`cmpeq`, `cmpg`, `jz`, `jmp`)
- Add/remove instructions
- Add new functions

**What you need to update when making changes:**
- Labels — if jump targets changed
- Addresses (XXXX:) — optional, the assembler ignores them

> **Note:** `hash`, `locals`, and `max_stack` are all computed automatically —
> you don't need to update them manually.

### Step 4: Assemble Back

```
Gibbed.MadMax.XvmAssemble.exe bullet_damage_handler.dis
```

This creates `bullet_damage_handler.xvmc`.

### Step 5: Verify (Optional)

Disassemble the new `.xvmc` and compare with the original `.dis`:

```
Gibbed.MadMax.XvmDisassemble.exe bullet_damage_handler.xvmc bullet_damage_handler_check.dis
```

The `.dis` files should be identical (round-trip).

### Step 6: Pack and Insert into the Game

Replace the original `.xvmc` in the game resources with the modified one.

---

## Limitations

### Assembler Limits

| Limit | Value | Reason |
|-------|-------|--------|
| Max constants | 2047 | 11-bit operand |
| Max instructions per function | 2047 | 11-bit operand |
| Max local variables | 2047 | 11-bit operand |
| Max stack depth | 65535 | 16-bit field |
| Max call arguments | 2047 | 11-bit operand |
| String length | 255 | 8-bit Length field |

### Instruction Format

Each instruction is 16 bits:

```
[15       5][4    0]
 operand    opcode
 (11 bits)  (5 bits)
```

- **5-bit opcode** -> 32 possible instructions (30 used)
- **11-bit operand** -> values 0-2047

### debug_info Is Fully Restored

The assembler fully supports `debug_info` round-trip. When the disassembler encounters a
`debug_info` ADF instance in a `.xvmc` file, it emits `@line:col` annotations at the end
of each instruction line. The assembler reads these annotations back and rebuilds the
`debug_info` ADF instance (type `0xDCB06466`) with per-instruction line and column data.

If you are writing scripts from scratch, you can omit the `@line:col` annotations —
the assembler will simply not generate a `debug_info` instance. The game will still
execute the script normally; `debug_info` is only used for runtime error messages
with source line references.

---

## Common Mistakes

### 1. Forgetting `pop` After `call`

```asm
    ; WRONG — result remains on the stack!
    ldloc 0
    ldglob "game"
    ldattr "DoSomething"
    call 1

    ; CORRECT
    ldloc 0
    ldglob "game"
    ldattr "DoSomething"
    call 1
    pop                    ; remove unused result
```

If the `call` result is not needed — always `pop`. Otherwise the stack will grow,
and `max_stack` will be incorrect.

### 2. Wrong Argument Order for call

```asm
    ; WRONG — arguments must come BEFORE the object
    ldglob "game"
    ldattr "Method"
    ldloc 0               ; arg1 after method — ERROR
    call 1

    ; CORRECT
    ldloc 0               ; arg1 — push first
    ldglob "game"
    ldattr "Method"        ; method — push last
    call 1
```

### 3. ~~Incorrect `max_stack`~~ (No Longer an Issue)

`max_stack` is now computed automatically by the assembler. You do not need to
specify or update it manually.

### 4. ~~Incorrect `locals`~~ (No Longer an Issue)

`locals` is now computed automatically by scanning all `ldloc`/`stloc` instructions.

### 5. Undefined Label

```asm
    jz label_42            ; ERROR if label_42 is not defined
```

The assembler will throw an error: `undefined label 'label_42'`.

### 6. Label Outside Function

Labels are scoped to their function. You cannot jump from one function to another.

### 7. `ret` at the End of Every Function

Every function **must** end with a `ret` instruction:

```asm
    ret 0    ; return without value
    ; or
    ret 1    ; return with value (top of stack)
```

---

## Cheat Sheet

### Read property
```asm
ldloc OBJ -> ldattr "Property" -> stloc DEST
```

### Write property
```asm
ldloc OBJ -> ldloc VALUE -> stattr "Property"
```

### Method call without return value
```asm
ldloc ARG1 -> [ldloc ARG2...] -> ldglob "obj" -> ldattr "Method" -> call N -> pop
```

### if condition
```asm
[condition] -> jz label_skip -> [if body] -> label_skip:
```

### if-else condition
```asm
[condition] -> jz label_else -> [if body] -> jmp label_end -> label_else: -> [else body] -> label_end:
```

### Compare with number
```asm
ldloc VAR -> ldfloat VALUE -> cmpeq/cmpg/cmpge/cmpne -> jz label
```

### Null check
```asm
ldloc VAR -> jz label_is_null
```

### Debug output
```asm
ldstr "format" -> [values...] -> mklist N -> ldglob "debug" -> ldattr "LogInfo" -> call 2 -> pop
```
