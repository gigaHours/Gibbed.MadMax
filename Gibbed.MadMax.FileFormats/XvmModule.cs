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
using System.IO;
using System.Text;
using Gibbed.IO;

namespace Gibbed.MadMax.FileFormats
{
    public class XvmModule
    {
        public const uint TypeHash = 0x41D02347;

        #region Fields
        private uint _NameHash;
        private uint _SourceHash;
        private uint _Flags;
        private uint _ModuleSize;
        private long _DebugInfoArray;
        private ulong _ThisInstance;
        private ulong _ThisType;
        private readonly List<Function> _Functions;
        private readonly List<uint> _ImportHashes; 
        private readonly List<Constant> _Constants;
        private readonly List<uint> _StringHashes; 
        private byte[] _StringBuffer;
        private string _Name;
        #endregion

        public XvmModule()
        {
            this._Functions = new List<Function>();
            this._ImportHashes = new List<uint>();
            this._Constants = new List<Constant>();
            this._StringHashes = new List<uint>();
        }

        #region Properties
        public uint NameHash
        {
            get { return this._NameHash; }
            set { this._NameHash = value; }
        }

        public uint SourceHash
        {
            get { return this._SourceHash; }
            set { this._SourceHash = value; }
        }

        public uint Flags
        {
            get { return this._Flags; }
            set { this._Flags = value; }
        }

        public uint ModuleSize
        {
            get { return this._ModuleSize; }
            set { this._ModuleSize = value; }
        }

        public long DebugInfoArray
        {
            get { return this._DebugInfoArray; }
            set { this._DebugInfoArray = value; }
        }

        public ulong ThisInstance
        {
            get { return this._ThisInstance; }
            set { this._ThisInstance = value; }
        }

        public ulong ThisType
        {
            get { return this._ThisType; }
            set { this._ThisType = value; }
        }

        public List<Function> Functions
        {
            get { return this._Functions; }
        }

        public List<uint> ImportHashes
        {
            get { return this._ImportHashes; }
        }

        public List<Constant> Constants
        {
            get { return this._Constants; }
        }

        public List<uint> StringHashes
        {
            get { return this._StringHashes; }
        }

        public byte[] StringBuffer
        {
            get { return this._StringBuffer; }
            set { this._StringBuffer = value; }
        }

        public string Name
        {
            get { return this._Name; }
            set { this._Name = value; }
        }
        #endregion

        public void Serialize(Stream output, Endian endian)
        {
            throw new NotImplementedException();
        }

        public void Deserialize(Stream input, Endian endian)
        {
            var basePosition = input.Position;

            var rawModule = RawModule.Read(input, endian);

            var functions = new Function[rawModule.FunctionCount];
            if (rawModule.FunctionCount != 0)
            {
                if (rawModule.FunctionCount < 0 || rawModule.FunctionCount > int.MaxValue)
                {
                    throw new FormatException();
                }

                var rawFunctions = new RawFunction[rawModule.FunctionCount];
                input.Position = basePosition + rawModule.FunctionOffset;
                for (long i = 0; i < rawModule.FunctionCount; i++)
                {
                    rawFunctions[i] = RawFunction.Read(input, endian);
                }

                for (long i = 0; i < rawModule.FunctionCount; i++)
                {
                    var rawFunction = rawFunctions[i];
                    var function = new Function();
                    function.NameHash = rawFunction.NameHash;
                    function.LocalsCount = rawFunction.LocalsCount;
                    function.ArgCount = rawFunction.ArgCount;
                    function.MaxStackDepth = rawFunction.MaxStackDepth;
                    function.Module = rawFunction.Module;
                    function.LinenoPtr = rawFunction.LinenoPtr;
                    function.ColnoPtr = rawFunction.ColnoPtr;

                    if (rawFunction.InstructionCount != 0)
                    {
                        if (rawFunction.InstructionCount < 0 || rawFunction.InstructionCount > int.MaxValue)
                        {
                            throw new FormatException();
                        }

                        input.Position = basePosition + rawFunction.InstructionOffset;
                        function.Instructions = new ushort[rawFunction.InstructionCount];
                        for (long j = 0; j < rawFunction.InstructionCount; j++)
                        {
                            function.Instructions[j] = input.ReadValueU16(endian);
                        }
                    }

                    if (rawFunction.NameCount == 0)
                    {
                        function.Name = null;
                    }
                    else
                    {
                        if (rawFunction.NameCount < 0 || rawFunction.NameCount > int.MaxValue)
                        {
                            throw new FormatException();
                        }

                        input.Position = basePosition + rawFunction.NameOffset;
                        function.Name = input.ReadString((int)rawFunction.NameCount, true, Encoding.ASCII);
                    }

                    functions[i] = function;
                }
            }

            var importHashes = new uint[rawModule.ImportHashCount];
            if (rawModule.ImportHashCount != 0)
            {
                if (rawModule.ImportHashCount < 0 || rawModule.ImportHashCount > int.MaxValue)
                {
                    throw new FormatException();
                }

                input.Position = basePosition + rawModule.ImportHashOffset;
                for (long i = 0; i < rawModule.ImportHashCount; i++)
                {
                    importHashes[i] = input.ReadValueU32(endian);
                }
            }

            var constants = new Constant[rawModule.ConstantCount];
            if (rawModule.ConstantCount != 0)
            {
                if (rawModule.ConstantCount < 0 || rawModule.ConstantCount > int.MaxValue)
                {
                    throw new FormatException();
                }

                input.Position = basePosition + rawModule.ConstantOffset;
                for (long i = 0; i < rawModule.ConstantCount; i++)
                {
                    constants[i] = Constant.Read(input, endian);
                }
            }

            var stringHashes = new uint[rawModule.StringHashCount];
            if (rawModule.StringHashCount != 0)
            {
                if (rawModule.StringHashCount < 0 || rawModule.StringHashCount > int.MaxValue)
                {
                    throw new FormatException();
                }

                input.Position = basePosition + rawModule.StringHashOffset;
                for (long i = 0; i < rawModule.StringHashCount; i++)
                {
                    stringHashes[i] = input.ReadValueU32(endian);
                }
            }

            byte[] stringBuffer;
            if (rawModule.StringBufferCount == 0)
            {
                stringBuffer = null;
            }
            else
            {
                if (rawModule.StringBufferCount < 0 || rawModule.StringBufferCount > int.MaxValue)
                {
                    throw new FormatException();
                }

                input.Position = basePosition + rawModule.StringBufferOffset;
                stringBuffer = input.ReadBytes((int)rawModule.StringBufferCount);
            }

            string name = null;
            if (rawModule.NameCount != 0)
            {
                if (rawModule.NameCount < 0 || rawModule.NameCount > int.MaxValue)
                {
                    throw new FormatException();
                }

                input.Position = basePosition + rawModule.NameOffset;
                name = input.ReadString((int)rawModule.NameCount, true, Encoding.ASCII);
            }

            this._NameHash = rawModule.NameHash;
            this._SourceHash = rawModule.SourceHash;
            this._Flags = rawModule.Flags;
            this._ModuleSize = rawModule.ModuleSize;
            this._DebugInfoArray = rawModule.DebugInfoArray;
            this._ThisInstance = rawModule.ThisInstance;
            this._ThisType = rawModule.ThisType;
            this._Functions.Clear();
            this._Functions.AddRange(functions);
            this._ImportHashes.Clear();
            this._ImportHashes.AddRange(importHashes);
            this._Constants.Clear();
            this._Constants.AddRange(constants);
            this._StringHashes.Clear();
            this._StringHashes.AddRange(stringHashes);
            this._StringBuffer = stringBuffer;
            this._Name = name;
        }

        private struct RawModule
        {
            public uint NameHash;
            public uint SourceHash;
            public uint Flags;
            public uint ModuleSize;
            public long DebugInfoArray;
            public ulong ThisInstance;
            public ulong ThisType;
            public long FunctionOffset;
            public long FunctionCount;
            public long ImportHashOffset;
            public long ImportHashCount;
            public long ConstantOffset;
            public long ConstantCount;
            public long StringHashOffset;
            public long StringHashCount;
            public long StringBufferOffset;
            public long StringBufferCount;
            public long DebugStringPointer;
            public long DebugStrings;
            public long NameOffset;
            public long NameCount;

            public static RawModule Read(Stream input, Endian endian)
            {
                var instance = new RawModule();
                instance.NameHash = input.ReadValueU32(endian);
                instance.SourceHash = input.ReadValueU32(endian);
                instance.Flags = input.ReadValueU32(endian);
                instance.ModuleSize = input.ReadValueU32(endian);
                instance.DebugInfoArray = input.ReadValueS64(endian);
                instance.ThisInstance = input.ReadValueU64(endian);
                instance.ThisType = input.ReadValueU64(endian);
                instance.FunctionOffset = input.ReadValueS64(endian);
                instance.FunctionCount = input.ReadValueS64(endian);
                instance.ImportHashOffset = input.ReadValueS64(endian);
                instance.ImportHashCount = input.ReadValueS64(endian);
                instance.ConstantOffset = input.ReadValueS64(endian);
                instance.ConstantCount = input.ReadValueS64(endian);
                instance.StringHashOffset = input.ReadValueS64(endian);
                instance.StringHashCount = input.ReadValueS64(endian);
                instance.StringBufferOffset = input.ReadValueS64(endian);
                instance.StringBufferCount = input.ReadValueS64(endian);
                instance.DebugStringPointer = input.ReadValueS64(endian);
                instance.DebugStrings = input.ReadValueS64(endian);
                instance.NameOffset = input.ReadValueS64(endian);
                instance.NameCount = input.ReadValueS64(endian);
                return instance;
            }
        }

        public struct Constant
        {
            public ulong Flags;
            public ulong Value;

            public byte Length
            {
                get { return (byte)((this.Flags >> 0) & 0xFF); }
            }

            public byte AllocatedLength
            {
                get { return (byte)((this.Flags >> 8) & 0xFF); }
            }

            public byte Type
            {
                get { return (byte)((this.Flags >> 16) & 0xF); }
            }

            internal static Constant Read(Stream input, Endian endian)
            {
                var instance = new Constant();
                instance.Flags = input.ReadValueU64(endian);
                instance.Value = input.ReadValueU64(endian);
                return instance;
            }
        }

        public struct Function
        {
            public uint NameHash;
            public ushort LocalsCount;
            public ushort ArgCount;
            public ushort[] Instructions;
            public ushort MaxStackDepth;
            public ulong Module;
            public long LinenoPtr;
            public long ColnoPtr;
            public string Name;
        }

        private struct RawFunction
        {
            public uint NameHash;
            public ushort LocalsCount;
            public ushort ArgCount;
            public long InstructionOffset;
            public long InstructionCount;
            public ushort MaxStackDepth;
            public ulong Module;
            public long LinenoPtr;
            public long ColnoPtr;
            public long NameOffset;
            public long NameCount;

            public static RawFunction Read(Stream input, Endian endian)
            {
                var instance = new RawFunction();
                instance.NameHash = input.ReadValueU32(endian);
                instance.LocalsCount = input.ReadValueU16(endian);
                instance.ArgCount = input.ReadValueU16(endian);
                instance.InstructionOffset = input.ReadValueS64(endian);
                instance.InstructionCount = input.ReadValueS64(endian);
                instance.MaxStackDepth = input.ReadValueU16(endian);
                input.Position += 6; // padding
                instance.Module = input.ReadValueU64(endian);
                instance.LinenoPtr = input.ReadValueS64(endian);
                instance.ColnoPtr = input.ReadValueS64(endian);
                instance.NameOffset = input.ReadValueS64(endian);
                instance.NameCount = input.ReadValueS64(endian);
                return instance;
            }
        }
    }
}
