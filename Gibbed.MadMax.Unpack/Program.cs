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
using System.Text.RegularExpressions;
using Gibbed.IO;
using Gibbed.MadMax.FileFormats;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using NDesk.Options;

namespace Gibbed.MadMax.Unpack
{
    internal class Program
    {
        private static string GetExecutableName()
        {
            return Path.GetFileName(Environment.ProcessPath);
        }

        public static void Main(string[] args)
        {
            bool showHelp = false;
            bool extractUnknowns = true;
            string filterPattern = null;
            bool overwriteFiles = false;
            bool verbose = false;
            string currentProject = null;

            var options = new OptionSet()
            {
                { "o|overwrite", "overwrite existing files", v => overwriteFiles = v != null },
                { "nu|no-unknowns", "don't extract unknown files", v => extractUnknowns = v == null },
                { "f|filter=", "only extract files using pattern", v => filterPattern = v },
                { "v|verbose", "be verbose", v => verbose = v != null },
                { "h|help", "show this message and exit", v => showHelp = v != null },
                { "p|project=", "override current project", v => currentProject = v },
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

            if (extras.Count < 1 || extras.Count > 2 || showHelp == true)
            {
                Console.WriteLine("Usage: {0} [OPTIONS]+ input_tab [output_dir]", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            string tabPath = Path.GetFullPath(extras[0]);
            string outputPath = extras.Count > 1
                                    ? Path.GetFullPath(extras[1])
                                    : Path.ChangeExtension(tabPath, null) + "_unpack";

            Regex filter = null;
            if (string.IsNullOrEmpty(filterPattern) == false)
            {
                filter = new Regex(filterPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }

            var manager = ProjectData.Manager.Load(currentProject);
            if (manager.ActiveProject == null)
            {
                Console.WriteLine("Warning: no active project loaded.");
            }

            var pathLookup = manager.LoadDirectoryList();
            var hashes = manager.LoadFileLists(null);

            var tab = new ArchiveTableFile();
            using (var input = File.OpenRead(tabPath))
            {
                tab.Deserialize(input);
            }

            var arcPath = Path.ChangeExtension(tabPath, ".arc");

            using (var input = File.OpenRead(arcPath))
            {
                long current = 0;
                long total = tab.Entries.Count;
                var padding = total.ToString(CultureInfo.InvariantCulture).Length;

                foreach (var kv in tab.Entries)
                {
                    current++;

                    var nameHash = kv.Key;
                    var entry = kv.Value;

                    string name = hashes[nameHash];
                    if (name == null)
                    {
                        if (extractUnknowns == false)
                        {
                            continue;
                        }

                        var guess = new byte[32];
                        int read = 0;

                        if (tab.EntryChunks.ContainsKey(nameHash) == false)
                        {
                            input.Seek(entry.Offset, SeekOrigin.Begin);

                            if (entry.CompressedSize == entry.UncompressedSize)
                            {
                                read = input.Read(guess, 0, (int)Math.Min(guess.Length, entry.UncompressedSize));
                            }
                            else
                            {
                                var zlib = new InflaterInputStream(input, new Inflater(true));
                                read = zlib.Read(guess, 0, (int)Math.Min(guess.Length, entry.UncompressedSize));
                            }
                        }
                        else
                        {
                            var chunks = tab.EntryChunks[nameHash];
                            if (chunks.Count > 0)
                            {
                                var chunk = chunks[0];
                                var nextUncompressedOffset =
                                    1 < chunks.Count
                                        ? chunks[1].UncompressedOffset
                                        : entry.UncompressedSize;
                                var uncompressedSize = nextUncompressedOffset - chunk.UncompressedOffset;
                                input.Seek(entry.Offset + chunk.CompressedOffset, SeekOrigin.Begin);
                                var zlib = new InflaterInputStream(input, new Inflater(true));
                                read = zlib.Read(guess, 0, (int)Math.Min(guess.Length, uncompressedSize));
                            }
                        }

                        var extension = FileDetection.Detect(guess, read);
                        name = nameHash.ToString("X8");
                        name = Path.ChangeExtension(name, "." + extension);
                        name = Path.Combine("__UNKNOWN", extension, name);
                    }
                    else
                    {
                        if (pathLookup.ContainsKey(name) == false)
                        {
                            name = Path.Combine("__UNSORTED", name);
                        }
                        else
                        {
                            name = pathLookup[name].First();
                        }

                        if (name.StartsWith("/") == true)
                        {
                            name = name.Substring(1);
                        }
                        name = name.Replace('/', Path.DirectorySeparatorChar);
                    }

                    if (filter != null && filter.IsMatch(name) == false)
                    {
                        continue;
                    }

                    var entryPath = Path.Combine(outputPath, name);
                    if (overwriteFiles == false && File.Exists(entryPath) == true)
                    {
                        continue;
                    }

                    if (verbose == true)
                    {
                        Console.WriteLine(
                            "[{0}/{1}] {2}",
                            current.ToString(CultureInfo.InvariantCulture).PadLeft(padding),
                            total,
                            name);
                    }

                    var entryDirectory = Path.GetDirectoryName(entryPath);
                    if (entryDirectory != null)
                    {
                        Directory.CreateDirectory(entryDirectory);
                    }

                    using (var output = File.Create(entryPath))
                    {
                        if (tab.EntryChunks.ContainsKey(nameHash) == false)
                        {
                            input.Seek(entry.Offset, SeekOrigin.Begin);

                            if (entry.CompressedSize == entry.UncompressedSize)
                            {
                                output.WriteFromStream(input, entry.UncompressedSize);
                            }
                            else
                            {
                                var zlib = new InflaterInputStream(input, new Inflater(true));
                                output.WriteFromStream(zlib, entry.UncompressedSize);
                            }
                        }
                        else
                        {
                            input.Seek(entry.Offset, SeekOrigin.Begin);
                            using (var temp = input.ReadToMemoryStream((int)entry.CompressedSize))
                            {
                                var chunks = tab.EntryChunks[nameHash];
                                for (int i = 0; i < chunks.Count; i++)
                                {
                                    var chunk = chunks[i];
                                    var nextUncompressedOffset =
                                        i + 1 < chunks.Count
                                            ? chunks[i + 1].UncompressedOffset
                                            : entry.UncompressedSize;
                                    var uncompressedSize = nextUncompressedOffset - chunk.UncompressedOffset;

                                    //input.Seek(entry.Offset + chunk.CompressedOffset, SeekOrigin.Begin);
                                    temp.Seek(chunk.CompressedOffset, SeekOrigin.Begin);
                                    output.Seek(chunk.UncompressedOffset, SeekOrigin.Begin);

                                    var zlib = new InflaterInputStream(temp, new Inflater(true));
                                    output.WriteFromStream(zlib, uncompressedSize);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
