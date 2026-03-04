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
using System.Text.RegularExpressions;
using Gibbed.MadMax.PropertyFormats;
using NDesk.Options;

namespace Gibbed.MadMax.ResolveHashes
{
    internal class Program
    {
        private static string GetExecutableName()
        {
            return Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }

        public static void Main(string[] args)
        {
            bool showHelp = false;
            string currentProject = null;
            bool inPlace = false;
            bool verbose = false;
            bool noComments = false;

            var options = new OptionSet
            {
                { "p|project=", "override current project", v => currentProject = v },
                { "i|in-place", "modify input file in place", v => inPlace = v != null },
                { "v|verbose", "show replacement details", v => verbose = v != null },
                { "no-comments", "do not add hash comments next to replacements", v => noComments = v != null },
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

            if (extras.Count < 1 || extras.Count > 2 || showHelp)
            {
                Console.WriteLine("Usage: {0} [OPTIONS]+ input [output]", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Resolve Jenkins hash values to names in a text file.");
                Console.WriteLine("Searches for decimal integers and 0x hex values,");
                Console.WriteLine("replaces them with known names from *.namelist files.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            var manager = ProjectData.Manager.Load(currentProject);
            if (manager.ActiveProject == null)
            {
                Console.WriteLine("Warning: no active project loaded.");
            }

            var names = manager.LoadPropertyNames();

            string inputPath = extras[0];
            if (!File.Exists(inputPath))
            {
                Console.WriteLine("Error: input file '{0}' not found.", inputPath);
                return;
            }

            string text = File.ReadAllText(inputPath);

            int replacementCount = 0;

            // Match 0x hex values (e.g. 0xDEADBEEF, 0x1A2B3C4D)
            text = Regex.Replace(text, @"0[xX]([0-9A-Fa-f]{1,8})\b", match =>
            {
                if (uint.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash))
                {
                    if (names.Contains(hash))
                    {
                        string name = names[hash];
                        replacementCount++;
                        if (verbose)
                        {
                            Console.WriteLine("  {0} -> {1}", match.Value, name);
                        }
                        string comment = noComments
                            ? ""
                            : string.Format("<!-- {0} | {1} | 0x{2:X8} -->", name, hash, hash);
                        return name + comment;
                    }
                }
                return match.Value;
            });

            // Match decimal integers in uint32 range
            // Use a negative lookbehind/lookahead to avoid matching numbers that are part of
            // identifiers, hex values, or floating point numbers
            text = Regex.Replace(text, @"(?<![0-9A-Za-z_\.xX])(\d{1,10})(?![0-9A-Za-z_\.])", match =>
            {
                string numStr = match.Groups[1].Value;
                if (ulong.TryParse(numStr, out ulong val) && val <= uint.MaxValue)
                {
                    uint hash = (uint)val;
                    if (names.Contains(hash))
                    {
                        string name = names[hash];
                        replacementCount++;
                        if (verbose)
                        {
                            Console.WriteLine("  {0} -> {1}", hash, name);
                        }
                        string comment = noComments
                            ? ""
                            : string.Format("<!-- {0} | {1} | 0x{2:X8} -->", name, hash, hash);
                        return name + comment;
                    }
                }
                return match.Value;
            });

            string outputPath;
            if (inPlace)
            {
                outputPath = inputPath;
            }
            else if (extras.Count > 1)
            {
                outputPath = extras[1];
            }
            else
            {
                string dir = Path.GetDirectoryName(inputPath);
                string name = Path.GetFileNameWithoutExtension(inputPath);
                string ext = Path.GetExtension(inputPath);
                outputPath = Path.Combine(dir ?? "", name + "_resolved" + ext);
            }

            File.WriteAllText(outputPath, text);

            Console.WriteLine("Resolved {0} hash(es).", replacementCount);
            Console.WriteLine("Output: {0}", outputPath);
        }
    }
}
