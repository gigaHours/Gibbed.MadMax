# Gibbed.MadMax

A set of C# tools for working with Mad Max (2015, Avalanche Studios) game files.

## Features

- **Unpack** game archives (`.tab`/`.arc` and `.sarc`)
- **Convert** binary data formats (ADF, properties) to XML and back
- **Decompile** XVM script bytecode to high-level Python-like `.xvm` source code
- **Compile** `.xvm` source code back to `.xvmc` bytecode
- **Disassemble** XVM script bytecode to human-readable `.dis` assembly (with debug info)
- **Assemble** `.dis` files back into `.xvmc` bytecode (round-trip capable, including debug_info)
- **Browse** archive contents via a GUI application
- **Repack** files into `.sarc` archives

## Building

### Requirements

- Visual Studio 2022 (or compatible)
- .NET Framework 4.8 SDK
- .NET Standard 2.0 SDK (for Gibbed.IO and NDesk.Options)

### Build

```bash
# Visual Studio
Open "Mad Max.sln" -> Build -> Build Solution

# Command line
msbuild "Mad Max.sln" /p:Configuration=Release
```

All compiled files are output to the `bin/` directory.

## Tools

### Archive Tools

| Tool | Description |
|------|-------------|
| `Gibbed.MadMax.Unpack` | Unpack large archives (`.tab` + `.arc`) |
| `Gibbed.MadMax.SmallUnpack` | Unpack small archives (`.sarc`) |
| `Gibbed.MadMax.SmallPack` | Pack files into `.sarc` archives |

### Conversion Tools

| Tool | Description |
|------|-------------|
| `Gibbed.MadMax.ConvertAdf` | Convert ADF files (binary <-> XML) |
| `Gibbed.MadMax.ConvertProperty` | Convert property containers (binary <-> XML) |
| `Gibbed.MadMax.ConvertSpreadsheet` | Convert spreadsheet data |

### XVM Script Tools

| Tool | Description |
|------|-------------|
| `Gibbed.MadMax.XvmDecompile` | Decompile `.xvmc` bytecode to high-level `.xvm` source (supports batch directory mode) |
| `Gibbed.MadMax.XvmCompile` | Compile `.xvm` source to `.xvmc` bytecode (supports batch directory mode) |
| `Gibbed.MadMax.XvmDisassemble` | Disassemble `.xvmc` bytecode to `.dis` text |
| `Gibbed.MadMax.XvmAssemble` | Assemble `.dis` text back to `.xvmc` bytecode |

### Other

| Tool | Description |
|------|-------------|
| `RebuildFileLists` | Rebuild file name hash lists |
| `Gibbed.Avalanche.ArchiveViewer` | GUI archive browser (Windows Forms) |

## XVM Script Modding

The XVM tools provide two levels of script modification:

### High-Level: Decompile / Compile (recommended)

```
.xvmc  -->  XvmDecompile  -->  .xvm  -->  [edit]  -->  XvmCompile  -->  .xvmc
```

1. **Decompile** an `.xvmc` to get readable Python-like `.xvm` source code
2. **Edit** the `.xvm` file — modify logic in a familiar high-level syntax
3. **Compile** the `.xvm` back into `.xvmc`

Both tools support **batch directory mode**: drag a folder onto the `.exe` (or pass a directory path) to process all `.xvmc`/`.xvm` files recursively.

For the complete XVM scripting language reference, see:
- **[XVM Scripting System (English)](docs/XVM.md)**
- **[Скриптовая система XVM (русский)](docs/XVM_RU.md)**

### Low-Level: Disassemble / Assemble

```
.xvmc  -->  XvmDisassemble  -->  .dis  -->  [edit]  -->  XvmAssemble  -->  .xvmc
```

1. **Disassemble** an `.xvmc` file to get a readable `.dis` file
2. **Edit** the `.dis` file — modify individual bytecode instructions
3. **Assemble** the `.dis` back into `.xvmc`

Debug information (`debug_info` and `debug_strings`) is fully preserved during the round-trip.

For the XVM assembly language reference, see:
- **[XVM Assembly Guide (English)](XVM_ASSEMBLY_GUIDE.md)**
- **[XVM Assembly Guide (Russian)](XVM_ASSEMBLY_GUIDE_RU.md)**

### Quick Example (High-Level .xvm)

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

### Quick Example (Low-Level .dis)

```asm
== MyFunction ==
; hash: 0xABCD1234  args: 2  locals: 3  max_stack: 5

    ldloc 1              ; load target @7:5
    ldattr "Health"      ; get Health attribute @7:18
    ldfloat 10           ; @8:5
    sub                  ; Health - 10 @8:12
    stloc 2              ; store in temp @8:5

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

## Game File Formats

Mad Max uses the **Apex Engine** with custom binary formats:

| Format | Extension | Description |
|--------|-----------|-------------|
| ADF | `.adf`, `.xvmc`, etc. | Arbitrary Data Format — universal typed data container |
| TAB/ARC | `.tab` + `.arc` | Large game archives (indexed + data) |
| SARC | `.sarc` | Small uncompressed archives |
| RTPC/BIN | `.rtpc`, `.bin` | Property containers for game object configuration |
| XVMC | `.xvmc` | Compiled XVM script bytecode (ADF wrapper) |

## Project Structure

```
Gibbed.MadMax/
+-- Gibbed.IO/                          # Binary I/O library
+-- Gibbed.ProjectData/                 # Project metadata, hash lists
+-- Gibbed.MadMax.FileFormats/          # File format parsers (ADF, TAB, SARC, XVM)
+-- Gibbed.MadMax.PropertyFormats/      # Property container formats
+-- NDesk.Options/                      # Command-line argument parsing
+-- Gibbed.MadMax.Unpack/              # Archive unpacker
+-- Gibbed.MadMax.SmallUnpack/         # Small archive unpacker
+-- Gibbed.MadMax.SmallPack/           # Small archive packer
+-- Gibbed.MadMax.ConvertAdf/          # ADF converter
+-- Gibbed.MadMax.ConvertProperty/     # Property converter
+-- Gibbed.MadMax.ConvertSpreadsheet/  # Spreadsheet converter
+-- Gibbed.MadMax.XvmScript/           # Shared XVM AST library
+-- Gibbed.MadMax.XvmDecompile/        # XVM decompiler (.xvmc -> .xvm)
+-- Gibbed.MadMax.XvmCompile/          # XVM compiler (.xvm -> .xvmc)
+-- Gibbed.MadMax.XvmDisassemble/      # XVM disassembler (.xvmc -> .dis)
+-- Gibbed.MadMax.XvmAssemble/         # XVM assembler (.dis -> .xvmc)
+-- Gibbed.Avalanche.ArchiveViewer/    # GUI archive viewer
+-- RebuildFileLists/                  # Hash list rebuilder
+-- bin/                               # Build output
```

## Documentation

- **[README (English)](README.md)** — This file
- **[README (Russian)](README_RU.md)** — README на русском
- **[XVM Scripting System (English)](docs/XVM.md)** — Complete XVM language reference, toolchain, bytecode architecture
- **[Скриптовая система XVM (русский)](docs/XVM_RU.md)** — Полный справочник языка XVM, инструменты, архитектура байткода
- **[XVM Assembly Guide (English)](XVM_ASSEMBLY_GUIDE.md)** — Low-level XVM assembly guide
- **[XVM Assembly Guide (Russian)](XVM_ASSEMBLY_GUIDE_RU.md)** — Руководство по XVM ассемблеру
- **[Project Documentation (Russian)](DOCUMENTATION_RU.md)** — Detailed technical documentation

## License

BSD 3-Clause License.

Copyright (c) 2015, Rick (rick 'at' gibbed 'dot' us)

Permission is granted to anyone to use this software for any purpose, including
commercial applications, and to alter it and redistribute it freely.

See individual source files for the full license text.

## Credits

- **Rick** — Original author
- **Sir Kane** — Contributor
- **SK83RJOSH** — Contributor
