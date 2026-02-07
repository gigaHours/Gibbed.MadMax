using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Gibbed.MadMax.FileFormats;

namespace Gibbed.MadMax.XvmAssemble
{
    /// <summary>
    /// Parses a .dis file (output of XvmDisassemble) into an intermediate representation
    /// that can be assembled back into a .xvmc file.
    /// </summary>
    internal class DisParser
    {
        public class ParsedModule
        {
            public string Name;
            public uint NameHash;
            public uint SourceHash;
            public uint Flags;
            public uint ModuleSize;
            public List<ParsedFunction> Functions = new List<ParsedFunction>();
            public bool HasDebugStrings;
            public List<uint> ImportHashes = new List<uint>();
        }

        public class ParsedFunction
        {
            public string Name;
            public uint NameHash;
            public ushort ArgCount;
            public ushort LocalsCount;
            public ushort MaxStackDepth;
            public List<ParsedInstruction> Instructions = new List<ParsedInstruction>();
            public Dictionary<string, int> Labels = new Dictionary<string, int>();
        }

        public class ParsedInstruction
        {
            public XvmOpcode Opcode;
            public InstructionOperandType OperandType;
            public int IntOperand;
            public float FloatOperand;
            public string StringOperand;
            public byte[] BytesOperand;
            public string LabelOperand;
            public int SourceLine;
        }

        public enum InstructionOperandType
        {
            None,       // assert, and, or, add, etc.
            Int,        // ldloc N, stloc N, call N, mklist N, ret N, ldbool N, dbgout N
            Float,      // ldfloat X
            String,     // ldstr "text", ldattr "name", stattr "name", ldglob "name"
            Bytes,      // ldbytes XX YY ZZ
            Label,      // jmp label_X, jz label_X
            IsNone,     // ldnone
        }

        public static ParsedModule Parse(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            return Parse(lines);
        }

        public static ParsedModule Parse(string[] lines)
        {
            var module = new ParsedModule();
            ParsedFunction currentFunction = null;
            int instructionIndex = 0;
            bool hasDebugStrings = false;
            string headerSection = null; // tracks "imports"

            for (int lineNum = 0; lineNum < lines.Length; lineNum++)
            {
                var rawLine = lines[lineNum];
                var line = rawLine.Trim();

                if (string.IsNullOrEmpty(line))
                    continue;

                // Module header comments: ; name: xxx
                if (line.StartsWith(";") && currentFunction == null)
                {
                    ParseModuleHeader(line, module, ref headerSection);
                    continue;
                }

                // Function header: == FunctionName ==
                if (line.StartsWith("==") && line.EndsWith("=="))
                {
                    currentFunction = new ParsedFunction();
                    currentFunction.Name = line.Substring(2, line.Length - 4).Trim();
                    instructionIndex = 0;
                    module.Functions.Add(currentFunction);
                    continue;
                }

                // Function metadata comment: ; hash: 0x... args: N locals: N max_stack: N
                if (line.StartsWith(";") && currentFunction != null)
                {
                    ParseFunctionMeta(line, currentFunction);
                    continue;
                }

                // Label: label_XX:
                if (line.EndsWith(":") && !line.Contains(" "))
                {
                    var labelName = line.Substring(0, line.Length - 1);
                    currentFunction.Labels[labelName] = instructionIndex;
                    continue;
                }

                // Instruction line: 0000: opcode [operand] [; comment]
                if (currentFunction != null)
                {
                    var instr = ParseInstruction(line, lineNum + 1);
                    if (instr != null)
                    {
                        // Detect if we have debug strings (string constants in quotes vs hashes)
                        if (instr.OperandType == InstructionOperandType.String &&
                            (instr.Opcode == XvmOpcode.LoadAttr ||
                             instr.Opcode == XvmOpcode.StoreAttr ||
                             instr.Opcode == XvmOpcode.LoadGlobal))
                        {
                            hasDebugStrings = true;
                        }

                        currentFunction.Instructions.Add(instr);
                        instructionIndex++;
                    }
                }
            }

            module.HasDebugStrings = hasDebugStrings;
            return module;
        }

        private static void ParseModuleHeader(string line, ParsedModule module, ref string headerSection)
        {
            var content = line.TrimStart(';').Trim();

            if (string.IsNullOrEmpty(content))
                return;

            // Check for section headers
            if (content == "imports:")
            {
                headerSection = "imports";
                return;
            }
            // If we're inside a section, parse hex values
            if (headerSection == "imports" && content.StartsWith("0x"))
            {
                module.ImportHashes.Add(ParseHex(content));
                return;
            }

            // Regular header fields (reset section)
            if (content.StartsWith("name:"))
            {
                headerSection = null;
                module.Name = content.Substring("name:".Length).Trim();
            }
            else if (content.StartsWith("name_hash:"))
            {
                headerSection = null;
                module.NameHash = ParseHex(content.Substring("name_hash:".Length).Trim());
            }
            else if (content.StartsWith("source_hash:"))
            {
                headerSection = null;
                module.SourceHash = ParseHex(content.Substring("source_hash:".Length).Trim());
            }
            else if (content.StartsWith("flags:"))
            {
                headerSection = null;
                module.Flags = ParseHex(content.Substring("flags:".Length).Trim());
            }
            else if (content.StartsWith("size:"))
            {
                headerSection = null;
                uint.TryParse(content.Substring("size:".Length).Trim(), out module.ModuleSize);
            }
            // Ignore other comment lines like "=== XVM Module ===" and known fields
        }

        private static void ParseFunctionMeta(string line, ParsedFunction function)
        {
            var content = line.TrimStart(';').Trim();

            // Parse: hash: 0xC9716879  args: 3  locals: 7  max_stack: 9
            var hashMatch = Regex.Match(content, @"hash:\s*0x([0-9A-Fa-f]+)");
            if (hashMatch.Success)
                function.NameHash = uint.Parse(hashMatch.Groups[1].Value, NumberStyles.HexNumber);

            var argsMatch = Regex.Match(content, @"args:\s*(\d+)");
            if (argsMatch.Success)
                function.ArgCount = ushort.Parse(argsMatch.Groups[1].Value);

            var localsMatch = Regex.Match(content, @"locals:\s*(\d+)");
            if (localsMatch.Success)
                function.LocalsCount = ushort.Parse(localsMatch.Groups[1].Value);

            var stackMatch = Regex.Match(content, @"max_stack:\s*(\d+)");
            if (stackMatch.Success)
                function.MaxStackDepth = ushort.Parse(stackMatch.Groups[1].Value);
        }

        private static ParsedInstruction ParseInstruction(string line, int sourceLine)
        {
            // Strip comment (but not inside strings)
            var codePart = StripComment(line);
            codePart = codePart.Trim();

            if (string.IsNullOrEmpty(codePart))
                return null;

            // Strip address prefix: "0000: "
            var addrMatch = Regex.Match(codePart, @"^[0-9A-Fa-f]{4}:\s*");
            if (addrMatch.Success)
                codePart = codePart.Substring(addrMatch.Length);

            if (string.IsNullOrEmpty(codePart))
                return null;

            var instr = new ParsedInstruction();
            instr.SourceLine = sourceLine;

            // Split into mnemonic and rest
            var firstSpace = codePart.IndexOf(' ');
            string mnemonic;
            string rest;

            if (firstSpace < 0)
            {
                mnemonic = codePart;
                rest = "";
            }
            else
            {
                mnemonic = codePart.Substring(0, firstSpace);
                rest = codePart.Substring(firstSpace + 1).Trim();
            }

            switch (mnemonic)
            {
                // Simple opcodes (no operand)
                case "assert": instr.Opcode = XvmOpcode.Assert; instr.OperandType = InstructionOperandType.None; break;
                case "and": instr.Opcode = XvmOpcode.And; instr.OperandType = InstructionOperandType.None; break;
                case "or": instr.Opcode = XvmOpcode.Or; instr.OperandType = InstructionOperandType.None; break;
                case "add": instr.Opcode = XvmOpcode.Add; instr.OperandType = InstructionOperandType.None; break;
                case "div": instr.Opcode = XvmOpcode.Div; instr.OperandType = InstructionOperandType.None; break;
                case "mod": instr.Opcode = XvmOpcode.Mod; instr.OperandType = InstructionOperandType.None; break;
                case "mul": instr.Opcode = XvmOpcode.Mul; instr.OperandType = InstructionOperandType.None; break;
                case "sub": instr.Opcode = XvmOpcode.Sub; instr.OperandType = InstructionOperandType.None; break;
                case "cmpeq": instr.Opcode = XvmOpcode.CmpEq; instr.OperandType = InstructionOperandType.None; break;
                case "cmpge": instr.Opcode = XvmOpcode.CmpGe; instr.OperandType = InstructionOperandType.None; break;
                case "cmpg": instr.Opcode = XvmOpcode.CmpG; instr.OperandType = InstructionOperandType.None; break;
                case "cmpne": instr.Opcode = XvmOpcode.CmpNe; instr.OperandType = InstructionOperandType.None; break;
                case "lditem": instr.Opcode = XvmOpcode.LoadItem; instr.OperandType = InstructionOperandType.None; break;
                case "pop": instr.Opcode = XvmOpcode.Pop; instr.OperandType = InstructionOperandType.None; break;
                case "stitem": instr.Opcode = XvmOpcode.StoreItem; instr.OperandType = InstructionOperandType.None; break;
                case "not": instr.Opcode = XvmOpcode.Not; instr.OperandType = InstructionOperandType.None; break;
                case "neg": instr.Opcode = XvmOpcode.Neg; instr.OperandType = InstructionOperandType.None; break;

                // Integer operand
                case "ldloc":
                    instr.Opcode = XvmOpcode.LoadLocal;
                    instr.OperandType = InstructionOperandType.Int;
                    instr.IntOperand = int.Parse(rest);
                    break;
                case "stloc":
                    instr.Opcode = XvmOpcode.StoreLocal;
                    instr.OperandType = InstructionOperandType.Int;
                    instr.IntOperand = int.Parse(rest);
                    break;
                case "call":
                    instr.Opcode = XvmOpcode.Call;
                    instr.OperandType = InstructionOperandType.Int;
                    instr.IntOperand = int.Parse(rest);
                    break;
                case "mklist":
                    instr.Opcode = XvmOpcode.MakeList;
                    instr.OperandType = InstructionOperandType.Int;
                    instr.IntOperand = int.Parse(rest);
                    break;
                case "ret":
                    instr.Opcode = XvmOpcode.Ret;
                    instr.OperandType = InstructionOperandType.Int;
                    instr.IntOperand = int.Parse(rest);
                    break;
                case "ldbool":
                    instr.Opcode = XvmOpcode.LoadBool;
                    instr.OperandType = InstructionOperandType.Int;
                    instr.IntOperand = int.Parse(rest);
                    break;
                case "dbgout":
                    instr.Opcode = XvmOpcode.DebugOut;
                    instr.OperandType = InstructionOperandType.Int;
                    instr.IntOperand = int.Parse(rest);
                    break;

                // ldnone
                case "ldnone":
                    instr.Opcode = XvmOpcode.LoadConst;
                    instr.OperandType = InstructionOperandType.IsNone;
                    break;

                // Float operand
                case "ldfloat":
                    instr.Opcode = XvmOpcode.LoadConst;
                    instr.OperandType = InstructionOperandType.Float;
                    instr.FloatOperand = float.Parse(rest, CultureInfo.InvariantCulture);
                    break;

                // String operand (quoted)
                case "ldstr":
                    instr.Opcode = XvmOpcode.LoadConst;
                    instr.OperandType = InstructionOperandType.String;
                    instr.StringOperand = Unescape(rest);
                    break;
                case "ldattr":
                    instr.Opcode = XvmOpcode.LoadAttr;
                    instr.OperandType = InstructionOperandType.String;
                    instr.StringOperand = Unescape(rest);
                    break;
                case "stattr":
                    instr.Opcode = XvmOpcode.StoreAttr;
                    instr.OperandType = InstructionOperandType.String;
                    instr.StringOperand = Unescape(rest);
                    break;
                case "ldglob":
                    instr.Opcode = XvmOpcode.LoadGlobal;
                    instr.OperandType = InstructionOperandType.String;
                    instr.StringOperand = Unescape(rest);
                    break;

                // Bytes operand
                case "ldbytes":
                    instr.Opcode = XvmOpcode.LoadConst;
                    instr.OperandType = InstructionOperandType.Bytes;
                    instr.BytesOperand = ParseHexBytes(rest);
                    break;

                // Label operand
                case "jmp":
                    instr.Opcode = XvmOpcode.Jmp;
                    instr.OperandType = InstructionOperandType.Label;
                    instr.LabelOperand = rest;
                    break;
                case "jz":
                    instr.Opcode = XvmOpcode.Jz;
                    instr.OperandType = InstructionOperandType.Label;
                    instr.LabelOperand = rest;
                    break;

                default:
                    throw new FormatException(string.Format(
                        "Line {0}: unknown mnemonic '{1}'", sourceLine, mnemonic));
            }

            return instr;
        }

        private static string StripComment(string line)
        {
            // Strip "; comment" but not inside quoted strings
            bool inQuote = false;
            bool escape = false;
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (escape)
                {
                    escape = false;
                    continue;
                }
                if (c == '\\' && inQuote)
                {
                    escape = true;
                    continue;
                }
                if (c == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }
                if (c == ';' && !inQuote)
                {
                    return line.Substring(0, i);
                }
            }
            return line;
        }

        private static string Unescape(string input)
        {
            // Remove surrounding quotes
            input = input.Trim();
            if (input.StartsWith("\"") && input.EndsWith("\""))
            {
                input = input.Substring(1, input.Length - 2);
            }
            else if (input.StartsWith("0x"))
            {
                // Hash reference without debug strings — keep as-is
                return input;
            }

            var sb = new StringBuilder();
            bool escape = false;
            foreach (char c in input)
            {
                if (escape)
                {
                    switch (c)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'n': sb.Append('\n'); break;
                        default: sb.Append('\\'); sb.Append(c); break;
                    }
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static byte[] ParseHexBytes(string input)
        {
            var parts = input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber);
            }
            return bytes;
        }

        private static uint ParseHex(string input)
        {
            input = input.Trim();
            if (input.StartsWith("0x") || input.StartsWith("0X"))
                return uint.Parse(input.Substring(2), NumberStyles.HexNumber);
            return uint.Parse(input);
        }
    }
}
