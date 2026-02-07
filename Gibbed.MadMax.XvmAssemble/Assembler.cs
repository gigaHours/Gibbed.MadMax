using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Gibbed.MadMax.FileFormats;

namespace Gibbed.MadMax.XvmAssemble
{
    /// <summary>
    /// Assembles a parsed .dis module into an XvmModule ready for serialization.
    /// Builds constant table, string buffer, string hashes, resolves labels,
    /// and encodes instructions.
    /// </summary>
    internal class Assembler
    {
        private readonly DisParser.ParsedModule _parsed;

        // Built tables
        private readonly List<XvmModule.Constant> _constants = new List<XvmModule.Constant>();
        private readonly List<byte> _stringBuffer = new List<byte>();
        private readonly List<uint> _stringHashes = new List<uint>();
        private readonly List<string> _debugStringsList = new List<string>();

        // Track prefix positions in StringBuffer for debug string patching
        // Each entry: (position of debug_hi byte in _stringBuffer, index in _debugStringsList)
        // Only key strings get patched — inline strings don't need debug offset patching
        private readonly List<int> _debugOffsetPositions = new List<int>();
        // Parallel to _debugOffsetPositions — index into _debugStringsList
        private readonly List<int> _debugOffsetStringIndices = new List<int>();

        // Deduplication maps
        private readonly Dictionary<string, int> _stringConstantMap = new Dictionary<string, int>(); // "text" -> constant index
        private readonly Dictionary<string, int> _keyStringMap = new Dictionary<string, int>(); // attr/glob key -> constant index
        private readonly Dictionary<uint, int> _floatConstantMap = new Dictionary<uint, int>(); // float bits -> constant index
        private readonly Dictionary<string, int> _bytesConstantMap = new Dictionary<string, int>(); // hex string -> constant index
        private int _noneConstantIndex = -1;

        // String hash dedup
        private readonly Dictionary<uint, int> _stringHashIndexMap = new Dictionary<uint, int>(); // hash -> index in _stringHashes

        public Assembler(DisParser.ParsedModule parsed)
        {
            _parsed = parsed;
        }

        public AssembleResult Assemble()
        {
            var module = new XvmModule();
            module.NameHash = _parsed.NameHash;
            module.SourceHash = _parsed.SourceHash;
            module.Flags = _parsed.Flags;
            module.ModuleSize = _parsed.ModuleSize;
            module.Name = _parsed.Name;

            // Pass 1: scan all instructions, build constant/string tables
            foreach (var pf in _parsed.Functions)
            {
                foreach (var pi in pf.Instructions)
                {
                    RegisterConstant(pi);
                }
            }

            // Pass 2: encode instructions with resolved labels and constant indices
            foreach (var pf in _parsed.Functions)
            {
                var function = new XvmModule.Function();
                function.Name = pf.Name;
                function.NameHash = pf.NameHash;
                function.ArgCount = pf.ArgCount;
                function.LocalsCount = pf.LocalsCount;
                function.MaxStackDepth = pf.MaxStackDepth;

                var instructions = new ushort[pf.Instructions.Count];
                for (int i = 0; i < pf.Instructions.Count; i++)
                {
                    instructions[i] = EncodeInstruction(pf.Instructions[i], pf.Labels);
                }
                function.Instructions = instructions;

                module.Functions.Add(function);
            }

            // Build debug strings blob BEFORE finalizing StringBuffer,
            // because BuildDebugStringsBlob patches debug offsets in _stringBuffer
            var result = new AssembleResult();
            if (_parsed.HasDebugStrings && _debugStringsList.Count > 0)
            {
                result.DebugStrings = BuildDebugStringsBlob();
            }

            // Finalize tables (after debug string patching)
            foreach (var c in _constants)
            {
                module.Constants.Add(c);
            }

            foreach (var h in _stringHashes)
            {
                module.StringHashes.Add(h);
            }
            module.StringBuffer = _stringBuffer.ToArray();

            // Add import hashes from parsed module
            foreach (var h in _parsed.ImportHashes)
            {
                module.ImportHashes.Add(h);
            }

            result.Module = module;
            return result;
        }

        private void RegisterConstant(DisParser.ParsedInstruction instr)
        {
            switch (instr.OperandType)
            {
                case DisParser.InstructionOperandType.IsNone:
                    GetOrCreateNoneConstant();
                    break;

                case DisParser.InstructionOperandType.Float:
                    if (instr.Opcode == XvmOpcode.LoadConst)
                        GetOrCreateFloatConstant(instr.FloatOperand);
                    break;

                case DisParser.InstructionOperandType.String:
                    if (instr.Opcode == XvmOpcode.LoadConst)
                        GetOrCreateStringConstant(instr.StringOperand);
                    else // ldattr, stattr, ldglob — key strings
                        GetOrCreateKeyStringConstant(instr.StringOperand);
                    break;

                case DisParser.InstructionOperandType.Bytes:
                    if (instr.Opcode == XvmOpcode.LoadConst)
                        GetOrCreateBytesConstant(instr.BytesOperand);
                    break;
            }
        }

        private int GetOrCreateNoneConstant()
        {
            if (_noneConstantIndex >= 0)
                return _noneConstantIndex;

            var c = new XvmModule.Constant();
            c.Flags = 0;
            c.Value = 0;
            _noneConstantIndex = _constants.Count;
            _constants.Add(c);
            return _noneConstantIndex;
        }

        private int GetOrCreateFloatConstant(float value)
        {
            var rawBytes = BitConverter.GetBytes(value);
            var rawBits = BitConverter.ToUInt32(rawBytes, 0);

            int index;
            if (_floatConstantMap.TryGetValue(rawBits, out index))
                return index;

            var c = new XvmModule.Constant();
            // Type 3 (float): type in bits 16-19
            c.Flags = 0x30000;
            c.Value = rawBits;

            index = _constants.Count;
            _floatConstantMap[rawBits] = index;
            _constants.Add(c);
            return index;
        }

        private int GetOrCreateStringConstant(string text)
        {
            int index;
            if (_stringConstantMap.TryGetValue(text, out index))
                return index;

            var strBytes = Encoding.ASCII.GetBytes(text);
            int offset = AddInlineToStringBuffer(text, strBytes);
            byte len = (byte)strBytes.Length;

            var c = new XvmModule.Constant();
            // Type 4 (string): flags = len | (alloc_len << 8) | (4 << 16)
            // Original format: AllocatedLength = 0 for all string constants
            c.Flags = (ulong)len | 0x40000UL;
            c.Value = (ulong)offset;

            index = _constants.Count;
            _stringConstantMap[text] = index;
            _constants.Add(c);
            return index;
        }

        private int GetOrCreateKeyStringConstant(string text)
        {
            // Key strings (ldattr/stattr/ldglob): only 3-byte prefix in StringBuffer,
            // NO inline string data. String content lives only in debug_strings.
            // Constant.Length = original string length, AllocatedLength = 0
            int index;
            if (_keyStringMap.TryGetValue(text, out index))
                return index;

            var strBytes = Encoding.ASCII.GetBytes(text);
            int offset = AddKeyToStringBuffer(text);
            byte len = (byte)strBytes.Length;

            var c = new XvmModule.Constant();
            // flags = len | (0 << 8) | (4 << 16)
            c.Flags = (ulong)len | 0x40000UL;
            c.Value = (ulong)offset;

            index = _constants.Count;
            _keyStringMap[text] = index;
            _constants.Add(c);
            return index;
        }

        private int GetOrCreateBytesConstant(byte[] data)
        {
            var hexKey = BitConverter.ToString(data);
            int index;
            if (_bytesConstantMap.TryGetValue(hexKey, out index))
                return index;

            int offset = AddBytesToStringBuffer(data);
            byte len = (byte)data.Length;

            var c = new XvmModule.Constant();
            // flags = len | (0 << 8) | (4 << 16)
            c.Flags = (ulong)len | 0x40000UL;
            c.Value = (ulong)offset;

            index = _constants.Count;
            _bytesConstantMap[hexKey] = index;
            _constants.Add(c);
            return index;
        }

        /// <summary>
        /// Adds a string to the StringBuffer with inline data:
        /// [hash_index] [debug_offset_hi] [debug_offset_lo] [string_bytes...]
        /// Used for ldstr constants. Inline strings are NOT added to debug_strings.
        /// Returns the offset pointing to the string data.
        /// </summary>
        private int AddInlineToStringBuffer(string text, byte[] strBytes)
        {
            uint hash = HashUtil.HashString(text);
            int hashIndex = GetOrCreateStringHash(hash);

            // Write 3-byte prefix — inline strings do NOT go into debug_strings
            _stringBuffer.Add((byte)hashIndex);
            _stringBuffer.Add(0); // debug offset hi (unused for inline)
            _stringBuffer.Add(0); // debug offset lo (unused for inline)

            int dataOffset = _stringBuffer.Count;

            // Write string bytes inline + null terminator
            _stringBuffer.AddRange(strBytes);
            _stringBuffer.Add(0); // null terminator

            return dataOffset;
        }

        /// <summary>
        /// Adds a key string to the StringBuffer with prefix ONLY (no inline data):
        /// [hash_index] [debug_offset_hi] [debug_offset_lo]
        /// Used for ldattr/stattr/ldglob constants. The actual string text lives
        /// only in the debug_strings blob.
        /// Returns the offset pointing right after the prefix (where data would be).
        /// </summary>
        private int AddKeyToStringBuffer(string text)
        {
            uint hash = HashUtil.HashString(text);
            int hashIndex = GetOrCreateStringHash(hash);

            // Key strings go into debug_strings
            int debugIdx = _debugStringsList.Count;
            _debugStringsList.Add(text);

            // Write 3-byte prefix only
            _stringBuffer.Add((byte)hashIndex);

            // Track position of debug offset bytes for later patching
            int debugHiPos = _stringBuffer.Count;
            _debugOffsetPositions.Add(debugHiPos);
            _debugOffsetStringIndices.Add(debugIdx);

            _stringBuffer.Add(0); // debug offset hi (placeholder)
            _stringBuffer.Add(0); // debug offset lo (placeholder)

            // NO inline data — return offset right after prefix
            int dataOffset = _stringBuffer.Count;
            return dataOffset;
        }

        private int AddBytesToStringBuffer(byte[] data)
        {
            uint hash = HashUtil.HashBytes(data);
            int hashIndex = GetOrCreateStringHash(hash);

            // Bytes are NOT added to debug_strings

            // Write 3-byte prefix
            _stringBuffer.Add((byte)hashIndex);
            _stringBuffer.Add(0); // debug offset hi (unused for bytes)
            _stringBuffer.Add(0); // debug offset lo (unused for bytes)

            int dataOffset = _stringBuffer.Count;
            _stringBuffer.AddRange(data);
            _stringBuffer.Add(0); // null terminator

            return dataOffset;
        }

        private int GetOrCreateStringHash(uint hash)
        {
            int index;
            if (_stringHashIndexMap.TryGetValue(hash, out index))
                return index;

            index = _stringHashes.Count;
            _stringHashes.Add(hash);
            _stringHashIndexMap[hash] = index;
            return index;
        }

        private ushort EncodeInstruction(DisParser.ParsedInstruction instr, Dictionary<string, int> labels)
        {
            ushort opcode = (ushort)instr.Opcode;
            ushort operand = 0;

            switch (instr.OperandType)
            {
                case DisParser.InstructionOperandType.None:
                    operand = 0;
                    break;

                case DisParser.InstructionOperandType.Int:
                    operand = (ushort)instr.IntOperand;
                    break;

                case DisParser.InstructionOperandType.Float:
                    operand = (ushort)GetOrCreateFloatConstant(instr.FloatOperand);
                    break;

                case DisParser.InstructionOperandType.IsNone:
                    operand = (ushort)GetOrCreateNoneConstant();
                    break;

                case DisParser.InstructionOperandType.String:
                    if (instr.Opcode == XvmOpcode.LoadConst)
                        operand = (ushort)GetOrCreateStringConstant(instr.StringOperand);
                    else
                        operand = (ushort)GetOrCreateKeyStringConstant(instr.StringOperand);
                    break;

                case DisParser.InstructionOperandType.Bytes:
                    operand = (ushort)GetOrCreateBytesConstant(instr.BytesOperand);
                    break;

                case DisParser.InstructionOperandType.Label:
                    int targetAddress;
                    if (!labels.TryGetValue(instr.LabelOperand, out targetAddress))
                    {
                        throw new FormatException(string.Format(
                            "Line {0}: undefined label '{1}'", instr.SourceLine, instr.LabelOperand));
                    }
                    operand = (ushort)targetAddress;
                    break;
            }

            return (ushort)((operand << 5) | opcode);
        }

        private byte[] BuildDebugStringsBlob()
        {
            // Build a contiguous buffer of null-terminated UTF8 strings
            // containing ONLY key strings (ldattr/stattr/ldglob).
            // Then patch the debug offsets in StringBuffer for those key strings.
            using (var ms = new MemoryStream())
            {
                // First, build the blob — write all debug strings sequentially
                var offsets = new int[_debugStringsList.Count];
                for (int i = 0; i < _debugStringsList.Count; i++)
                {
                    offsets[i] = (int)ms.Position;
                    var text = _debugStringsList[i];
                    if (text != null)
                    {
                        var bytes = Encoding.UTF8.GetBytes(text);
                        ms.Write(bytes, 0, bytes.Length);
                    }
                    ms.WriteByte(0); // null terminator
                }

                // Patch debug offsets in StringBuffer for key strings only
                for (int i = 0; i < _debugOffsetPositions.Count; i++)
                {
                    int hiPos = _debugOffsetPositions[i];
                    int stringIdx = _debugOffsetStringIndices[i];
                    int debugOffset = offsets[stringIdx];
                    _stringBuffer[hiPos] = (byte)((debugOffset >> 8) & 0xFF);
                    _stringBuffer[hiPos + 1] = (byte)(debugOffset & 0xFF);
                }

                return ms.ToArray();
            }
        }
    }

    internal class AssembleResult
    {
        public XvmModule Module;
        public byte[] DebugStrings; // null if no debug strings
    }
}
