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
using System.Linq;
using System.Text;
using Gibbed.IO;
using Gibbed.MadMax.FileFormats;
using Gibbed.MadMax.XvmScript;
using NDesk.Options;

namespace Gibbed.MadMax.XvmDecompile
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
            bool emitHashes = false;

            var options = new OptionSet
            {
                { "hashes", "emit #! hash/source_hash directives", v => emitHashes = v != null },
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
                Console.WriteLine("Usage: {0} [OPTIONS]+ input_xvmc_or_directory [output_xvm]", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Decompiles an XVM bytecode module (.xvmc) into high-level source code (.xvm).");
                Console.WriteLine("If a directory is specified, all .xvmc files are decompiled recursively.");
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            string inputPath = extras[0];

            if (Directory.Exists(inputPath))
            {
                var files = Directory.GetFiles(inputPath, "*.xvmc", SearchOption.AllDirectories);
                if (files.Length == 0)
                {
                    Console.WriteLine("No .xvmc files found in {0}", inputPath);
                    return;
                }

                int success = 0, failed = 0;
                foreach (var file in files)
                {
                    var outPath = Path.ChangeExtension(file, ".xvm");
                    try
                    {
                        var module = Decompile(file);
                        using (var output = File.Create(outPath))
                        using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                        {
                            var printer = new AstPrinter(writer) { EmitHashes = emitHashes };
                            printer.Print(module);
                        }
                        Console.WriteLine("Decompiled {0}", file);
                        success++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Error decompiling {0}: {1}", file, ex.Message);
                        failed++;
                    }
                }
                Console.WriteLine();
                Console.WriteLine("Done: {0} decompiled, {1} failed", success, failed);
            }
            else
            {
                string outputPath = extras.Count > 1 ? extras[1] : Path.ChangeExtension(inputPath, ".xvm");

                try
                {
                    var module = Decompile(inputPath);

                    using (var output = File.Create(outputPath))
                    using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                    {
                        var printer = new AstPrinter(writer) { EmitHashes = emitHashes };
                        printer.Print(module);
                    }

                    Console.WriteLine("Decompiled to {0}", outputPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error: {0}", ex.Message);
                    Console.Error.WriteLine(ex.StackTrace);
                }
            }
        }

        private static ScriptModule Decompile(string inputPath)
        {
            Endian endian;
            var adf = new AdfFile();
            var xvmModule = new XvmModule();
            MemoryStream debugStrings = null;

            using (var input = File.OpenRead(inputPath))
            {
                adf.Deserialize(input);
                endian = adf.Endian;

                // Read debug_strings if available
                var debugStringsInfo = adf.InstanceInfos.FirstOrDefault(i => i.Name == "debug_strings");
                if (debugStringsInfo.TypeHash == 0xFEF3B589)
                {
                    input.Position = debugStringsInfo.Offset;
                    using (var data = input.ReadToMemoryStream((int)debugStringsInfo.Size))
                    {
                        var offset = data.ReadValueS64(endian);
                        var count = data.ReadValueS64(endian);
                        if (count > 0 && count <= int.MaxValue)
                        {
                            data.Position = offset;
                            debugStrings = new MemoryStream(data.ReadBytes((int)count), false);
                        }
                    }
                }

                // Read module
                var moduleInfo = adf.InstanceInfos.First(i => i.Name == "module");
                if (moduleInfo.TypeHash != XvmModule.TypeHash)
                    throw new FormatException("Invalid module type hash.");

                input.Position = moduleInfo.Offset;
                using (var data = input.ReadToMemoryStream((int)moduleInfo.Size))
                {
                    xvmModule.Deserialize(data, endian);
                }
            }

            // Build script module
            var scriptModule = new ScriptModule();
            scriptModule.Name = xvmModule.Name;
            scriptModule.SourceHash = xvmModule.SourceHash;
            scriptModule.Flags = xvmModule.Flags;
            scriptModule.Imports.AddRange(xvmModule.ImportHashes);

            foreach (var func in xvmModule.Functions)
            {
                var scriptFunc = DecompileFunction(func, xvmModule, debugStrings);
                scriptModule.Functions.Add(scriptFunc);
            }

            if (debugStrings != null)
                debugStrings.Dispose();

            return scriptModule;
        }

        private static ScriptFunction DecompileFunction(
            XvmModule.Function function,
            XvmModule module,
            MemoryStream debugStrings)
        {
            var scriptFunc = new ScriptFunction();
            scriptFunc.Name = function.Name ?? string.Format("func_0x{0:X8}", function.NameHash);
            scriptFunc.NameHash = function.NameHash;

            // Build parameter names
            for (int i = 0; i < function.ArgCount; i++)
            {
                if (i == 0 && function.ArgCount >= 1)
                    scriptFunc.Parameters.Add("self");
                else
                    scriptFunc.Parameters.Add(string.Format("arg{0}", i));
            }

            if (function.Instructions == null || function.Instructions.Length == 0)
            {
                scriptFunc.Body.Add(new ReturnStmt(null));
                return scriptFunc;
            }

            // Step 1: Decode instructions
            var decoded = InstructionDecoder.Decode(function.Instructions, module, debugStrings);

            // Step 2: Build CFG
            var blocks = CfgBuilder.Build(decoded);

            // Step 3: Run expression recovery on each block
            var localNames = new Dictionary<int, string>();
            for (int i = 0; i < function.ArgCount; i++)
            {
                localNames[i] = scriptFunc.Parameters[i];
            }

            var blockResults = new Dictionary<int, BlockResult>();
            foreach (var block in blocks)
            {
                var br = ExpressionRecovery.ProcessBlock(block, function.ArgCount, localNames);
                blockResults[block.Id] = br;
            }

            // Step 4: Structural analysis — recover if/else, while
            var analysis = new StructuralAnalysis(blocks, blockResults);
            var body = analysis.Analyze();
            scriptFunc.Body = body;

            // Remove trailing "return" with no value if it's the last statement
            // (implicit return at end of void function)
            if (scriptFunc.Body.Count > 0)
            {
                var last = scriptFunc.Body[scriptFunc.Body.Count - 1];
                if (last is ReturnStmt ret && ret.Value == null)
                {
                    scriptFunc.Body.RemoveAt(scriptFunc.Body.Count - 1);
                }
            }

            return scriptFunc;
        }
    }
}
