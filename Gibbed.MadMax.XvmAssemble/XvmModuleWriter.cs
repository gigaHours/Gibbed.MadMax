using System.IO;
using System.Text;
using Gibbed.IO;
using Gibbed.MadMax.FileFormats;

namespace Gibbed.MadMax.XvmAssemble
{
    /// <summary>
    /// Serializes an XvmModule into a binary stream, matching the format
    /// that XvmModule.Deserialize() reads.
    ///
    /// Binary layout (all offsets relative to basePosition):
    /// [RawModule header]
    /// [Function headers (RawFunction[])]
    /// [Function instructions (ushort[][] interleaved)]
    /// [Function names (string[])]
    /// [ImportHashes (uint[])]
    /// [Constants (Constant[])]
    /// [StringHashes (uint[])]
    /// [StringBuffer (byte[])]
    /// [Module name (string)]
    /// </summary>
    internal static class XvmModuleWriter
    {
        // RawModule header size: 4+4+4+4 + 8+8+8 + (10 pairs of int64) = 24 + 160 = 184 bytes
        private const int RawModuleHeaderSize =
            4 + 4 + 4 + 4 + // NameHash, SourceHash, Flags, ModuleSize
            8 + 8 + 8 +     // DebugInfoArray, ThisInstance, ThisType
            8 + 8 +         // FunctionOffset, FunctionCount
            8 + 8 +         // ImportHashOffset, ImportHashCount
            8 + 8 +         // ConstantOffset, ConstantCount
            8 + 8 +         // StringHashOffset, StringHashCount
            8 + 8 +         // StringBufferOffset, StringBufferCount
            8 + 8 +         // DebugStringPointer, DebugStrings
            8 + 8;          // NameOffset, NameCount

        // RawFunction size: 4+2+2 + 8+8 + 2+6(pad) + 8+8+8 + 8+8 = 72 bytes
        private const int RawFunctionSize =
            4 + 2 + 2 +     // NameHash, LocalsCount, ArgCount
            8 + 8 +         // InstructionOffset, InstructionCount
            2 + 6 +         // MaxStackDepth + padding
            8 +             // Module
            8 + 8 +         // LinenoPtr, ColnoPtr
            8 + 8;          // NameOffset, NameCount

        public static void Serialize(Stream output, XvmModule module, Endian endian)
        {
            var basePosition = output.Position;

            // Reserve space for header — we'll fill it after computing offsets
            // Pad header to 16-byte alignment (152 bytes header + 8 padding = 160)
            output.Position = basePosition + Align(RawModuleHeaderSize, 16);

            // Write functions
            long functionOffset = 0;
            long functionCount = module.Functions.Count;

            if (functionCount > 0)
            {
                functionOffset = output.Position - basePosition;

                // Reserve space for function headers
                var funcHeadersStart = output.Position;
                output.Position = funcHeadersStart + functionCount * RawFunctionSize;

                // Write function data (instructions, names) and collect offsets
                var funcInfos = new FuncWriteInfo[functionCount];
                for (int i = 0; i < functionCount; i++)
                {
                    var func = module.Functions[i];
                    var info = new FuncWriteInfo();

                    // Write instructions
                    if (func.Instructions != null && func.Instructions.Length > 0)
                    {
                        info.InstructionOffset = output.Position - basePosition;
                        info.InstructionCount = func.Instructions.Length;
                        foreach (var instr in func.Instructions)
                        {
                            output.WriteValueU16(instr, endian);
                        }
                    }

                    // Write function name
                    if (func.Name != null)
                    {
                        var nameBytes = Encoding.ASCII.GetBytes(func.Name);
                        info.NameOffset = output.Position - basePosition;
                        info.NameCount = nameBytes.Length + 1; // include null terminator space
                        output.Write(nameBytes, 0, nameBytes.Length);
                        output.WriteValueU8(0); // null terminator
                    }

                    funcInfos[i] = info;
                }

                // Go back and write function headers
                var afterFuncs = output.Position;
                output.Position = funcHeadersStart;

                for (int i = 0; i < functionCount; i++)
                {
                    var func = module.Functions[i];
                    var info = funcInfos[i];

                    output.WriteValueU32(func.NameHash, endian);
                    output.WriteValueU16(func.LocalsCount, endian);
                    output.WriteValueU16(func.ArgCount, endian);
                    output.WriteValueS64(info.InstructionOffset, endian);
                    output.WriteValueS64(info.InstructionCount, endian);
                    output.WriteValueU16(func.MaxStackDepth, endian);
                    // 6 bytes padding
                    for (int p = 0; p < 6; p++)
                        output.WriteValueU8(0);
                    output.WriteValueU64(func.Module, endian);
                    output.WriteValueS64(func.LinenoPtr, endian);
                    output.WriteValueS64(func.ColnoPtr, endian);
                    output.WriteValueS64(info.NameOffset, endian);
                    output.WriteValueS64(info.NameCount, endian);
                }

                output.Position = afterFuncs;
            }

            // Write import hashes (aligned to 16 bytes)
            long importHashOffset = 0;
            long importHashCount = module.ImportHashes.Count;
            if (importHashCount > 0)
            {
                WritePadToAlign(output, basePosition, 16);
                importHashOffset = output.Position - basePosition;
                foreach (var hash in module.ImportHashes)
                {
                    output.WriteValueU32(hash, endian);
                }
            }

            // Write constants (aligned to 16 bytes)
            long constantOffset = 0;
            long constantCount = module.Constants.Count;
            if (constantCount > 0)
            {
                WritePadToAlign(output, basePosition, 16);
                constantOffset = output.Position - basePosition;
                foreach (var c in module.Constants)
                {
                    output.WriteValueU64(c.Flags, endian);
                    output.WriteValueU64(c.Value, endian);
                }
            }

            // Write string hashes (aligned to 16 bytes)
            long stringHashOffset = 0;
            long stringHashCount = module.StringHashes.Count;
            if (stringHashCount > 0)
            {
                WritePadToAlign(output, basePosition, 16);
                stringHashOffset = output.Position - basePosition;
                foreach (var hash in module.StringHashes)
                {
                    output.WriteValueU32(hash, endian);
                }
            }

            // Write string buffer (aligned to 16 bytes)
            long stringBufferOffset = 0;
            long stringBufferCount = 0;
            if (module.StringBuffer != null && module.StringBuffer.Length > 0)
            {
                WritePadToAlign(output, basePosition, 16);
                stringBufferOffset = output.Position - basePosition;
                stringBufferCount = module.StringBuffer.Length;
                output.Write(module.StringBuffer, 0, module.StringBuffer.Length);
            }

            // Write module name (aligned to 16 bytes)
            long nameOffset = 0;
            long nameCount = 0;
            if (module.Name != null)
            {
                WritePadToAlign(output, basePosition, 16);
                var nameBytes = Encoding.ASCII.GetBytes(module.Name);
                nameOffset = output.Position - basePosition;
                nameCount = nameBytes.Length + 1;
                output.Write(nameBytes, 0, nameBytes.Length);
                output.WriteValueU8(0);
            }

            // Go back and write the module header
            var endPosition = output.Position;
            output.Position = basePosition;

            output.WriteValueU32(module.NameHash, endian);
            output.WriteValueU32(module.SourceHash, endian);
            output.WriteValueU32(module.Flags, endian);
            output.WriteValueU32(module.ModuleSize, endian);
            output.WriteValueS64(0, endian); // DebugInfoArray
            output.WriteValueU64(0, endian); // ThisInstance
            output.WriteValueU64(0, endian); // ThisType
            output.WriteValueS64(functionOffset, endian);
            output.WriteValueS64(functionCount, endian);
            output.WriteValueS64(importHashOffset, endian);
            output.WriteValueS64(importHashCount, endian);
            output.WriteValueS64(constantOffset, endian);
            output.WriteValueS64(constantCount, endian);
            output.WriteValueS64(stringHashOffset, endian);
            output.WriteValueS64(stringHashCount, endian);
            output.WriteValueS64(stringBufferOffset, endian);
            output.WriteValueS64(stringBufferCount, endian);
            output.WriteValueS64(0, endian); // DebugStringPointer
            output.WriteValueS64(0, endian); // DebugStrings
            output.WriteValueS64(nameOffset, endian);
            output.WriteValueS64(nameCount, endian);
            // Pad header to 16-byte alignment
            while (output.Position - basePosition < Align(RawModuleHeaderSize, 16))
                output.WriteValueU8(0);

            output.Position = endPosition;
        }

        private static void WritePadToAlign(Stream output, long basePosition, int alignment)
        {
            long relativePos = output.Position - basePosition;
            long aligned = (relativePos + alignment - 1) & ~((long)alignment - 1);
            while (output.Position - basePosition < aligned)
                output.WriteValueU8(0);
        }

        private static int Align(int value, int alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }

        private struct FuncWriteInfo
        {
            public long InstructionOffset;
            public long InstructionCount;
            public long NameOffset;
            public long NameCount;
        }
    }
}
