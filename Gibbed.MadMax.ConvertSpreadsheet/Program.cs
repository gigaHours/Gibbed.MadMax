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
using System.Xml;
using Gibbed.IO;
using NDesk.Options;

namespace Gibbed.MadMax.ConvertSpreadsheet
{
    internal class Program
    {
        private static string GetExecutableName()
        {
            return Path.GetFileName(Environment.ProcessPath);
        }

        private static void Main(string[] args)
        {
            bool showHelp = false;

            var options = new OptionSet
            {
                { "h|help", "show this message and exit", v => showHelp = v != null },
            };

            List<string> extras;

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

            if (extras.Count < 1 || extras.Count > 2 ||
                showHelp == true)
            {
                Console.WriteLine("Usage: {0} [OPTIONS]+ input_xlsc [output_xml]", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            string inputPath = extras[0];
            string outputPath = extras.Count > 1 ? extras[1] : Path.ChangeExtension(inputPath, ".xml");

            Endian endian;
            var book = new XlsBook();

            using (var input = File.OpenRead(inputPath))
            {
                var adf = new FileFormats.AdfFile();
                adf.Deserialize(input);
                endian = adf.Endian;

                var bookInfo = adf.InstanceInfos.FirstOrDefault(i => i.Name == "XLSBook");
                if (bookInfo.TypeHash != XlsBook.TypeHash)
                {
                    throw new FormatException();
                }

                input.Position = bookInfo.Offset;
                book.Deserialize(input, endian);
            }

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "\t",
                CheckCharacters = false,
            };

            using (var output = File.Create(outputPath))
            using (var writer = XmlWriter.Create(output, settings))
            {
                const string spreadsheetNs = "urn:schemas-microsoft-com:office:spreadsheet";

                writer.WriteStartDocument();
                writer.WriteProcessingInstruction("mso-application", "progid=\"Excel.Sheet\"");

                writer.WriteStartElement("ss", "Workbook", spreadsheetNs);
                //writer.WriteAttributeString("xmlns", "ss", "", spreadsheetNs);
                //writer.WriteAttributeString("xmlns", "o", "", "urn:schemas-microsoft-com:office:office");
                //writer.WriteAttributeString("xmlns", "x", "", "urn:schemas-microsoft-com:office:excel");
                //writer.WriteAttributeString("xmlns", "html", "", "http://www.w3.org/TR/REC-html40");

                writer.WriteStartElement("Styles", spreadsheetNs);
                for (int i = 0; i < book.Attributes.Count; i++)
                {
                    var attribute = book.Attributes[i];

                    writer.WriteStartElement("Style", spreadsheetNs);
                    writer.WriteAttributeString("ID", spreadsheetNs, "test" + i.ToString(CultureInfo.InvariantCulture));

                    if (attribute.ForegroundColorIndex != book.ColorData.Count)
                    {
                        writer.WriteStartElement("Font", spreadsheetNs);
                        writer.WriteAttributeString("Color", spreadsheetNs, string.Format("#{0:x6}", book.ColorData[attribute.ForegroundColorIndex]));
                        writer.WriteEndElement();
                    }

                    if (attribute.BackgroundColorIndex != book.ColorData.Count)
                    {
                        writer.WriteStartElement("Interior", spreadsheetNs);
                        writer.WriteAttributeString("Color", spreadsheetNs, string.Format("#{0:x6}", book.ColorData[attribute.BackgroundColorIndex]));
                        writer.WriteAttributeString("Pattern", spreadsheetNs, "Solid");
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }
                writer.WriteEndElement();

                foreach (var sheet in book.Sheets)
                {
                    writer.WriteStartElement("Worksheet", spreadsheetNs);
                    writer.WriteAttributeString("Name", spreadsheetNs, sheet.Name);

                    writer.WriteStartElement("Table", spreadsheetNs);
                    writer.WriteAttributeString("ExpandedColumnCount", spreadsheetNs, sheet.ColumnCount.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("ExpandedRowCount", spreadsheetNs, sheet.RowCount.ToString(CultureInfo.InvariantCulture));

                    for (uint index = 0, row = 0; row < sheet.RowCount; row++)
                    {
                        writer.WriteStartElement("Row", spreadsheetNs);
                        for (uint col = 0; col < sheet.ColumnCount; col++, index++)
                        {
                            var cell = book.Cells[(int)sheet.CellIndices[index]];

                            writer.WriteStartElement("Cell", spreadsheetNs);
                            writer.WriteAttributeString("StyleID", spreadsheetNs, "test" + cell.AttributeIndex.ToString(CultureInfo.InvariantCulture));

                            writer.WriteStartElement("Data", spreadsheetNs);

                            switch (cell.Type)
                            {
                                case XlsBook.CellType.Bool:
                                {
                                    writer.WriteAttributeString("Type", spreadsheetNs, "Boolean");
                                    var value = book.BoolData[(int)cell.DataIndex];
                                    writer.WriteValue(value);
                                    break;
                                }

                                case XlsBook.CellType.String:
                                {
                                    writer.WriteAttributeString("Type", spreadsheetNs, "String");
                                    var value = book.StringData[(int)cell.DataIndex];
                                    writer.WriteValue(value);
                                    break;
                                }

                                case XlsBook.CellType.Float:
                                {
                                    writer.WriteAttributeString("Type", spreadsheetNs, "Number");
                                    var value = book.ValueData[(int)cell.DataIndex];
                                    writer.WriteValue(value);
                                    break;
                                }

                                default:
                                {
                                    throw new NotImplementedException();
                                }
                            }

                            writer.WriteEndElement();
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }
    }
}
