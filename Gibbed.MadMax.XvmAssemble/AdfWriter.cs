using System.Collections.Generic;
using System.IO;
using System.Text;
using Gibbed.IO;
using Gibbed.MadMax.FileFormats;

namespace Gibbed.MadMax.XvmAssemble
{
    /// <summary>
    /// Writes an XvmModule (and optional debug strings) into an ADF container (.xvmc).
    ///
    /// Layout matches the original game format:
    ///   [ADF Header: 0x40 bytes]
    ///   [Comment: null-terminated string]
    ///   [Padding to 16-byte alignment]
    ///   [Module instance data]
    ///   [Padding to 16-byte alignment]
    ///   [DebugStrings instance data] (if present)
    ///   [InstanceInfos]
    ///   [NameTable]
    /// </summary>
    public static class AdfWriter
    {
        private const uint DebugInfoTypeHash = 0xDCB06466;

        public static void Write(Stream output, XvmModule module, byte[] debugStrings,
            AssembleResult debugInfo, Endian endian)
        {
            // Serialize XvmModule to bytes
            byte[] moduleData;
            using (var ms = new MemoryStream())
            {
                XvmModuleWriter.Serialize(ms, module, endian);
                moduleData = ms.ToArray();
            }

            // Serialize debug_strings to bytes (if present)
            byte[] debugStringsData = null;
            if (debugStrings != null)
            {
                using (var ms = new MemoryStream())
                {
                    // debug_strings instance: offset (8 bytes) + count (8 bytes) + data
                    long dataOffset = 16; // after the two int64 fields
                    ms.WriteValueS64(dataOffset, endian);
                    ms.WriteValueS64(debugStrings.Length, endian);
                    ms.Write(debugStrings, 0, debugStrings.Length);
                    debugStringsData = ms.ToArray();
                }
            }

            // Serialize debug_info to bytes (if present)
            byte[] debugInfoData = null;
            if (debugInfo != null && debugInfo.HasDebugInfo)
            {
                debugInfoData = BuildDebugInfoInstance(debugInfo, endian);
            }

            // Build name table
            var nameTable = new List<string>();
            nameTable.Add("module");
            if (debugInfoData != null)
                nameTable.Add("debug_info");
            if (debugStringsData != null)
                nameTable.Add("debug_strings");

            // Compute layout — data first, metadata last
            int instanceCount = 1 + (debugInfoData != null ? 1 : 0) + (debugStringsData != null ? 1 : 0);

            // Header: 0x40 bytes + comment (empty null-terminated) = 0x41
            int headerEnd = 0x40 + 1; // comment is empty string + null terminator

            // Align module data to 16 bytes (matching original)
            int moduleDataOffset = Align(headerEnd, 16);
            int moduleDataEnd = moduleDataOffset + moduleData.Length;

            // Debug info data (if present) aligned to 16 bytes
            int debugInfoDataOffset = 0;
            int afterModule = moduleDataEnd;
            if (debugInfoData != null)
            {
                debugInfoDataOffset = Align(afterModule, 16);
                afterModule = debugInfoDataOffset + debugInfoData.Length;
            }

            // Debug strings data (if present) aligned to 16 bytes
            int debugStringsDataOffset = 0;
            int afterData;
            if (debugStringsData != null)
            {
                debugStringsDataOffset = Align(afterModule, 16);
                afterData = debugStringsDataOffset + debugStringsData.Length;
            }
            else
            {
                afterData = afterModule;
            }

            // InstanceInfos come after all data
            int instanceInfoOffset = afterData;
            int instanceInfoSize = instanceCount * 24;

            // NameTable comes after InstanceInfos
            int nameTableOffset = instanceInfoOffset + instanceInfoSize;
            int nameTableSize = ComputeNameTableSize(nameTable);

            int totalSize = nameTableOffset + nameTableSize;

            // Write header
            output.WriteValueU32(AdfFile.Signature, endian); // magic
            output.WriteValueU32(4, endian); // version
            output.WriteValueU32((uint)instanceCount, endian);
            output.WriteValueU32((uint)instanceInfoOffset, endian);
            output.WriteValueU32(0, endian); // type def count
            output.WriteValueU32(0, endian); // type def offset
            output.WriteValueU32(0, endian); // unknown18 count
            output.WriteValueU32(0, endian); // unknown1C offset
            output.WriteValueU32((uint)nameTable.Count, endian);
            output.WriteValueU32((uint)nameTableOffset, endian);
            output.WriteValueU32((uint)totalSize, endian);
            output.WriteValueU32(0, endian); // unknown2C
            output.WriteValueU32(0, endian); // unknown30
            output.WriteValueU32(0, endian); // unknown34
            output.WriteValueU32(0, endian); // unknown38
            output.WriteValueU32(0, endian); // unknown3C
            output.WriteStringZ("", Encoding.ASCII); // empty comment

            // Pad to module data offset
            WritePadding(output, moduleDataOffset);

            // Write module data
            output.Write(moduleData, 0, moduleData.Length);

            // Write debug info data (if present)
            if (debugInfoData != null)
            {
                WritePadding(output, debugInfoDataOffset);
                output.Write(debugInfoData, 0, debugInfoData.Length);
            }

            // Write debug strings data (if present)
            if (debugStringsData != null)
            {
                WritePadding(output, debugStringsDataOffset);
                output.Write(debugStringsData, 0, debugStringsData.Length);
            }

            // Write instance infos
            long nameIdx = 0;

            // module instance
            WriteInstanceInfo(output, endian,
                HashUtil.HashString("module"),
                XvmModule.TypeHash,
                (uint)moduleDataOffset,
                (uint)moduleData.Length,
                nameIdx++);

            // debug_info instance
            if (debugInfoData != null)
            {
                WriteInstanceInfo(output, endian,
                    HashUtil.HashString("debug_info"),
                    DebugInfoTypeHash,
                    (uint)debugInfoDataOffset,
                    (uint)debugInfoData.Length,
                    nameIdx++);
            }

            // debug_strings instance
            if (debugStringsData != null)
            {
                WriteInstanceInfo(output, endian,
                    HashUtil.HashString("debug_strings"),
                    0xFEF3B589,
                    (uint)debugStringsDataOffset,
                    (uint)debugStringsData.Length,
                    nameIdx++);
            }

            // Write name table
            WriteNameTable(output, nameTable);
        }

        private static void WriteInstanceInfo(Stream output, Endian endian,
            uint nameHash, uint typeHash, uint offset, uint size, long nameIndex)
        {
            output.WriteValueU32(nameHash, endian);
            output.WriteValueU32(typeHash, endian);
            output.WriteValueU32(offset, endian);
            output.WriteValueU32(size, endian);
            output.WriteValueS64(nameIndex, endian);
        }

        private static void WriteNameTable(Stream output, List<string> nameTable)
        {
            // First: all length bytes
            foreach (var name in nameTable)
            {
                output.WriteValueU8((byte)Encoding.ASCII.GetByteCount(name));
            }
            // Then: all strings with null terminators
            foreach (var name in nameTable)
            {
                output.WriteString(name, Encoding.ASCII);
                output.WriteValueU8(0);
            }
        }

        private static int ComputeNameTableSize(List<string> nameTable)
        {
            int size = 0;
            foreach (var name in nameTable)
                size += 1; // length byte
            foreach (var name in nameTable)
                size += Encoding.ASCII.GetByteCount(name) + 1; // string + null
            return size;
        }

        private static byte[] BuildDebugInfoInstance(AssembleResult debugInfo, Endian endian)
        {
            int funcCount = debugInfo.FunctionLineno.Count;

            // Header: 16 bytes
            // Function entries: funcCount * 40 bytes
            int headerSize = 16 + funcCount * 40;
            int dataStart = Align(headerSize, 16);

            // Compute data offsets for each function's lineno and colno arrays
            var linenoOffsets = new int[funcCount];
            var colnoOffsets = new int[funcCount];
            int pos = dataStart;

            for (int i = 0; i < funcCount; i++)
            {
                linenoOffsets[i] = pos;
                int linenoSize = debugInfo.FunctionLineno[i].Length * 2;
                pos += linenoSize;
                pos = Align(pos, 16);

                colnoOffsets[i] = pos;
                int colnoSize = debugInfo.FunctionColno[i].Length * 2;
                pos += colnoSize;
                if (i < funcCount - 1)
                    pos = Align(pos, 16);
            }

            int totalSize = pos;
            var buffer = new byte[totalSize];

            using (var ms = new MemoryStream(buffer))
            {
                // Header
                ms.WriteValueS64(0x10, endian); // functionsOffset
                ms.WriteValueS64(funcCount, endian); // functionCount

                // Function entries
                for (int i = 0; i < funcCount; i++)
                {
                    ms.WriteValueS64(linenoOffsets[i], endian);
                    ms.WriteValueS64(debugInfo.FunctionLineno[i].Length, endian);
                    ms.WriteValueS64(colnoOffsets[i], endian);
                    ms.WriteValueS64(debugInfo.FunctionColno[i].Length, endian);
                    ms.WriteValueU32(debugInfo.FunctionNameHashes[i], endian);
                    ms.WriteValueU32(0, endian); // padding
                }

                // Write data arrays (padding is already zeroed)
                for (int i = 0; i < funcCount; i++)
                {
                    ms.Position = linenoOffsets[i];
                    foreach (var v in debugInfo.FunctionLineno[i])
                        ms.WriteValueU16(v, endian);

                    ms.Position = colnoOffsets[i];
                    foreach (var v in debugInfo.FunctionColno[i])
                        ms.WriteValueU16(v, endian);
                }
            }

            return buffer;
        }

        private static void WritePadding(Stream output, int targetOffset)
        {
            while (output.Position < targetOffset)
                output.WriteValueU8(0);
        }

        private static int Align(int value, int alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }
    }
}
