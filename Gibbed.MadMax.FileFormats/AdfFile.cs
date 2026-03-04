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
    public class AdfFile
    {
        public const uint Signature = 0x41444620; // 'ADF '

        #region Fields
        private Endian _Endian;
        private string _Comment;
        private readonly List<TypeDefinition> _TypeDefinitions;
        private readonly List<InstanceInfo> _InstanceInfos;
        #endregion

        public AdfFile()
        {
            this._TypeDefinitions = new List<TypeDefinition>();
            this._InstanceInfos = new List<InstanceInfo>();
        }

        #region Properties
        public Endian Endian
        {
            get { return this._Endian; }
            set { this._Endian = value; }
        }

        public string Comment
        {
            get { return this._Comment; }
            set { this._Comment = value; }
        }

        public List<TypeDefinition> TypeDefinitions
        {
            get { return this._TypeDefinitions; }
        }

        public List<InstanceInfo> InstanceInfos
        {
            get { return this._InstanceInfos; }
        }
        #endregion

        public void Serialize(Stream output)
        {
            var endian = this._Endian;
            var basePosition = output.Position;

            // Build string table by pre-registering all names
            var stringTable = new StringTable(null);
            foreach (var typeDef in this._TypeDefinitions)
            {
                stringTable.Add(typeDef.Name);
                if (typeDef.Members != null)
                {
                    foreach (var member in typeDef.Members)
                    {
                        stringTable.Add(member.Name);
                    }
                }
            }
            foreach (var instanceInfo in this._InstanceInfos)
            {
                stringTable.Add(instanceInfo.Name);
            }

            // Calculate layout
            var comment = this._Comment ?? "";
            var headerSize = 0x40L;
            var commentSize = (long)Encoding.ASCII.GetByteCount(comment) + 1;
            var dataStartOffset = Align(headerSize + commentSize, 16);

            // Assign instance data offsets
            var currentOffset = dataStartOffset;
            for (int i = 0; i < this._InstanceInfos.Count; i++)
            {
                var info = this._InstanceInfos[i];
                info.Offset = (uint)currentOffset;
                info.Size = (uint)(info.Data != null ? info.Data.Length : 0);
                this._InstanceInfos[i] = info;
                currentOffset += info.Size;
                if (i < this._InstanceInfos.Count - 1)
                {
                    currentOffset = Align(currentOffset, 8);
                }
            }

            // Instance info table offset
            var instanceInfoOffset = currentOffset;

            // Type definitions offset (each InstanceInfo entry = 24 bytes)
            var typeDefOffset = instanceInfoOffset + this._InstanceInfos.Count * 24;

            // Calculate type definition total size
            var typeDefTotalSize = 0L;
            foreach (var td in this._TypeDefinitions)
            {
                typeDefTotalSize += 36; // base header
                switch (td.Type)
                {
                    case TypeDefinitionType.Structure:
                        typeDefTotalSize += 4 + (td.Members != null ? td.Members.Length * 32L : 0);
                        break;
                    case TypeDefinitionType.Enumeration:
                        typeDefTotalSize += 4 + (td.Members != null ? td.Members.Length * 12L : 0);
                        break;
                    default:
                        typeDefTotalSize += 4;
                        break;
                }
            }

            // Name table offset and total size
            var nameTableOffset = typeDefOffset + typeDefTotalSize;
            var nameTableSize = stringTable.GetByteCount();
            var totalSize = nameTableOffset + nameTableSize;

            // Write header
            output.Position = basePosition;
            output.WriteValueU32(Signature, endian);
            output.WriteValueU32(4, endian); // version
            output.WriteValueU32((uint)this._InstanceInfos.Count, endian);
            output.WriteValueU32((uint)instanceInfoOffset, endian);
            output.WriteValueU32((uint)this._TypeDefinitions.Count, endian);
            output.WriteValueU32((uint)typeDefOffset, endian);
            output.WriteValueU32(0, endian); // unknown18Count
            output.WriteValueU32(0, endian); // unknown1COffset
            output.WriteValueU32((uint)stringTable.Count, endian);
            output.WriteValueU32((uint)nameTableOffset, endian);
            output.WriteValueU32((uint)totalSize, endian);
            output.WriteValueU32(0, endian); // unknown2C
            output.WriteValueU32(0, endian); // unknown30
            output.WriteValueU32(0, endian); // unknown34
            output.WriteValueU32(0, endian); // unknown38
            output.WriteValueU32(0, endian); // unknown3C

            // Write comment
            output.WriteStringZ(comment, Encoding.ASCII);

            // Pad to data start
            var padCount = dataStartOffset - (output.Position - basePosition);
            for (long i = 0; i < padCount; i++)
            {
                output.WriteValueU8(0);
            }

            // Write instance data
            foreach (var info in this._InstanceInfos)
            {
                output.Position = basePosition + info.Offset;
                if (info.Data != null && info.Data.Length > 0)
                {
                    output.Write(info.Data, 0, info.Data.Length);
                }
            }

            // Write instance info entries
            output.Position = basePosition + instanceInfoOffset;
            foreach (var info in this._InstanceInfos)
            {
                info.Write(output, endian, stringTable);
            }

            // Write type definitions
            output.Position = basePosition + typeDefOffset;
            foreach (var td in this._TypeDefinitions)
            {
                td.Write(output, endian, stringTable);
            }

            // Write name table
            output.Position = basePosition + nameTableOffset;
            stringTable.WriteTo(output);
        }

        private static long Align(long value, long alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }

        public void Deserialize(Stream input)
        {
            var basePosition = input.Position;

            var magic = input.ReadValueU32(Endian.Little);
            if (magic != Signature && magic.Swap() != Signature)
            {
                throw new FormatException();
            }
            var endian = magic == Signature ? Endian.Little : Endian.Big;

            var version = input.ReadValueU32(endian);
            if (version != 4)
            {
                throw new FormatException();
            }

            var instanceCount = input.ReadValueU32(endian);
            var instanceOffset = input.ReadValueU32(endian);
            var typeDefinitionCount = input.ReadValueU32(endian);
            var typeDefinitionOffset = input.ReadValueU32(endian);
            var unknown18Count = input.ReadValueU32(endian);
            var unknown1COffset = input.ReadValueU32(endian);
            var nameTableCount = input.ReadValueU32(endian);
            var nameTableOffset = input.ReadValueU32(endian);
            var totalSize = input.ReadValueU32(endian);
            var unknown2C = input.ReadValueU32(endian);
            var unknown30 = input.ReadValueU32(endian);
            var unknown34 = input.ReadValueU32(endian);
            var unknown38 = input.ReadValueU32(endian);
            var unknown3C = input.ReadValueU32(endian);
            var comment = input.ReadStringZ(Encoding.ASCII);

            if (unknown18Count > 0 || unknown1COffset != 0)
            {
                throw new FormatException();
            }

            if (unknown2C != 0 || unknown30 != 0 || unknown34 != 0 || unknown38 != 0 || unknown3C != 0)
            {
                throw new FormatException();
            }

            if (basePosition + totalSize > input.Length)
            {
                throw new EndOfStreamException();
            }

            var names = new string[nameTableCount];
            if (nameTableCount > 0)
            {
                input.Position = basePosition + nameTableOffset;
                var nameLengths = new byte[nameTableCount];
                for (uint i = 0; i < nameTableCount; i++)
                {
                    nameLengths[i] = input.ReadValueU8();
                }
                for (uint i = 0; i < nameTableCount; i++)
                {
                    names[i] = input.ReadString(nameLengths[i], true, Encoding.ASCII);
                    input.Seek(1, SeekOrigin.Current);
                }
            }
            var stringTable = new StringTable(names);

            var typeDefinitions = new TypeDefinition[typeDefinitionCount];
            if (typeDefinitionCount > 0)
            {
                input.Position = basePosition + typeDefinitionOffset;
                for (uint i = 0; i < typeDefinitionCount; i++)
                {
                    typeDefinitions[i] = TypeDefinition.Read(input, endian, stringTable);
                }
            }

            var instanceInfos = new InstanceInfo[instanceCount];
            if (instanceCount > 0)
            {
                input.Position = basePosition + instanceOffset;
                for (uint i = 0; i < instanceCount; i++)
                {
                    instanceInfos[i] = InstanceInfo.Read(input, endian, stringTable);
                }
            }

            this._Endian = endian;
            this._Comment = comment;
            this._TypeDefinitions.Clear();
            this._TypeDefinitions.AddRange(typeDefinitions);
            this._InstanceInfos.Clear();
            this._InstanceInfos.AddRange(instanceInfos);
        }

        public enum TypeDefinitionType : uint
        {
            Primitive = 0,
            Structure = 1,
            Pointer = 2,
            Array = 3,
            InlineArray = 4,
            String = 5,
            BitField = 7,
            Enumeration = 8,
            StringHash = 9,
        }

        public struct TypeDefinition
        {
            public bool IsEnumeration;
            public uint EnumerationCount;

            public TypeDefinitionType Type;
            public uint Size;
            public uint Alignment;
            public uint NameHash;
            public string Name;
            public uint Flags;
            public uint ElementTypeHash;
            public uint ElementLength;
            public MemberDefinition[] Members;
            public Dictionary<uint, uint> membersEnum;
            

            internal static TypeDefinition Read(Stream input, Endian endian, StringTable stringTable)
            {
                

                var instance = new TypeDefinition();
                {
                    instance.Type = (TypeDefinitionType)input.ReadValueU32(endian);
                    instance.Size = input.ReadValueU32(endian);
                    instance.Alignment = input.ReadValueU32(endian);
                    instance.NameHash = input.ReadValueU32(endian);
                    var nameIndex = input.ReadValueS64(endian);
                    instance.Name = stringTable.Get(nameIndex);
                    instance.Flags = input.ReadValueU32(endian);
                    instance.ElementTypeHash = input.ReadValueU32(endian);
                    instance.ElementLength = input.ReadValueU32(endian);
                }
                

                switch (instance.Type)
                {
                    case TypeDefinitionType.Structure:
                    {
                        var memberCount = input.ReadValueU32(endian);
                        instance.Members = new MemberDefinition[memberCount];
                        for (uint i = 0; i < memberCount; i++)
                        {
                            instance.Members[i] = MemberDefinition.Read(input, endian, stringTable);
                        }
                        break;
                    }

                    case TypeDefinitionType.Array:
                    {
                        var memberCount = input.ReadValueU32(endian);
                        if (memberCount != 0)
                        {
                            throw new FormatException();
                        }
                        break;
                    }

                    case TypeDefinitionType.InlineArray:
                    {
                            var memberCount = input.ReadValueU32(endian);
                            Console.WriteLine("InlineArray " + memberCount);
                            if (memberCount != 0)
                            {
                                throw new FormatException();
                            }

                            break;
                    }

                    case TypeDefinitionType.Pointer:
                    case TypeDefinitionType.BitField:
                    {
                            var unknown = input.ReadValueU32(endian);
                            if (unknown != 0)
                            {
                                throw new FormatException();
                            }
                            break;
                        }

                    case TypeDefinitionType.Enumeration:
                        {
                            instance.EnumerationCount = input.ReadValueU32(endian);
                            instance.IsEnumeration = true;
                            instance.Members = new MemberDefinition[instance.EnumerationCount];
                            instance.membersEnum = new Dictionary<uint, uint>();
                            for (uint i = 0; i < instance.EnumerationCount; i++)
                            {
                                instance.Members[i] = MemberDefinition.ReadEnum(input, endian, stringTable);
                                instance.membersEnum.Add(instance.Members[i].EnumId, i);
                                Console.WriteLine("Enum: "+instance.Name+"::"+instance.Members[i].Name+" = "+ instance.Members[i].EnumId);
                            }
                            break;
                        }

                    default:
                    {
                        throw new NotSupportedException();
                    }
                }

                return instance;
            }

            internal void Write(Stream output, Endian endian, StringTable stringTable)
            {
                output.WriteValueU32((uint)this.Type, endian);
                output.WriteValueU32(this.Size, endian);
                output.WriteValueU32(this.Alignment, endian);
                output.WriteValueU32(this.NameHash, endian);
                output.WriteValueS64(stringTable.Add(this.Name), endian);
                output.WriteValueU32(this.Flags, endian);
                output.WriteValueU32(this.ElementTypeHash, endian);
                output.WriteValueU32(this.ElementLength, endian);

                switch (this.Type)
                {
                    case TypeDefinitionType.Structure:
                    {
                        output.WriteValueU32((uint)(this.Members != null ? this.Members.Length : 0), endian);
                        if (this.Members != null)
                        {
                            foreach (var member in this.Members)
                            {
                                member.Write(output, endian, stringTable);
                            }
                        }
                        break;
                    }

                    case TypeDefinitionType.Array:
                    case TypeDefinitionType.InlineArray:
                    case TypeDefinitionType.Pointer:
                    case TypeDefinitionType.BitField:
                    {
                        output.WriteValueU32(0, endian);
                        break;
                    }

                    case TypeDefinitionType.Enumeration:
                    {
                        output.WriteValueU32(this.EnumerationCount, endian);
                        if (this.Members != null)
                        {
                            foreach (var member in this.Members)
                            {
                                member.WriteEnum(output, endian, stringTable);
                            }
                        }
                        break;
                    }

                    default:
                    {
                        throw new NotSupportedException();
                    }
                }
            }

            public override string ToString()
            {
                return string.Format("{0} ({1:X})", this.Name, this.NameHash);
            }
        }

        public struct MemberDefinition
        {
            public bool IsEnum;
            public uint EnumId;
            public string Name;
            public uint TypeHash;
            public uint Size;
            public uint Offset;
            public uint Unknown10;
            public uint Unknown14;
            public uint Unknown18;

            internal static MemberDefinition Read(Stream input, Endian endian, StringTable stringTable)
            {
                var instance = new MemberDefinition();
                var nameIndex = input.ReadValueS64(endian);
                instance.Name = stringTable.Get(nameIndex);
                instance.TypeHash = input.ReadValueU32(endian);
                instance.Size = input.ReadValueU32(endian);
                instance.Offset = input.ReadValueU32(endian);
                instance.Unknown10 = input.ReadValueU32(endian);
                instance.Unknown14 = input.ReadValueU32(endian);
                instance.Unknown18 = input.ReadValueU32(endian);

                Console.WriteLine("{4:X} MemberDefinition {0} {1:X} UNK {2:X} {3:X}", instance.Name, instance.Offset, instance.Unknown14, instance.Unknown18, input.Position);

                return instance;
            }

            internal static MemberDefinition ReadEnum(Stream input, Endian endian, StringTable stringTable)
            {
                var instance = new MemberDefinition();
                var nameIndex = input.ReadValueS64(endian);
                instance.Name = stringTable.Get(nameIndex);
                instance.IsEnum = true;
                instance.EnumId = input.ReadValueU32(endian);
                return instance;
            }

            internal void Write(Stream output, Endian endian, StringTable stringTable)
            {
                output.WriteValueS64(stringTable.Add(this.Name), endian);
                output.WriteValueU32(this.TypeHash, endian);
                output.WriteValueU32(this.Size, endian);
                output.WriteValueU32(this.Offset, endian);
                output.WriteValueU32(this.Unknown10, endian);
                output.WriteValueU32(this.Unknown14, endian);
                output.WriteValueU32(this.Unknown18, endian);
            }

            internal void WriteEnum(Stream output, Endian endian, StringTable stringTable)
            {
                output.WriteValueS64(stringTable.Add(this.Name), endian);
                output.WriteValueU32(this.EnumId, endian);
            }

            public override string ToString()
            {
                return string.Format("{0} ({1:X}) @ {2:X}", this.Name, this.TypeHash, this.Offset);
            }
        }

        public struct InstanceInfo
        {
            public uint NameHash;
            public uint TypeHash;
            public uint Offset;
            public uint Size;
            public string Name;
            public byte[] Data;

            internal static InstanceInfo Read(Stream input, Endian endian, StringTable stringTable)
            {
                var instance = new InstanceInfo();
                instance.NameHash = input.ReadValueU32(endian);
                instance.TypeHash = input.ReadValueU32(endian);
                instance.Offset = input.ReadValueU32(endian);
                instance.Size = input.ReadValueU32(endian);
                var nameIndex = input.ReadValueS64(endian);
                instance.Name = stringTable.Get(nameIndex);
                return instance;
            }

            internal void Write(Stream output, Endian endian, StringTable stringTable)
            {
                output.WriteValueU32(this.NameHash, endian);
                output.WriteValueU32(this.TypeHash, endian);
                output.WriteValueU32(this.Offset, endian);
                output.WriteValueU32(this.Size, endian);
                output.WriteValueS64(stringTable.Add(this.Name), endian);
            }

            public override string ToString()
            {
                return string.Format("{0} ({1:X})", this.Name, this.TypeHash);
            }
        }

        internal class StringTable
        {
            private readonly List<string> _Table;

            public StringTable(string[] names)
            {
                this._Table = names == null ? new List<string>() : new List<string>(names);
            }

            public int Count
            {
                get { return this._Table.Count; }
            }

            public string Get(long index)
            {
                if (index < 0 || index >= this._Table.Count || index > int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException("index");
                }

                return this._Table[(int)index];
            }

            public long Add(string text)
            {
                var index = this._Table.IndexOf(text);
                if (index >= 0)
                {
                    return index;
                }
                index = this._Table.Count;
                this._Table.Add(text);
                return index;
            }

            public void WriteTo(Stream output)
            {
                // Write length bytes
                foreach (var name in this._Table)
                {
                    output.WriteValueU8((byte)Encoding.ASCII.GetByteCount(name));
                }
                // Write strings with null terminators
                foreach (var name in this._Table)
                {
                    output.WriteString(name, Encoding.ASCII);
                    output.WriteValueU8(0);
                }
            }

            public long GetByteCount()
            {
                long total = this._Table.Count; // length bytes
                foreach (var name in this._Table)
                {
                    total += Encoding.ASCII.GetByteCount(name) + 1; // string + null
                }
                return total;
            }
        }
    }
}
