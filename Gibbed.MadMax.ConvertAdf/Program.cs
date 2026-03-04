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
using System.Text;
using System.Xml;
using Gibbed.IO;
using NDesk.Options;
using MemberDefinition = Gibbed.MadMax.FileFormats.AdfFile.MemberDefinition;
using TypeDefinition = Gibbed.MadMax.FileFormats.AdfFile.TypeDefinition;
using TypeDefinitionType = Gibbed.MadMax.FileFormats.AdfFile.TypeDefinitionType;

namespace Gibbed.MadMax.ConvertAdf
{
    internal class Program
    {
        private static string GetExecutableName()
        {
            return Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        private static void SetOption<T>(string s, ref T variable, T value)
        {
            if (s == null)
            {
                return;
            }

            variable = value;
        }

        internal enum Mode
        {
            Unknown,
            Export,
            Import,
        }

        private static void Main(string[] args)
        {
            var mode = Mode.Unknown;
            bool showHelp = false;
            var typeLibraryPaths = new List<string>();

            var options = new OptionSet
            {
                // ReSharper disable AccessToModifiedClosure
                { "e|export", "convert from binary to XML", v => SetOption(v, ref mode, Mode.Export) },
                { "i|import", "convert from XML to binary", v => SetOption(v, ref mode, Mode.Import) },
                // ReSharper restore AccessToModifiedClosure
                { "t|type-library=", "load type library from file", v => typeLibraryPaths.Add(v) },
                { "h|help", "show this message and exit", v => showHelp = v != null },
            };

            List<string> extras;

            //args = new string[1];
            //args[0] = "C:\\get_player_scrap.gsrc";//"C:\\mapicons.mapiconsc";
            //typeLibraryPaths.Add("C:\\gsrc.adf");
            //args[0] = "C:\\mapicons.mapiconsc";
            //args[0] = "C:\\map.guixc";
            //typeLibraryPaths.Add("C:\\guixc.adf");
            //args[0] = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Mad Max\\archives_win64\\unpacked\\global\\location_info_unpack\\global\\location_info.locationinfoc";
            try
            {
                extras = options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("{0}: ", GetExecutableName());
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `{0} --help' for more information.", GetExecutableName());
                return;
            }

            if (mode == Mode.Unknown && extras.Count >= 1)
            {
                var extension = Path.GetExtension(extras[0]);
                if (extension != null && extension.ToLowerInvariant() == ".xml")
                {
                    mode = Mode.Import;
                }
                else
                {
                    mode = Mode.Export;
                }
            }

            if (extras.Count < 1 || extras.Count > 2 ||
                showHelp == true ||
                mode == Mode.Unknown)
            {
                Console.WriteLine("Usage: {0} [OPTIONS]+ [-e] input_adf [output_xml]", GetExecutableName());
                Console.WriteLine("       {0} [OPTIONS]+ [-i] input_xml [output_adf]", GetExecutableName());
                Console.WriteLine("Convert an ADF file between binary and XML format.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            var runtime = new RuntimeTypeLibrary();
            foreach (var typeLibraryPath in typeLibraryPaths)
            {
                var adf = new FileFormats.AdfFile();

                using (var input = File.OpenRead(typeLibraryPath))
                {
                    adf.Deserialize(input);
                }

                if (adf.InstanceInfos.Count > 0)
                {
                    //throw new InvalidOperationException();
                }

                runtime.AddTypeDefinitions(adf);
            }

            if (mode == Mode.Export)
            {
                string inputPath = extras[0];
                string outputPath = extras.Count > 1 ? extras[1] : inputPath + ".xml";

                var adf = new FileFormats.AdfFile();
                using (var input = File.OpenRead(inputPath))
                {
                    adf.Deserialize(input);
                    var endian = adf.Endian;

                    runtime.AddTypeDefinitions(adf);

                    var settings = new XmlWriterSettings
                    {
                        Indent = true,
                        IndentChars = "    ",
                        CheckCharacters = false,
                    };

                    using (var output = File.Create(outputPath))
                    {
                        var writer = XmlWriter.Create(output, settings);
                        writer.WriteStartDocument();
                        writer.WriteStartElement("adf");

                        // Write type definitions
                        if (adf.TypeDefinitions.Count > 0)
                        {
                            writer.WriteStartElement("typedefs");
                            foreach (var typeDef in adf.TypeDefinitions)
                            {
                                WriteTypeDefinition(writer, typeDef);
                            }
                            writer.WriteEndElement();
                        }

                        // Write instances
                        if (adf.InstanceInfos.Count > 0)
                        {
                            writer.WriteStartElement("instances");

                            foreach (var instanceInfo in adf.InstanceInfos)
                            {
                                writer.WriteStartElement("instance");
                                Console.WriteLine(instanceInfo.Name);

                                writer.WriteAttributeString("root", instanceInfo.Name);
                                writer.WriteAttributeString("nameHash", instanceInfo.NameHash.ToString("X8"));
                                writer.WriteAttributeString("typeHash", instanceInfo.TypeHash.ToString("X8"));

                                var typeDefinition = runtime.GetTypeDefinition(instanceInfo.TypeHash);
                                input.Position = instanceInfo.Offset;
                                Console.WriteLine("TypeDef FilePos Data {0:X}", input.Position);
                                using (var data = input.ReadToMemoryStream((int)instanceInfo.Size))
                                {
                                    WriteInstance(typeDefinition, instanceInfo.Name, data, writer, endian, runtime);
                                }

                                writer.WriteEndElement();
                            }

                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                        writer.WriteEndDocument();
                        writer.Flush();
                    }
                }
            }
            else if (mode == Mode.Import)
            {
                Import(extras, runtime);
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        private struct WorkItem
        {
            public long Id;
            public string Name;
            public TypeDefinition TypeDefinition;
            public long Offset;

            public WorkItem(long id, string name, TypeDefinition typeDefinition, long offset)
            {
                this.Id = id;
                this.Name = name;
                this.TypeDefinition = typeDefinition;
                this.Offset = offset;
            }
        }

        private static void WriteInstance(TypeDefinition rootTypeDefinition,
                                          string name,
                                          MemoryStream data,
                                          XmlWriter writer,
                                          Endian endian,
                                          RuntimeTypeLibrary runtime)
        {
            long counter = 0;
            var queue = new Queue<WorkItem>();
            queue.Enqueue(new WorkItem(counter++, name, rootTypeDefinition, 0));

            while (queue.Count > 0)
            {
                var workItem = queue.Dequeue();
                Console.WriteLine(workItem.Name);
                switch (workItem.TypeDefinition.Type)
                {
                    case TypeDefinitionType.Structure:
                    {
                        data.Position = workItem.Offset;
                        WriteStructure(
                            writer,
                            workItem.TypeDefinition,
                            workItem.Id,
                            workItem.Name,
                            data,
                            endian,
                            runtime,
                            ref counter,
                            queue);
                        break;
                    }

                    case TypeDefinitionType.Array:
                    {
                        data.Position = workItem.Offset;
                        WriteArray(
                            writer,
                            workItem.TypeDefinition,
                            workItem.Id,
                            data,
                            endian,
                            runtime,
                            ref counter,
                            queue);
                        break;
                    }

                    default:
                    {   
                            data.Position = workItem.Offset;
                        WriteArray(
                            writer,
                            workItem.TypeDefinition,
                            workItem.Id,
                            data,
                            endian,
                            runtime,
                            ref counter,
                            queue);
                            break;
                        //throw new NotImplementedException();
                    }
                }
            }
        }

        private static void WriteStructure(XmlWriter writer,
                                           TypeDefinition typeDefinition,
                                           long id,
                                           string name,
                                           MemoryStream data,
                                           Endian endian,
                                           RuntimeTypeLibrary runtime,
                                           ref long counter,
                                           Queue<WorkItem> queue)
        {
            var basePosition = data.Position;

            writer.WriteStartElement("struct");
            writer.WriteAttributeString("type", typeDefinition.Name);

            if (name != null)
            {
                writer.WriteAttributeString("name", name);
            }

            if (id >= 0)
            {
                writer.WriteAttributeString("id", "#" + id);
            }

            foreach (var memberDefinition in typeDefinition.Members)
            {
                data.Position = basePosition + memberDefinition.Offset;
                WriteMember(writer, data, endian, runtime, memberDefinition, ref counter, queue);
            }

            writer.WriteEndElement();
        }

        private static void WriteMember(XmlWriter writer,
                                        MemoryStream data,
                                        Endian endian,
                                        RuntimeTypeLibrary runtime,
                                        MemberDefinition memberDefinition,
                                        ref long counter,
                                        Queue<WorkItem> queue)
        {
            writer.WriteStartElement("member");
            writer.WriteAttributeString("name", memberDefinition.Name);

            switch (memberDefinition.TypeHash)
            {
                case TypeHashes.Primitive.UInt8:
                    {
                        var value = data.ReadValueU8();
                        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    }

                case TypeHashes.Primitive.Int8:
                    {
                        var value = data.ReadValueS8();
                        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    }

                case TypeHashes.Primitive.UInt16:
                {
                    var value = data.ReadValueU16(endian);
                    writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                case TypeHashes.Primitive.UInt32:
                {
                    var value = data.ReadValueU32(endian);
                    writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                case TypeHashes.Primitive.UInt64:
                {
                    var value = data.ReadValueU64(endian);
                    writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                case TypeHashes.Primitive.Int16:
                    {
                        var value = data.ReadValueS16(endian);
                        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    }

                case TypeHashes.Primitive.Int32:
                    {
                        var value = data.ReadValueS32(endian);
                        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    }

                case TypeHashes.Primitive.Int64:
                    {
                        var value = data.ReadValueS64(endian);
                        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    }

                case TypeHashes.Primitive.Float:
                    {
                        var value = data.ReadValueF32(endian);
                        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    }

                case TypeHashes.Primitive.Double:
                    {
                        var value = data.ReadValueF64(endian);
                        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    }

                case TypeHashes.Primitive.String:
                    {
                        var offset = data.ReadValueS64(endian);
                        data.Position = offset;
                        var value = data.ReadStringZ(Encoding.UTF8);
                        Console.WriteLine(value);
                        writer.WriteValue(value);
                        break;
                    }

                default:
                {
                    var typeDefinition = runtime.GetTypeDefinition(memberDefinition.TypeHash);
                    switch (typeDefinition.Type)
                    {
                        case TypeDefinitionType.Structure:
                        {
                            WriteStructure(writer, typeDefinition, -1, null, data, endian, runtime, ref counter, queue);
                            break;
                        }

                        case TypeDefinitionType.Array:
                        {
                            var id = counter++;
                            queue.Enqueue(new WorkItem(id, null, typeDefinition, data.Position));
                            writer.WriteValue("#" + id.ToString(CultureInfo.InvariantCulture));
                            break;
                        }

                            case TypeDefinitionType.InlineArray:
                                {
                                    WriteArrayItems(writer,
                                                         typeDefinition,
                                                         -1,
                                                         data, endian, runtime, ref counter, queue, data.Position, typeDefinition.ElementLength);
                                    break;
                                }

                            case TypeDefinitionType.Pointer:
                                {
                                    writer.WriteValue("POINTER");
                                    break;
                                }

                            case TypeDefinitionType.BitField:
                                {
                                    writer.WriteValue("BitField:UNK");
                                    break;
                                }

                            case TypeDefinitionType.Enumeration:
                                {
                                    var enumID = data.ReadValueU32(endian);
                                    writer.WriteValue(typeDefinition.Members[typeDefinition.membersEnum[enumID]].Name+":"+enumID);
                                    break;
                                }

                            default:
                            {
                                throw new NotSupportedException();
                            }
                    }

                    break;
                }
            }

            writer.WriteEndElement();
        }

        private static void WriteArray(XmlWriter writer,
                                       TypeDefinition typeDefinition,
                                       long id,
                                       MemoryStream data,
                                       Endian endian,
                                       RuntimeTypeLibrary runtime,
                                       ref long counter,
                                       Queue<WorkItem> queue)
        {
            var offset = data.ReadValueS64(endian);
            var count = data.ReadValueS64(endian);
            WriteArrayItems(writer,
                                 typeDefinition,
                                 id,
                                 data, endian, runtime, ref counter, queue,
                                 offset, count);
        }

        private static void WriteArrayItems(XmlWriter writer,
                                       TypeDefinition typeDefinition,
                                       long id,
                                       MemoryStream data,
                                       Endian endian,
                                       RuntimeTypeLibrary runtime,
                                       ref long counter,
                                       Queue<WorkItem> queue, long offset, long count)
        {
            writer.WriteStartElement("array");

            if (id >= 0)
            {
                writer.WriteAttributeString("id", "#" + id);
            }

            //Console.WriteLine("Write Array {0:X}", data.Position);

            switch (typeDefinition.ElementTypeHash)
            {
                case TypeHashes.Primitive.UInt8:
                {
                    data.Position = offset;
                    var sb = new StringBuilder();
                    for (long i = 0; i < count; i++)
                    {
                        var value = data.ReadValueU8();
                        sb.Append(value.ToString(CultureInfo.InvariantCulture));
                        sb.Append(" ");
                    }
                    writer.WriteValue(sb.ToString());
                    break;
                }

                case TypeHashes.Primitive.Int8:
                    {
                        data.Position = offset;
                        var sb = new StringBuilder();
                        for (long i = 0; i < count; i++)
                        {
                            var value = data.ReadValueS8();
                            sb.Append(value.ToString(CultureInfo.InvariantCulture));
                            sb.Append(" ");
                        }
                        writer.WriteValue(sb.ToString());
                        break;
                    }

                case TypeHashes.Primitive.UInt16:
                {
                    data.Position = offset;
                    var sb = new StringBuilder();
                    for (long i = 0; i < count; i++)
                    {
                        var value = data.ReadValueU16(endian);
                        sb.Append(value.ToString(CultureInfo.InvariantCulture));
                        sb.Append(" ");
                    }
                    writer.WriteValue(sb.ToString());
                    break;
                }

                case TypeHashes.Primitive.Int16:
                    {
                        data.Position = offset;
                        var sb = new StringBuilder();
                        for (long i = 0; i < count; i++)
                        {
                            var value = data.ReadValueS16(endian);
                            sb.Append(value.ToString(CultureInfo.InvariantCulture));
                            sb.Append(" ");
                        }
                        writer.WriteValue(sb.ToString());
                        break;
                    }


                case TypeHashes.Primitive.UInt32:
                {
                    data.Position = offset;
                    var sb = new StringBuilder();
                    for (long i = 0; i < count; i++)
                    {
                        var value = data.ReadValueU32(endian);
                        sb.Append(value.ToString(CultureInfo.InvariantCulture));
                        sb.Append(" ");
                    }
                    writer.WriteValue(sb.ToString());
                    break;
                }

                case TypeHashes.Primitive.Int32:
                    {
                        data.Position = offset;
                        var sb = new StringBuilder();
                        for (long i = 0; i < count; i++)
                        {
                            var value = data.ReadValueS32(endian);
                            sb.Append(value.ToString(CultureInfo.InvariantCulture));
                            sb.Append(" ");
                        }
                        writer.WriteValue(sb.ToString());
                        break;
                    }

                case TypeHashes.Primitive.UInt64:
                    {
                        data.Position = offset;
                        var sb = new StringBuilder();
                        for (long i = 0; i < count; i++)
                        {
                            var value = data.ReadValueU64(endian);
                            sb.Append(value.ToString(CultureInfo.InvariantCulture));
                            sb.Append(" ");
                        }
                        writer.WriteValue(sb.ToString());
                        break;
                    }

                case TypeHashes.Primitive.Int64:
                    {
                        data.Position = offset;
                        var sb = new StringBuilder();
                        for (long i = 0; i < count; i++)
                        {
                            var value = data.ReadValueS64(endian);
                            sb.Append(value.ToString(CultureInfo.InvariantCulture));
                            sb.Append(" ");
                        }
                        writer.WriteValue(sb.ToString());
                        break;
                    }

                case TypeHashes.Primitive.Float:
                    {
                        data.Position = offset;
                        var sb = new StringBuilder();
                        for (long i = 0; i < count; i++)
                        {
                            var value = data.ReadValueF32(endian);
                            sb.Append(value.ToString(CultureInfo.InvariantCulture));
                            sb.Append(" ");
                        }
                        writer.WriteValue(sb.ToString());
                        break;
                    }

                case TypeHashes.Primitive.Double:
                    {
                        data.Position = offset;
                        var sb = new StringBuilder();
                        for (long i = 0; i < count; i++)
                        {
                            var value = data.ReadValueF64(endian);
                            sb.Append(value.ToString(CultureInfo.InvariantCulture));
                            sb.Append(" ");
                        }
                        writer.WriteValue(sb.ToString());
                        break;
                    }

                default:
                {
                    var elementTypeDefinition = runtime.GetTypeDefinition(typeDefinition.ElementTypeHash);
                    switch (elementTypeDefinition.Type)
                    {
                        case TypeDefinitionType.Structure:
                        {
                            for (long i = 0; i < count; i++)
                            {
                                data.Position = offset + (i * elementTypeDefinition.Size);
                                WriteStructure(
                                    writer,
                                    elementTypeDefinition,
                                    -1,
                                    null,
                                    data,
                                    endian,
                                    runtime,
                                    ref counter,
                                    queue);
                            }
                            break;
                        }

                        default:
                        {
                            throw new NotSupportedException();
                        }
                    }
                    break;
                }
            }

            writer.WriteEndElement();
        }

        private static void WriteTypeDefinition(XmlWriter writer, TypeDefinition typeDef)
        {
            writer.WriteStartElement("typedef");
            writer.WriteAttributeString("name", typeDef.Name);
            writer.WriteAttributeString("hash", typeDef.NameHash.ToString("X8"));
            writer.WriteAttributeString("type", typeDef.Type.ToString());
            writer.WriteAttributeString("size", typeDef.Size.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("alignment", typeDef.Alignment.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("flags", typeDef.Flags.ToString("X8"));
            writer.WriteAttributeString("elementTypeHash", typeDef.ElementTypeHash.ToString("X8"));
            writer.WriteAttributeString("elementLength", typeDef.ElementLength.ToString(CultureInfo.InvariantCulture));

            if (typeDef.Type == TypeDefinitionType.Structure && typeDef.Members != null)
            {
                foreach (var member in typeDef.Members)
                {
                    writer.WriteStartElement("member");
                    writer.WriteAttributeString("name", member.Name);
                    writer.WriteAttributeString("typeHash", member.TypeHash.ToString("X8"));
                    writer.WriteAttributeString("size", member.Size.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("offset", member.Offset.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("unknown10", member.Unknown10.ToString("X8"));
                    writer.WriteAttributeString("unknown14", member.Unknown14.ToString("X8"));
                    writer.WriteAttributeString("unknown18", member.Unknown18.ToString("X8"));
                    writer.WriteEndElement();
                }
            }
            else if (typeDef.Type == TypeDefinitionType.Enumeration && typeDef.Members != null)
            {
                foreach (var member in typeDef.Members)
                {
                    writer.WriteStartElement("enum");
                    writer.WriteAttributeString("name", member.Name);
                    writer.WriteAttributeString("id", member.EnumId.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }
            }

            writer.WriteEndElement();
        }

        #region Import

        private static void Import(List<string> extras, RuntimeTypeLibrary runtime)
        {
            string inputPath = extras[0];
            string outputPath;
            if (extras.Count > 1)
            {
                outputPath = extras[1];
            }
            else if (inputPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                // Remove trailing .xml to restore original filename
                outputPath = inputPath.Substring(0, inputPath.Length - 4);
            }
            else
            {
                outputPath = Path.ChangeExtension(inputPath, null);
            }

            var doc = new XmlDocument();
            doc.Load(inputPath);

            var endian = Endian.Little;

            // Read type definitions from XML if present
            var typedefsNode = doc.SelectSingleNode("/adf/typedefs");
            if (typedefsNode != null)
            {
                foreach (XmlNode typedefNode in typedefsNode.ChildNodes)
                {
                    if (typedefNode.NodeType != XmlNodeType.Element || typedefNode.Name != "typedef")
                    {
                        continue;
                    }

                    var td = ParseTypeDefinitionFromXml(typedefNode);
                    if (runtime.TypeDefinitions.ContainsKey(td.NameHash) == false)
                    {
                        runtime.TypeDefinitions.Add(td.NameHash, td);
                    }
                }
            }

            var adf = new FileFormats.AdfFile();
            adf.Endian = endian;
            adf.Comment = "";

            var usedTypeHashes = new HashSet<uint>();

            // Process instances
            var instancesNode = doc.SelectSingleNode("/adf/instances");
            if (instancesNode != null)
            {
                foreach (XmlNode instanceNode in instancesNode.ChildNodes)
                {
                    if (instanceNode.NodeType != XmlNodeType.Element || instanceNode.Name != "instance")
                    {
                        continue;
                    }

                    var rootName = instanceNode.Attributes["root"].Value;

                    uint nameHash = 0;
                    uint typeHash = 0;
                    if (instanceNode.Attributes["nameHash"] != null)
                    {
                        nameHash = uint.Parse(instanceNode.Attributes["nameHash"].Value, NumberStyles.HexNumber);
                    }
                    if (instanceNode.Attributes["typeHash"] != null)
                    {
                        typeHash = uint.Parse(instanceNode.Attributes["typeHash"].Value, NumberStyles.HexNumber);
                    }

                    // Index all elements by id
                    var elementsById = new Dictionary<string, XmlNode>();
                    foreach (XmlNode child in instanceNode.ChildNodes)
                    {
                        if (child.NodeType != XmlNodeType.Element)
                        {
                            continue;
                        }

                        var idAttr = child.Attributes["id"];
                        if (idAttr != null)
                        {
                            elementsById[idAttr.Value] = child;
                        }
                    }

                    // Find the root struct
                    XmlNode rootStructNode = null;
                    foreach (XmlNode child in instanceNode.ChildNodes)
                    {
                        if (child.NodeType == XmlNodeType.Element && child.Name == "struct")
                        {
                            rootStructNode = child;
                            break;
                        }
                    }

                    if (rootStructNode == null)
                    {
                        throw new FormatException("Instance '" + rootName + "' has no root struct element");
                    }

                    var typeName = rootStructNode.Attributes["type"].Value;
                    TypeDefinition rootTypeDef;
                    try
                    {
                        rootTypeDef = runtime.GetTypeDefinitionByName(typeName);
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new FormatException("Type definition '" + typeName + "' not found. Use -t to load type library.");
                    }

                    if (typeHash == 0)
                    {
                        typeHash = rootTypeDef.NameHash;
                    }
                    if (nameHash == 0)
                    {
                        nameHash = typeHash; // fallback
                    }

                    usedTypeHashes.Add(rootTypeDef.NameHash);

                    // Build binary data
                    var data = new MemoryStream();
                    data.SetLength(rootTypeDef.Size);
                    data.Position = 0;

                    ReadStructureFromXml(data, 0, rootStructNode, rootTypeDef, endian, runtime, elementsById, usedTypeHashes);

                    var instanceInfo = new FileFormats.AdfFile.InstanceInfo();
                    instanceInfo.NameHash = nameHash;
                    instanceInfo.TypeHash = typeHash;
                    instanceInfo.Name = rootName;
                    instanceInfo.Data = data.ToArray();

                    adf.InstanceInfos.Add(instanceInfo);
                }
            }

            // Add all type definitions from the runtime library
            foreach (var kvp in runtime.TypeDefinitions)
            {
                adf.TypeDefinitions.Add(kvp.Value);
            }

            using (var output = File.Create(outputPath))
            {
                adf.Serialize(output);
            }

            Console.WriteLine("Written: {0}", outputPath);
        }

        private static TypeDefinition ParseTypeDefinitionFromXml(XmlNode node)
        {
            var td = new TypeDefinition();
            td.Name = node.Attributes["name"].Value;
            td.NameHash = uint.Parse(node.Attributes["hash"].Value, NumberStyles.HexNumber);
            td.Type = (TypeDefinitionType)Enum.Parse(typeof(TypeDefinitionType), node.Attributes["type"].Value);
            td.Size = uint.Parse(node.Attributes["size"].Value, CultureInfo.InvariantCulture);
            td.Alignment = uint.Parse(node.Attributes["alignment"].Value, CultureInfo.InvariantCulture);
            td.Flags = uint.Parse(node.Attributes["flags"].Value, NumberStyles.HexNumber);
            td.ElementTypeHash = uint.Parse(node.Attributes["elementTypeHash"].Value, NumberStyles.HexNumber);
            td.ElementLength = uint.Parse(node.Attributes["elementLength"].Value, CultureInfo.InvariantCulture);

            var members = new List<MemberDefinition>();

            if (td.Type == TypeDefinitionType.Structure)
            {
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element || child.Name != "member")
                    {
                        continue;
                    }

                    var md = new MemberDefinition();
                    md.Name = child.Attributes["name"].Value;
                    md.TypeHash = uint.Parse(child.Attributes["typeHash"].Value, NumberStyles.HexNumber);
                    md.Size = uint.Parse(child.Attributes["size"].Value, CultureInfo.InvariantCulture);
                    md.Offset = uint.Parse(child.Attributes["offset"].Value, CultureInfo.InvariantCulture);
                    md.Unknown10 = uint.Parse(child.Attributes["unknown10"].Value, NumberStyles.HexNumber);
                    md.Unknown14 = uint.Parse(child.Attributes["unknown14"].Value, NumberStyles.HexNumber);
                    md.Unknown18 = uint.Parse(child.Attributes["unknown18"].Value, NumberStyles.HexNumber);
                    members.Add(md);
                }
                td.Members = members.ToArray();
            }
            else if (td.Type == TypeDefinitionType.Enumeration)
            {
                td.membersEnum = new Dictionary<uint, uint>();
                td.IsEnumeration = true;
                uint enumIndex = 0;
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.NodeType != XmlNodeType.Element || child.Name != "enum")
                    {
                        continue;
                    }

                    var md = new MemberDefinition();
                    md.Name = child.Attributes["name"].Value;
                    md.IsEnum = true;
                    md.EnumId = uint.Parse(child.Attributes["id"].Value, CultureInfo.InvariantCulture);
                    members.Add(md);
                    td.membersEnum.Add(md.EnumId, enumIndex);
                    enumIndex++;
                }
                td.Members = members.ToArray();
                td.EnumerationCount = (uint)members.Count;
            }

            return td;
        }

        private static void ReadStructureFromXml(
            MemoryStream data,
            long baseOffset,
            XmlNode structNode,
            TypeDefinition typeDef,
            Endian endian,
            RuntimeTypeLibrary runtime,
            Dictionary<string, XmlNode> elementsById,
            HashSet<uint> usedTypes)
        {
            usedTypes.Add(typeDef.NameHash);

            if (typeDef.Members == null)
            {
                return;
            }

            foreach (var memberDef in typeDef.Members)
            {
                // Find the corresponding <member> element
                XmlNode memberNode = null;
                foreach (XmlNode child in structNode.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element && child.Name == "member" &&
                        child.Attributes["name"] != null && child.Attributes["name"].Value == memberDef.Name)
                    {
                        memberNode = child;
                        break;
                    }
                }

                if (memberNode == null)
                {
                    continue;
                }

                data.Position = baseOffset + memberDef.Offset;
                ReadMemberFromXml(data, memberNode, memberDef, endian, runtime, elementsById, usedTypes);
            }
        }

        private static void ReadMemberFromXml(
            MemoryStream data,
            XmlNode memberNode,
            MemberDefinition memberDef,
            Endian endian,
            RuntimeTypeLibrary runtime,
            Dictionary<string, XmlNode> elementsById,
            HashSet<uint> usedTypes)
        {
            var memberPosition = data.Position;

            switch (memberDef.TypeHash)
            {
                case TypeHashes.Primitive.UInt8:
                    data.WriteValueU8(byte.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture));
                    break;

                case TypeHashes.Primitive.Int8:
                    data.WriteValueS8(sbyte.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture));
                    break;

                case TypeHashes.Primitive.UInt16:
                    data.WriteValueU16(ushort.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture), endian);
                    break;

                case TypeHashes.Primitive.Int16:
                    data.WriteValueS16(short.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture), endian);
                    break;

                case TypeHashes.Primitive.UInt32:
                    data.WriteValueU32(uint.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture), endian);
                    break;

                case TypeHashes.Primitive.Int32:
                    data.WriteValueS32(int.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture), endian);
                    break;

                case TypeHashes.Primitive.UInt64:
                    data.WriteValueU64(ulong.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture), endian);
                    break;

                case TypeHashes.Primitive.Int64:
                    data.WriteValueS64(long.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture), endian);
                    break;

                case TypeHashes.Primitive.Float:
                    data.WriteValueF32(float.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture), endian);
                    break;

                case TypeHashes.Primitive.Double:
                    data.WriteValueF64(double.Parse(memberNode.InnerText.Trim(), CultureInfo.InvariantCulture), endian);
                    break;

                case TypeHashes.Primitive.String:
                {
                    var text = memberNode.InnerText;
                    var stringOffset = data.Length;
                    data.Position = stringOffset;
                    data.WriteStringZ(text, Encoding.UTF8);
                    data.Position = memberPosition;
                    data.WriteValueS64(stringOffset, endian);
                    break;
                }

                default:
                {
                    var typeDef = runtime.GetTypeDefinition(memberDef.TypeHash);
                    usedTypes.Add(typeDef.NameHash);

                    switch (typeDef.Type)
                    {
                        case TypeDefinitionType.Structure:
                        {
                            XmlNode childStruct = null;
                            foreach (XmlNode child in memberNode.ChildNodes)
                            {
                                if (child.NodeType == XmlNodeType.Element && child.Name == "struct")
                                {
                                    childStruct = child;
                                    break;
                                }
                            }
                            if (childStruct != null)
                            {
                                ReadStructureFromXml(data, memberPosition, childStruct, typeDef, endian, runtime, elementsById, usedTypes);
                            }
                            break;
                        }

                        case TypeDefinitionType.Array:
                        {
                            var refId = memberNode.InnerText.Trim();
                            if (elementsById.ContainsKey(refId))
                            {
                                var arrayNode = elementsById[refId];
                                ReadArrayFromXml(data, memberPosition, arrayNode, typeDef, endian, runtime, elementsById, usedTypes);
                            }
                            else
                            {
                                // Empty/missing array
                                data.WriteValueS64(0, endian);
                                data.WriteValueS64(0, endian);
                            }
                            break;
                        }

                        case TypeDefinitionType.InlineArray:
                        {
                            XmlNode childArray = null;
                            foreach (XmlNode child in memberNode.ChildNodes)
                            {
                                if (child.NodeType == XmlNodeType.Element && child.Name == "array")
                                {
                                    childArray = child;
                                    break;
                                }
                            }
                            if (childArray != null)
                            {
                                ReadInlineArrayFromXml(data, memberPosition, childArray, typeDef, endian, runtime, elementsById, usedTypes);
                            }
                            break;
                        }

                        case TypeDefinitionType.Enumeration:
                        {
                            var text = memberNode.InnerText.Trim();
                            var colonIndex = text.LastIndexOf(':');
                            var enumValue = uint.Parse(text.Substring(colonIndex + 1), CultureInfo.InvariantCulture);
                            data.WriteValueU32(enumValue, endian);
                            break;
                        }

                        case TypeDefinitionType.Pointer:
                        case TypeDefinitionType.BitField:
                        {
                            // Not fully supported, skip
                            break;
                        }

                        default:
                        {
                            throw new NotSupportedException("Unsupported type: " + typeDef.Type);
                        }
                    }
                    break;
                }
            }
        }

        private static void ReadArrayFromXml(
            MemoryStream data,
            long memberPosition,
            XmlNode arrayNode,
            TypeDefinition typeDef,
            Endian endian,
            RuntimeTypeLibrary runtime,
            Dictionary<string, XmlNode> elementsById,
            HashSet<uint> usedTypes)
        {
            usedTypes.Add(typeDef.NameHash);

            // Check for empty array
            bool isEmpty = !arrayNode.HasChildNodes ||
                           (arrayNode.ChildNodes.Count == 0) ||
                           (string.IsNullOrEmpty(arrayNode.InnerText.Trim()) && !HasElementChildren(arrayNode));

            if (isEmpty)
            {
                data.Position = memberPosition;
                data.WriteValueS64(0, endian);
                data.WriteValueS64(0, endian);
                return;
            }

            var arrayDataOffset = AlignLong(data.Length, 8);
            if (data.Length < arrayDataOffset)
            {
                data.SetLength(arrayDataOffset);
            }

            long count = WriteArrayItemsFromXml(data, arrayDataOffset, typeDef, endian, runtime, arrayNode, elementsById, usedTypes);

            // Write offset and count at member position
            data.Position = memberPosition;
            data.WriteValueS64(arrayDataOffset, endian);
            data.WriteValueS64(count, endian);
        }

        private static void ReadInlineArrayFromXml(
            MemoryStream data,
            long position,
            XmlNode arrayNode,
            TypeDefinition typeDef,
            Endian endian,
            RuntimeTypeLibrary runtime,
            Dictionary<string, XmlNode> elementsById,
            HashSet<uint> usedTypes)
        {
            usedTypes.Add(typeDef.NameHash);
            WriteArrayItemsFromXml(data, position, typeDef, endian, runtime, arrayNode, elementsById, usedTypes);
        }

        private static long WriteArrayItemsFromXml(
            MemoryStream data,
            long offset,
            TypeDefinition typeDef,
            Endian endian,
            RuntimeTypeLibrary runtime,
            XmlNode arrayNode,
            Dictionary<string, XmlNode> elementsById,
            HashSet<uint> usedTypes)
        {
            switch (typeDef.ElementTypeHash)
            {
                case TypeHashes.Primitive.UInt8:
                case TypeHashes.Primitive.Int8:
                case TypeHashes.Primitive.UInt16:
                case TypeHashes.Primitive.Int16:
                case TypeHashes.Primitive.UInt32:
                case TypeHashes.Primitive.Int32:
                case TypeHashes.Primitive.UInt64:
                case TypeHashes.Primitive.Int64:
                case TypeHashes.Primitive.Float:
                case TypeHashes.Primitive.Double:
                {
                    var text = arrayNode.InnerText.Trim();
                    if (string.IsNullOrEmpty(text))
                    {
                        return 0;
                    }

                    var values = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    data.Position = offset;

                    foreach (var val in values)
                    {
                        switch (typeDef.ElementTypeHash)
                        {
                            case TypeHashes.Primitive.UInt8:
                                data.WriteValueU8(byte.Parse(val, CultureInfo.InvariantCulture));
                                break;
                            case TypeHashes.Primitive.Int8:
                                data.WriteValueS8(sbyte.Parse(val, CultureInfo.InvariantCulture));
                                break;
                            case TypeHashes.Primitive.UInt16:
                                data.WriteValueU16(ushort.Parse(val, CultureInfo.InvariantCulture), endian);
                                break;
                            case TypeHashes.Primitive.Int16:
                                data.WriteValueS16(short.Parse(val, CultureInfo.InvariantCulture), endian);
                                break;
                            case TypeHashes.Primitive.UInt32:
                                data.WriteValueU32(uint.Parse(val, CultureInfo.InvariantCulture), endian);
                                break;
                            case TypeHashes.Primitive.Int32:
                                data.WriteValueS32(int.Parse(val, CultureInfo.InvariantCulture), endian);
                                break;
                            case TypeHashes.Primitive.UInt64:
                                data.WriteValueU64(ulong.Parse(val, CultureInfo.InvariantCulture), endian);
                                break;
                            case TypeHashes.Primitive.Int64:
                                data.WriteValueS64(long.Parse(val, CultureInfo.InvariantCulture), endian);
                                break;
                            case TypeHashes.Primitive.Float:
                                data.WriteValueF32(float.Parse(val, CultureInfo.InvariantCulture), endian);
                                break;
                            case TypeHashes.Primitive.Double:
                                data.WriteValueF64(double.Parse(val, CultureInfo.InvariantCulture), endian);
                                break;
                        }
                    }

                    return values.Length;
                }

                case TypeHashes.Primitive.String:
                {
                    var text = arrayNode.InnerText.Trim();
                    if (string.IsNullOrEmpty(text))
                    {
                        return 0;
                    }

                    var values = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    // For string arrays, each element is an 8-byte offset
                    // This case is unlikely in practice, handle basic case
                    data.Position = offset;
                    return values.Length;
                }

                default:
                {
                    var elemTypeDef = runtime.GetTypeDefinition(typeDef.ElementTypeHash);
                    usedTypes.Add(elemTypeDef.NameHash);

                    switch (elemTypeDef.Type)
                    {
                        case TypeDefinitionType.Structure:
                        {
                            var structNodes = new List<XmlNode>();
                            foreach (XmlNode child in arrayNode.ChildNodes)
                            {
                                if (child.NodeType == XmlNodeType.Element && child.Name == "struct")
                                {
                                    structNodes.Add(child);
                                }
                            }

                            if (structNodes.Count == 0)
                            {
                                return 0;
                            }

                            var totalArraySize = (long)structNodes.Count * elemTypeDef.Size;
                            var requiredLength = offset + totalArraySize;
                            if (data.Length < requiredLength)
                            {
                                data.SetLength(requiredLength);
                            }

                            for (int i = 0; i < structNodes.Count; i++)
                            {
                                var structOffset = offset + (long)i * elemTypeDef.Size;
                                ReadStructureFromXml(data, structOffset, structNodes[i], elemTypeDef, endian, runtime, elementsById, usedTypes);
                            }

                            return structNodes.Count;
                        }

                        default:
                        {
                            throw new NotSupportedException("Unsupported array element type: " + elemTypeDef.Type);
                        }
                    }
                }
            }
        }

        private static bool HasElementChildren(XmlNode node)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                {
                    return true;
                }
            }
            return false;
        }

        private static long AlignLong(long value, long alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }

        #endregion
    }
}
