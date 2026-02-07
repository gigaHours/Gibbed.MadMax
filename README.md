# Gibbed.MadMax

A set of C# tools for working with Mad Max (2015, Avalanche Studios) game files.

## Features

- **Unpack** game archives (`.tab`/`.arc` and `.sarc`)
- **Convert** binary data formats (ADF, properties) to XML and back
- **Disassemble** XVM script bytecode to human-readable `.dis` assembly
- **Assemble** `.dis` files back into `.xvmc` bytecode (round-trip capable)
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
| `Gibbed.MadMax.XvmDisassemble` | Disassemble `.xvmc` bytecode to `.dis` text |
| `Gibbed.MadMax.XvmAssemble` | Assemble `.dis` text back to `.xvmc` bytecode |

### Other

| Tool | Description |
|------|-------------|
| `RebuildFileLists` | Rebuild file name hash lists |
| `Gibbed.Avalanche.ArchiveViewer` | GUI archive browser (Windows Forms) |

## XVM Script Modding

The XVM tools enable a full round-trip workflow for modifying game scripts:

```
.xvmc  -->  XvmDisassemble  -->  .dis  -->  [edit]  -->  XvmAssemble  -->  .xvmc
```

1. **Disassemble** an `.xvmc` file to get a readable `.dis` file
2. **Edit** the `.dis` file — modify logic, change constants, add functions
3. **Assemble** the `.dis` back into `.xvmc`
4. **Verify** by disassembling the new `.xvmc` and comparing (should be identical)

For a complete guide on the XVM assembly language, see:
- **[XVM Assembly Guide (English)](XVM_ASSEMBLY_GUIDE.md)**
- **[XVM Assembly Guide (Russian)](XVM_ASSEMBLY_GUIDE_RU.md)**

### Quick Example

```asm
== MyFunction ==
; hash: 0xABCD1234  args: 2  locals: 3  max_stack: 5

    ldloc 1              ; load target
    ldattr "Health"      ; get Health attribute
    ldfloat 10
    sub                  ; Health - 10
    stloc 2              ; store in temp

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
+-- Gibbed.MadMax.XvmDisassemble/      # XVM disassembler
+-- Gibbed.MadMax.XvmAssemble/         # XVM assembler
+-- Gibbed.Avalanche.ArchiveViewer/    # GUI archive viewer
+-- RebuildFileLists/                  # Hash list rebuilder
+-- bin/                               # Build output
```

## Documentation

- **[README (English)](README.md)** — This file
- **[README (Russian)](README_RU.md)** — README on Russian
- **[XVM Assembly Guide (English)](XVM_ASSEMBLY_GUIDE.md)** — Complete guide to writing XVM assembly
- **[XVM Assembly Guide (Russian)](XVM_ASSEMBLY_GUIDE_RU.md)** — Complete guide to writing XVM assembly (Russian)
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
