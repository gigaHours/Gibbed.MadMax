using System;
using System.Collections.Generic;
using System.IO;
using Gibbed.IO;
using NDesk.Options;

namespace Gibbed.MadMax.XvmAssemble
{
    internal class Program
    {
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
                //extras.Add("C:\\Developers\\GitHub\\Gibbed.MadMax\\bin\\xvm\\bullet_damage_handler.dis");
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
                Console.WriteLine("Usage: {0} [OPTIONS]+ input_dis [output_xvmc]", GetExecutableName());
                Console.WriteLine("Assemble a .dis file into an XVM bytecode module (.xvmc).");
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            string inputPath = extras[0];
            string outputPath = extras.Count > 1 ? extras[1] : Path.ChangeExtension(inputPath, ".xvmc");

            try
            {
                Console.WriteLine("Parsing: {0}", inputPath);
                var parsed = DisParser.Parse(inputPath);

                Console.WriteLine("Module: {0}", parsed.Name ?? "(unnamed)");
                Console.WriteLine("Functions: {0}", parsed.Functions.Count);
                Console.WriteLine("Has debug strings: {0}", parsed.HasDebugStrings);
                Console.WriteLine("Has debug info: {0}", parsed.HasDebugInfo);

                Console.WriteLine("Assembling...");
                var assembler = new Assembler(parsed);
                var result = assembler.Assemble();

                Console.WriteLine("Constants: {0}", result.Module.Constants.Count);
                Console.WriteLine("String hashes: {0}", result.Module.StringHashes.Count);
                Console.WriteLine("String buffer: {0} bytes",
                    result.Module.StringBuffer != null ? result.Module.StringBuffer.Length : 0);

                Console.WriteLine("Writing: {0}", outputPath);
                using (var output = File.Create(outputPath))
                {
                    AdfWriter.Write(output, result.Module, result.DebugStrings, result, Endian.Little);
                }

                var fileInfo = new FileInfo(outputPath);
                Console.WriteLine("Done. Output size: {0} bytes", fileInfo.Length);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
            }
        }
    }
}
