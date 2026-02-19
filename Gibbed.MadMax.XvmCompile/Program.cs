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
using Gibbed.MadMax.XvmAssemble;
using NDesk.Options;

namespace Gibbed.MadMax.XvmCompile
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
            string globalsFile = null;

            var options = new OptionSet
            {
                { "g|globals=", "path to xvm_globals.txt (default: next to exe)", v => globalsFile = v },
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
                Console.WriteLine("Usage: {0} [OPTIONS]+ input_xvm [output_xvmc]", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Compiles XVM source code (.xvm) into bytecode (.xvmc).");
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            string inputPath = extras[0];
            string outputPath = extras.Count > 1 ? extras[1] : Path.ChangeExtension(inputPath, ".xvmc");

            try
            {
                Compile(inputPath, outputPath, globalsFile);
                Console.WriteLine("Compiled to {0}", outputPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: {0}", ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
            }
        }

        private static void Compile(string inputPath, string outputPath, string globalsFile)
        {
            var source = File.ReadAllText(inputPath, Encoding.UTF8);

            // Phase 0: Pre-parse #! directives from source text
            uint sourceHash = 0;
            uint flags = 0;
            var functionHashes = new Dictionary<string, uint>(); // funcName → hash
            PreParseDirectives(source, out sourceHash, out flags, functionHashes);

            // Phase 1: Lex
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();

            // Phase 2: Parse
            var parser = new Parser(tokens);
            var module = parser.ParseModule();

            // Apply pre-parsed directives
            if (sourceHash != 0)
                module.SourceHash = sourceHash;
            module.Flags = flags;

            foreach (var func in module.Functions)
            {
                uint hash;
                if (func.NameHash == 0 && functionHashes.TryGetValue(func.Name, out hash))
                    func.NameHash = hash;
            }

            // Phase 3: Semantic analysis
            var semantics = new SemanticAnalysis(module, globalsFile);
            var scopes = semantics.Analyze();

            // Phase 4: Code generation → ParsedModule (assembler IR)
            var codegen = new CodeGenerator(module, scopes, semantics);
            var parsedModule = codegen.Generate();

            // Auto-compute hashes
            if (parsedModule.NameHash == 0 && !string.IsNullOrEmpty(parsedModule.Name))
                parsedModule.NameHash = HashUtil.HashString(parsedModule.Name);

            // Auto-compute source_hash from source text if not explicitly specified
            if (parsedModule.SourceHash == 0)
                parsedModule.SourceHash = HashUtil.HashString(source);

            foreach (var func in parsedModule.Functions)
            {
                if (func.NameHash == 0 && !string.IsNullOrEmpty(func.Name))
                    func.NameHash = HashUtil.HashString(func.Name);
            }

            // Phase 5: Assemble (reuse existing assembler)
            var assembler = new Assembler(parsedModule);
            var result = assembler.Assemble();

            // Phase 6: Write ADF container
            using (var output = File.Create(outputPath))
            {
                AdfWriter.Write(output, result.Module, result.DebugStrings, result, Gibbed.IO.Endian.Little);
            }
        }
        /// <summary>
        /// Pre-parse #! directives from source text before lexing.
        /// Extracts source_hash and per-function hash directives.
        /// A #! hash directive applies to the next "def" line that follows it.
        /// </summary>
        private static void PreParseDirectives(string source, out uint sourceHash,
            out uint flags, Dictionary<string, uint> functionHashes)
        {
            sourceHash = 0;
            flags = 0;
            var lines = source.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            uint pendingHash = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (line.StartsWith("#!"))
                {
                    var directive = line.Substring(2).Trim();
                    if (directive.StartsWith("source_hash:"))
                    {
                        sourceHash = ParseHex(directive.Substring("source_hash:".Length).Trim());
                    }
                    else if (directive.StartsWith("flags:"))
                    {
                        flags = ParseHex(directive.Substring("flags:".Length).Trim());
                    }
                    else if (directive.StartsWith("hash:"))
                    {
                        pendingHash = ParseHex(directive.Substring("hash:".Length).Trim());
                    }
                }
                else if (line.StartsWith("def ") && pendingHash != 0)
                {
                    // Extract function name: "def FuncName(..." → "FuncName"
                    var rest = line.Substring(4);
                    int parenIdx = rest.IndexOf('(');
                    if (parenIdx > 0)
                    {
                        var funcName = rest.Substring(0, parenIdx).Trim();
                        functionHashes[funcName] = pendingHash;
                    }
                    pendingHash = 0;
                }
                else if (!string.IsNullOrEmpty(line) && !line.StartsWith("#"))
                {
                    // Non-empty, non-comment line that isn't "def" clears pending hash
                    pendingHash = 0;
                }
            }
        }

        private static uint ParseHex(string hex)
        {
            if (hex.StartsWith("0x") || hex.StartsWith("0X"))
                hex = hex.Substring(2);
            return uint.Parse(hex, NumberStyles.HexNumber);
        }
    }
}
