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
using System.IO;
using System.Linq;
using System.Text;
using Gibbed.MadMax.FileFormats;

namespace Gibbed.MadMax.XvmDecompile
{
    public enum ConstantKind
    {
        None,
        Float,
        String,
        Bytes,
    }

    public class DecodedInstruction
    {
        public int Index;
        public XvmOpcode Opcode;
        public int Operand;

        // Resolved constant values (for LoadConst)
        public ConstantKind ConstKind;
        public float FloatValue;
        public string StringValue;
        public byte[] BytesValue;

        // For LoadBool
        public bool BoolValue;

        // For LoadAttr, StoreAttr, LoadGlobal — resolved name
        public string AttrName;

        // For Jmp, Jz — target instruction index
        public int JumpTarget;
    }

    public static class InstructionDecoder
    {
        public static DecodedInstruction[] Decode(
            ushort[] instructions,
            XvmModule module,
            MemoryStream debugStrings)
        {
            var result = new DecodedInstruction[instructions.Length];

            for (int i = 0; i < instructions.Length; i++)
            {
                var raw = instructions[i];
                var opcode = (XvmOpcode)(raw & 0x1F);
                var operand = raw >> 5;

                var decoded = new DecodedInstruction();
                decoded.Index = i;
                decoded.Opcode = opcode;
                decoded.Operand = operand;

                switch (opcode)
                {
                    case XvmOpcode.LoadConst:
                        ResolveConstant(decoded, operand, module);
                        break;

                    case XvmOpcode.LoadBool:
                        decoded.BoolValue = operand != 0;
                        break;

                    case XvmOpcode.LoadAttr:
                    case XvmOpcode.StoreAttr:
                    case XvmOpcode.LoadGlobal:
                        decoded.AttrName = ResolveAttrName(operand, module, debugStrings);
                        break;

                    case XvmOpcode.Jmp:
                    case XvmOpcode.Jz:
                        decoded.JumpTarget = operand;
                        break;
                }

                result[i] = decoded;
            }

            return result;
        }

        private static void ResolveConstant(DecodedInstruction decoded, int constIndex, XvmModule module)
        {
            if (constIndex < 0 || constIndex >= module.Constants.Count)
                return;

            var constant = module.Constants[constIndex];

            switch (constant.Type)
            {
                case 0: // None
                    decoded.ConstKind = ConstantKind.None;
                    break;

                case 3: // Float
                    decoded.ConstKind = ConstantKind.Float;
                    var rawBytes = BitConverter.GetBytes((uint)constant.Value);
                    decoded.FloatValue = BitConverter.ToSingle(rawBytes, 0);
                    break;

                case 4: // String or Bytes
                {
                    if ((int)constant.Value + constant.Length > module.StringBuffer.Length)
                        break;

                    var bytes = new byte[constant.Length];
                    Array.Copy(module.StringBuffer, (int)constant.Value, bytes, 0, constant.Length);

                    bool isString = !bytes.Any(b =>
                        b != 0x09 && b != 0x0A && b != 0x0D && (b < 0x20 || b > 0x7F));

                    if (isString)
                    {
                        decoded.ConstKind = ConstantKind.String;
                        decoded.StringValue = Encoding.ASCII.GetString(bytes);
                    }
                    else
                    {
                        decoded.ConstKind = ConstantKind.Bytes;
                        decoded.BytesValue = bytes;
                    }
                    break;
                }
            }
        }

        private static string ResolveAttrName(int constIndex, XvmModule module, MemoryStream debugStrings)
        {
            if (constIndex < 0 || constIndex >= module.Constants.Count)
                return null;

            var constant = module.Constants[constIndex];
            if (constant.Type != 4)
                return null;

            int constVal = (int)constant.Value;

            if (debugStrings != null)
            {
                if (constVal >= 2 &&
                    constVal - 2 < module.StringBuffer.Length &&
                    constVal - 1 < module.StringBuffer.Length)
                {
                    var debugStringOffset = (module.StringBuffer[constVal - 2] << 8) |
                                            (module.StringBuffer[constVal - 1] << 0);
                    debugStrings.Position = debugStringOffset;
                    return ReadStringZ(debugStrings);
                }
            }
            else
            {
                // Fallback: use hash
                if (constVal >= 3 &&
                    constVal - 3 < module.StringBuffer.Length)
                {
                    var hashIndex = module.StringBuffer[constVal - 3];
                    if (hashIndex < module.StringHashes.Count)
                    {
                        return string.Format("_0x{0:X8}", module.StringHashes[hashIndex]);
                    }
                }
            }

            return null;
        }

        private static string ReadStringZ(Stream stream)
        {
            var sb = new StringBuilder();
            int b;
            while ((b = stream.ReadByte()) > 0)
            {
                sb.Append((char)b);
            }
            return sb.ToString();
        }
    }
}
