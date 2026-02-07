/* Copyright (c) 2015 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Gibbed.IO;
using NDesk.Options;
using XvmOpcode = Gibbed.MadMax.FileFormats.XvmOpcode;

namespace Gibbed.MadMax.XvmDisassemble
{
    internal class Program
    {
        private const uint DebugInfoTypeHash = 0xDCB06466;

        private static string GetExecutableName()
        {
            return Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        private static void Main(string[] args)
        {
            bool showHelp = false;

            var options = new OptionSet
            {
                { "h|help", "show this message and exit", v => showHelp = v != null },
            };

            List<string> extras = new List<string>();

            try
            {
                extras = options.Parse(args);
                //extras.Add("C:\\Developers\\GitHub\\Gibbed.MadMax\\bin\\xvm\\bullet_damage_handler.xvmc");
            }
            catch (OptionException e)
            {
                Console.Write("{0}: ", GetExecutableName());
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `{0} --help' for more information.", GetExecutableName());
                return;
            }

            if (extras.Count < 1 || extras.Count > 2 ||
                showHelp == true)
            {
                Console.WriteLine("Usage: {0} [OPTIONS]+ input_xvmc [output_dis]", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            string inputPath = extras[0];
            string outputPath = extras.Count > 1 ? extras[1] : Path.ChangeExtension(inputPath, ".dis");

            Endian endian;
            var adf = new FileFormats.AdfFile();
            var module = new FileFormats.XvmModule();
            MemoryStream debugStrings = null;
            Dictionary<uint, FunctionDebugInfo> debugInfoMap = null;

            using (var input = File.OpenRead(inputPath))
            {
                adf.Deserialize(input);
                endian = adf.Endian;

                if (adf.TypeDefinitions.Count > 0)
                {
                    //throw new NotSupportedException();
                }

                var debugStringsInfo = adf.InstanceInfos.FirstOrDefault(i => i.Name == "debug_strings");
                if (debugStringsInfo.TypeHash == 0xFEF3B589)
                {
                    input.Position = debugStringsInfo.Offset;
                    using (var data = input.ReadToMemoryStream((int)debugStringsInfo.Size))
                    {
                        var offset = data.ReadValueS64(endian);
                        var count = data.ReadValueS64(endian);
                        if (count < 0 || count > int.MaxValue)
                        {
                            throw new FormatException();
                        }

                        data.Position = offset;
                        debugStrings = new MemoryStream(data.ReadBytes((int)count), false);
                    }
                }

                var debugInfoInfo = adf.InstanceInfos.FirstOrDefault(i => i.Name == "debug_info");
                if (debugInfoInfo.TypeHash == DebugInfoTypeHash)
                {
                    input.Position = debugInfoInfo.Offset;
                    using (var data = input.ReadToMemoryStream((int)debugInfoInfo.Size))
                    {
                        debugInfoMap = ReadDebugInfo(data, endian);
                    }
                }

                var moduleInfo = adf.InstanceInfos.First(i => i.Name == "module");
                if (moduleInfo.TypeHash != FileFormats.XvmModule.TypeHash)
                {
                    throw new FormatException();
                }

                input.Position = moduleInfo.Offset;
                using (var data = input.ReadToMemoryStream((int)moduleInfo.Size))
                {
                    module.Deserialize(data, endian);
                }
            }

            using (var output = File.Create(outputPath))
            using (var streamWriter = new StreamWriter(output))
            using (var writer = new System.CodeDom.Compiler.IndentedTextWriter(streamWriter))
            using (debugStrings)
            {
                WriteModuleHeader(writer, module);

                foreach (var function in module.Functions)
                {
                    FunctionDebugInfo funcDebug = null;
                    if (debugInfoMap != null)
                        debugInfoMap.TryGetValue(function.NameHash, out funcDebug);
                    WriteFunctionDisassembly(writer, function, module, debugStrings, funcDebug);
                }
            }
        }

        private class FunctionDebugInfo
        {
            public ushort[] Lineno;
            public ushort[] Colno;
        }

        private static Dictionary<uint, FunctionDebugInfo> ReadDebugInfo(Stream data, Endian endian)
        {
            var result = new Dictionary<uint, FunctionDebugInfo>();

            var functionsOffset = data.ReadValueS64(endian);
            var functionCount = data.ReadValueS64(endian);

            if (functionCount <= 0)
                return result;

            data.Position = functionsOffset;

            var entries = new List<Tuple<long, long, long, long, uint>>();
            for (long i = 0; i < functionCount; i++)
            {
                var linenoOffset = data.ReadValueS64(endian);
                var linenoCount = data.ReadValueS64(endian);
                var colnoOffset = data.ReadValueS64(endian);
                var colnoCount = data.ReadValueS64(endian);
                var nameHash = data.ReadValueU32(endian);
                data.ReadValueU32(endian); // padding
                entries.Add(Tuple.Create(linenoOffset, linenoCount, colnoOffset, colnoCount, nameHash));
            }

            foreach (var entry in entries)
            {
                var info = new FunctionDebugInfo();

                if (entry.Item2 > 0)
                {
                    info.Lineno = new ushort[entry.Item2];
                    data.Position = entry.Item1;
                    for (long j = 0; j < entry.Item2; j++)
                        info.Lineno[j] = data.ReadValueU16(endian);
                }

                if (entry.Item4 > 0)
                {
                    info.Colno = new ushort[entry.Item4];
                    data.Position = entry.Item3;
                    for (long j = 0; j < entry.Item4; j++)
                        info.Colno[j] = data.ReadValueU16(endian);
                }

                result[entry.Item5] = info;
            }

            return result;
        }

        private static void WriteModuleHeader(
            System.CodeDom.Compiler.IndentedTextWriter writer,
            FileFormats.XvmModule module)
        {
            writer.WriteLine("; === XVM Module ===");
            if (module.Name != null)
            {
                writer.WriteLine("; name: {0}", module.Name);
            }
            writer.WriteLine("; name_hash: 0x{0:X8}", module.NameHash);
            writer.WriteLine("; source_hash: 0x{0:X8}", module.SourceHash);
            writer.WriteLine("; flags: 0x{0:X}", module.Flags);
            writer.WriteLine("; size: {0}", module.ModuleSize);
            writer.WriteLine("; functions: {0}", module.Functions.Count);
            writer.WriteLine("; constants: {0}", module.Constants.Count);
            writer.WriteLine("; string_hashes: {0}", module.StringHashes.Count);

            if (module.ImportHashes.Count > 0)
            {
                writer.WriteLine(";");
                writer.WriteLine("; imports:");
                foreach (var hash in module.ImportHashes)
                {
                    writer.WriteLine(";   0x{0:X8}", hash);
                }
            }

        }

        private static void WriteFunctionDisassembly(
            System.CodeDom.Compiler.IndentedTextWriter writer,
            FileFormats.XvmModule.Function function,
            FileFormats.XvmModule module,
            MemoryStream debugStrings,
            FunctionDebugInfo funcDebug)
        {
            writer.WriteLine();
            writer.WriteLine("== {0} ==", function.Name ?? string.Format("0x{0:X8}", function.NameHash));
            writer.WriteLine("; hash: 0x{0:X8}  args: {1}  locals: {2}  max_stack: {3}",
                function.NameHash, function.ArgCount, function.LocalsCount, function.MaxStackDepth);

            if (function.Instructions == null || function.Instructions.Length == 0)
            {
                writer.Indent++;
                writer.WriteLine("; (no instructions)");
                writer.Indent--;
                return;
            }

            // Pass 1: collect jump targets for labels
            var labels = new string[function.Instructions.Length];
            for (int i = 0; i < function.Instructions.Length; i++)
            {
                var instruction = function.Instructions[i];
                var opcode = (XvmOpcode)(instruction & 0x1F);
                var oparg = instruction >> 5;

                if (opcode == XvmOpcode.Jmp || opcode == XvmOpcode.Jz)
                {
                    if (oparg < labels.Length && labels[oparg] == null)
                    {
                        labels[oparg] = string.Format("label_{0}", oparg);
                    }
                }
            }

            // Pass 2: emit instructions
            writer.Indent++;

            for (int i = 0; i < function.Instructions.Length; i++)
            {
                if (labels[i] != null)
                {
                    writer.Indent--;
                    writer.WriteLine("{0}:", labels[i]);
                    writer.Indent++;
                }

                var instruction = function.Instructions[i];
                var opcode = (XvmOpcode)(instruction & 0x1F);
                var oparg = instruction >> 5;
                var rawOpcodeValue = (int)(instruction & 0x1F);

                // instruction address prefix
                writer.Write("{0:X4}: ", i);

                // handle unknown opcodes gracefully
                if (!Enum.IsDefined(typeof(XvmOpcode), opcode))
                {
                    writer.Write("??? 0x{0:X4} ; unknown opcode {1}, operand {2}",
                        instruction, rawOpcodeValue, oparg);
                    writer.WriteLine();
                    continue;
                }

                if (_SimpleStatements.ContainsKey(opcode) == true)
                {
                    writer.Write("{0}", _SimpleStatements[opcode]);
                }
                else
                {
                    switch (opcode)
                    {
                        case XvmOpcode.MakeList:
                        {
                            writer.Write("mklist {0}", oparg);
                            break;
                        }

                        case XvmOpcode.Call:
                        {
                            writer.Write("call {0}", oparg);
                            break;
                        }

                        case XvmOpcode.Jmp:
                        {
                            var target = oparg < labels.Length ? labels[oparg] : string.Format("0x{0:X4}", oparg);
                            writer.Write("jmp {0}", target);
                            break;
                        }

                        case XvmOpcode.Jz:
                        {
                            var target = oparg < labels.Length ? labels[oparg] : string.Format("0x{0:X4}", oparg);
                            writer.Write("jz {0}", target);
                            break;
                        }

                        case XvmOpcode.LoadAttr:
                        {
                            writer.Write("ldattr ");
                            WriteStringConstant(writer, module, debugStrings, oparg);
                            break;
                        }

                        case XvmOpcode.LoadConst:
                        {
                            WriteLoadConst(writer, module, oparg);
                            break;
                        }

                        case XvmOpcode.LoadBool:
                        {
                            writer.Write("ldbool {0}", oparg);
                            break;
                        }

                        case XvmOpcode.LoadGlobal:
                        {
                            writer.Write("ldglob ");
                            WriteStringConstant(writer, module, debugStrings, oparg);
                            break;
                        }

                        case XvmOpcode.LoadLocal:
                        {
                            writer.Write("ldloc {0}", oparg);
                            if (oparg < function.ArgCount)
                            {
                                writer.Write(" ; arg{0}", oparg);
                            }
                            break;
                        }

                        case XvmOpcode.DebugOut:
                        {
                            writer.Write("dbgout {0}", oparg);
                            break;
                        }

                        case XvmOpcode.Ret:
                        {
                            writer.Write("ret {0}", oparg);
                            break;
                        }

                        case XvmOpcode.StoreAttr:
                        {
                            writer.Write("stattr ");
                            WriteStringConstant(writer, module, debugStrings, oparg);
                            break;
                        }

                        case XvmOpcode.StoreLocal:
                        {
                            writer.Write("stloc {0}", oparg);
                            if (oparg < function.ArgCount)
                            {
                                writer.Write(" ; arg{0}", oparg);
                            }
                            break;
                        }

                        default:
                        {
                            writer.Write("??? 0x{0:X4} ; unhandled opcode {1}, operand {2}",
                                instruction, opcode, oparg);
                            break;
                        }
                    }
                }

                // Append debug line:col annotation
                if (funcDebug != null)
                {
                    ushort line = (funcDebug.Lineno != null && i < funcDebug.Lineno.Length)
                        ? funcDebug.Lineno[i] : (ushort)0;
                    ushort col = (funcDebug.Colno != null && i < funcDebug.Colno.Length)
                        ? funcDebug.Colno[i] : (ushort)0;
                    writer.Write(" @{0}:{1}", line, col);
                }

                writer.WriteLine();
            }

            writer.Indent--;
        }

        private static void WriteStringConstant(
            System.CodeDom.Compiler.IndentedTextWriter writer,
            FileFormats.XvmModule module,
            MemoryStream debugStrings,
            int oparg)
        {
            if (oparg < 0 || oparg >= module.Constants.Count)
            {
                writer.Write("??? ; constant index {0} out of range (0..{1})", oparg, module.Constants.Count - 1);
                return;
            }

            var constant = module.Constants[oparg];

            if (constant.Type != 4)
            {
                writer.Write("??? ; expected string constant (type 4), got type {0}", constant.Type);
                return;
            }

            if (debugStrings != null)
            {
                if (constant.Value >= 2 &&
                    constant.Value - 2 < (ulong)module.StringBuffer.Length &&
                    constant.Value - 1 < (ulong)module.StringBuffer.Length)
                {
                    var debugStringOffset = (module.StringBuffer[constant.Value - 2] << 8) |
                                            (module.StringBuffer[constant.Value - 1] << 0);
                    debugStrings.Position = debugStringOffset;
                    var text = debugStrings.ReadStringZ(Encoding.UTF8);
                    writer.Write("\"{0}\"", Escape(text));
                }
                else
                {
                    writer.Write("??? ; invalid string buffer offset {0}", constant.Value);
                }
            }
            else
            {
                if (constant.Value >= 3 &&
                    constant.Value - 3 < (ulong)module.StringBuffer.Length)
                {
                    var hashIndex = module.StringBuffer[constant.Value - 3];
                    if (hashIndex < module.StringHashes.Count)
                    {
                        var hash = module.StringHashes[hashIndex];
                        writer.Write("0x{0:X8}", hash);
                    }
                    else
                    {
                        writer.Write("??? ; hash index {0} out of range", hashIndex);
                    }
                }
                else
                {
                    writer.Write("??? ; invalid string buffer offset {0}", constant.Value);
                }
            }
        }

        private static void WriteLoadConst(
            System.CodeDom.Compiler.IndentedTextWriter writer,
            FileFormats.XvmModule module,
            int oparg)
        {
            if (oparg < 0 || oparg >= module.Constants.Count)
            {
                writer.Write("ldconst ??? ; constant index {0} out of range (0..{1})",
                    oparg, module.Constants.Count - 1);
                return;
            }

            var constant = module.Constants[oparg];

            switch (constant.Type)
            {
                case 0:
                {
                    writer.Write("ldnone");
                    break;
                }

                case 3:
                {
                    var rawValue = (uint)constant.Value;
                    var rawBytes = BitConverter.GetBytes(rawValue);
                    var value = BitConverter.ToSingle(rawBytes, 0);
                    writer.Write("ldfloat {0}", value.ToString("R", CultureInfo.InvariantCulture));
                    break;
                }

                case 4:
                {
                    if (constant.Value > int.MaxValue)
                    {
                        writer.Write("ldstr ??? ; string offset too large: {0}", constant.Value);
                        break;
                    }

                    if ((int)constant.Value + constant.Length > module.StringBuffer.Length)
                    {
                        writer.Write("ldstr ??? ; string exceeds buffer: offset={0} len={1} buf_size={2}",
                            constant.Value, constant.Length, module.StringBuffer.Length);
                        break;
                    }

                    var bytes = new byte[constant.Length];
                    Array.Copy(module.StringBuffer, (int)constant.Value, bytes, 0, constant.Length);

                    if (bytes.Any(b => b != 0x09 && b != 0x0A && b != 0x0D && (b < 0x20 || b > 0x7F)) == true)
                    {
                        var value = BitConverter.ToString(bytes);
                        value = value.ToUpperInvariant();
                        value = value.Replace("-", " ");
                        writer.Write("ldbytes {0}", value);
                    }
                    else
                    {
                        var text = Encoding.ASCII.GetString(bytes);
                        writer.Write("ldstr \"{0}\"", Escape(text));
                    }

                    break;
                }

                default:
                {
                    writer.Write("ldconst ??? ; unknown constant type {0}, flags=0x{1:X}, value=0x{2:X}",
                        constant.Type, constant.Flags, constant.Value);
                    break;
                }
            }
        }

        private static string Escape(string input)
        {
            var sb = new StringBuilder();
            foreach (char t in input)
            {
                switch (t)
                {
                    case '"':
                    {
                        sb.Append("\\\"");
                        break;
                    }

                    case '\\':
                    {
                        sb.Append("\\\\");
                        break;
                    }

                    case '\t':
                    {
                        sb.Append("\\t");
                        break;
                    }

                    case '\r':
                    {
                        sb.Append("\\r");
                        break;
                    }

                    case '\n':
                    {
                        sb.Append("\\n");
                        break;
                    }

                    default:
                    {
                        sb.Append(t);
                        break;
                    }
                }
            }
            return sb.ToString();
        }

        private static readonly Dictionary<XvmOpcode, string> _SimpleStatements =
            new Dictionary<XvmOpcode, string>()
            {
                { XvmOpcode.Assert, "assert" },
                { XvmOpcode.And, "and" },
                { XvmOpcode.Or, "or" },
                { XvmOpcode.Add, "add" },
                { XvmOpcode.Div, "div" },
                { XvmOpcode.Mod, "mod" },
                { XvmOpcode.Mul, "mul" },
                { XvmOpcode.Sub, "sub" },
                { XvmOpcode.CmpEq, "cmpeq" },
                { XvmOpcode.CmpGe, "cmpge" },
                { XvmOpcode.CmpG, "cmpg" },
                { XvmOpcode.CmpNe, "cmpne" },
                { XvmOpcode.LoadItem, "lditem" },
                { XvmOpcode.Pop, "pop" },
                { XvmOpcode.StoreItem, "stitem" },
                { XvmOpcode.Not, "not" },
                { XvmOpcode.Neg, "neg" },
            };
    }
}
