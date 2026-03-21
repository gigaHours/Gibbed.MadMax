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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NDesk.Options;

namespace Gibbed.MadMax.BinarySearch
{
    internal class Program
    {
        private static string GetExecutableName()
        {
            return Path.GetFileName(Environment.ProcessPath);
        }

        private enum Endianness
        {
            Little,
            Big,
            Both,
        }

        /// <summary>
        /// A single search pattern: the raw bytes to find + a human label.
        /// </summary>
        private sealed class SearchPattern
        {
            public byte[] Bytes { get; }
            public string Label { get; }

            public SearchPattern(byte[] bytes, string label)
            {
                Bytes = bytes;
                Label = label;
            }
        }

        /// <summary>
        /// Result of a match inside a file.
        /// </summary>
        private sealed class SearchResult
        {
            public string FilePath { get; }
            public long Offset { get; }
            public SearchPattern Pattern { get; }

            public SearchResult(string filePath, long offset, SearchPattern pattern)
            {
                FilePath = filePath;
                Offset = offset;
                Pattern = pattern;
            }
        }

        public static void Main(string[] args)
        {
            bool showHelp = false;
            var endianness = Endianness.Little;
            int threadCount = Environment.ProcessorCount;
            string filter = "*";
            int contextBytes = 16;

            var options = new OptionSet
            {
                { "l|little-endian", "search in little-endian byte order (default)", v => { if (v != null) endianness = Endianness.Little; } },
                { "b|big-endian", "search in big-endian byte order", v => { if (v != null) endianness = Endianness.Big; } },
                { "both-endian", "search in both byte orders", v => { if (v != null) endianness = Endianness.Both; } },
                { "t|threads=", "number of threads (default: CPU count)", v => threadCount = int.Parse(v) },
                { "f|filter=", "file filter pattern (default: *)", v => filter = v },
                { "c|context=", "context bytes to show around match (default: 16)", v => contextBytes = int.Parse(v) },
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

            if (extras.Count < 2 || showHelp)
            {
                Console.WriteLine("Usage: {0} [OPTIONS]+ <directory> <value1> [value2] ...", GetExecutableName());
                Console.WriteLine();
                Console.WriteLine("Search binary files recursively for byte patterns.");
                Console.WriteLine("Multi-threaded recursive search through all files in a directory.");
                Console.WriteLine();
                Console.WriteLine("Values can be:");
                Console.WriteLine("  0xABCD1234       hex number (converted to bytes with chosen endianness)");
                Console.WriteLine("  12345678         decimal number (converted to 4-byte uint32)");
                Console.WriteLine("  s:some text      string (searched as UTF-8 bytes)");
                Console.WriteLine("  b:FF00AA55       raw byte sequence (hex pairs)");
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  {0} /path/to/dir 0x73989A77", GetExecutableName());
                Console.WriteLine("  {0} /path/to/dir 0x73989A77 s:convoy b:DEADBEEF", GetExecutableName());
                Console.WriteLine("  {0} -b /path/to/dir 1937076855", GetExecutableName());
                return;
            }

            string searchDir = extras[0];
            if (!Directory.Exists(searchDir))
            {
                Console.WriteLine("Error: directory '{0}' not found.", searchDir);
                return;
            }

            // Parse search values into byte patterns
            var patterns = new List<SearchPattern>();
            for (int i = 1; i < extras.Count; i++)
            {
                string input = extras[i];
                var parsed = ParsePatterns(input, endianness);
                if (parsed == null || parsed.Count == 0)
                {
                    Console.WriteLine("Error: could not parse value '{0}'.", input);
                    return;
                }
                patterns.AddRange(parsed);
            }

            Console.WriteLine("Search directory : {0}", searchDir);
            Console.WriteLine("Threads          : {0}", threadCount);
            Console.WriteLine("File filter      : {0}", filter);
            Console.WriteLine("Endianness       : {0}", endianness);
            Console.WriteLine("Patterns         :");
            foreach (var p in patterns)
            {
                Console.WriteLine("  {0,-30} -> [{1}]", p.Label, FormatBytes(p.Bytes));
            }
            Console.WriteLine();

            // Enumerate all files
            string[] files;
            try
            {
                files = Directory.GetFiles(searchDir, filter, SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error enumerating files: {0}", ex.Message);
                return;
            }

            Console.WriteLine("Files to scan: {0}", files.Length);
            Console.WriteLine();

            var results = new ConcurrentBag<SearchResult>();
            int filesScanned = 0;
            int totalFiles = files.Length;
            long totalBytesScanned = 0;
            var startTime = DateTime.UtcNow;

            // Progress reporting
            var progressTimer = new Timer(_ =>
            {
                int scanned = Volatile.Read(ref filesScanned);
                long bytes = Volatile.Read(ref totalBytesScanned);
                double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                double mbps = elapsed > 0 ? (bytes / 1024.0 / 1024.0) / elapsed : 0;
                Console.Write("\r[{0}/{1} files] [{2:F1} MB scanned] [{3:F1} MB/s] [{4} matches]   ",
                    scanned, totalFiles,
                    bytes / 1024.0 / 1024.0,
                    mbps,
                    results.Count);
            }, null, 0, 250);

            // Parallel search
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = threadCount
            };

            Parallel.ForEach(files, parallelOptions, filePath =>
            {
                try
                {
                    var fileResults = SearchFile(filePath, patterns);
                    foreach (var r in fileResults)
                    {
                        results.Add(r);
                    }
                    Interlocked.Add(ref totalBytesScanned, new FileInfo(filePath).Length);
                }
                catch (Exception)
                {
                    // skip files we can't read (locked, permissions, etc.)
                }
                Interlocked.Increment(ref filesScanned);
            });

            progressTimer.Dispose();

            double totalElapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("=== Scan complete ===");
            Console.WriteLine("Time       : {0:F2}s", totalElapsed);
            Console.WriteLine("Files      : {0}", totalFiles);
            Console.WriteLine("Scanned    : {0:F1} MB", Volatile.Read(ref totalBytesScanned) / 1024.0 / 1024.0);
            Console.WriteLine("Matches    : {0}", results.Count);
            Console.WriteLine();

            if (results.IsEmpty)
            {
                Console.WriteLine("No matches found.");
                return;
            }

            // Group and sort results
            var sorted = results
                .OrderBy(r => r.FilePath)
                .ThenBy(r => r.Offset)
                .ToList();

            string lastFile = null;
            foreach (var result in sorted)
            {
                if (result.FilePath != lastFile)
                {
                    Console.WriteLine("--- {0} ---", result.FilePath);
                    lastFile = result.FilePath;
                }

                Console.Write("  offset 0x{0:X8} ({0,10})  pattern: {1}", result.Offset, result.Pattern.Label);

                // Show context bytes
                try
                {
                    string ctx = ReadContext(result.FilePath, result.Offset, result.Pattern.Bytes.Length, contextBytes);
                    Console.Write("  | {0}", ctx);
                }
                catch (Exception)
                {
                    // ignore context read errors
                }
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Parse a user input string into one or more search patterns.
        /// Returns multiple patterns when --both-endian is used for numeric values.
        /// </summary>
        private static List<SearchPattern> ParsePatterns(string input, Endianness endianness)
        {
            var result = new List<SearchPattern>();

            // String prefix: s:hello world
            if (input.StartsWith("s:", StringComparison.OrdinalIgnoreCase))
            {
                string str = input.Substring(2);
                byte[] bytes = Encoding.UTF8.GetBytes(str);
                result.Add(new SearchPattern(bytes, string.Format("str \"{0}\"", str)));
                return result;
            }

            // Raw bytes prefix: b:FF00AA55
            if (input.StartsWith("b:", StringComparison.OrdinalIgnoreCase))
            {
                string hex = input.Substring(2).Replace(" ", "").Replace("-", "");
                if (hex.Length % 2 != 0)
                {
                    return null;
                }
                byte[] bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                    {
                        return null;
                    }
                }
                result.Add(new SearchPattern(bytes, string.Format("raw [{0}]", FormatBytes(bytes))));
                return result;
            }

            // Numeric: hex (0x...) or decimal
            uint value;
            if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
            {
                string hexStr = input.Substring(2);
                if (!uint.TryParse(hexStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                {
                    return null;
                }
            }
            else
            {
                // Try decimal
                if (!uint.TryParse(input, out value))
                {
                    // Not a number — treat as string search
                    byte[] strBytes = Encoding.UTF8.GetBytes(input);
                    result.Add(new SearchPattern(strBytes, string.Format("str \"{0}\"", input)));
                    return result;
                }
            }

            // Convert uint32 to bytes
            byte[] leBytes = BitConverter.GetBytes(value); // native (usually LE)
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(leBytes);
            }

            byte[] beBytes = new byte[4];
            Array.Copy(leBytes, beBytes, 4);
            Array.Reverse(beBytes);

            string hexLabel = string.Format("0x{0:X8} ({1})", value, value);

            switch (endianness)
            {
                case Endianness.Little:
                    result.Add(new SearchPattern(leBytes, hexLabel + " LE"));
                    break;
                case Endianness.Big:
                    result.Add(new SearchPattern(beBytes, hexLabel + " BE"));
                    break;
                case Endianness.Both:
                    result.Add(new SearchPattern(leBytes, hexLabel + " LE"));
                    // Don't add duplicate if LE == BE (palindrome bytes)
                    if (!leBytes.SequenceEqual(beBytes))
                    {
                        result.Add(new SearchPattern(beBytes, hexLabel + " BE"));
                    }
                    break;
            }

            return result;
        }

        /// <summary>
        /// Search a single file for all patterns. Uses a sliding window approach
        /// with buffered reads to keep memory usage reasonable.
        /// </summary>
        private static List<SearchResult> SearchFile(string filePath, List<SearchPattern> patterns)
        {
            var results = new List<SearchResult>();

            const int bufferSize = 16 * 1024 * 1024; // 4 MB buffer
            int maxPatternLen = patterns.Max(p => p.Bytes.Length);
            int overlap = maxPatternLen - 1;

            byte[] buffer = new byte[bufferSize + overlap];
            long fileOffset = 0;
            int carryOver = 0;

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize))
            {
                while (true)
                {
                    int bytesRead = stream.Read(buffer, carryOver, bufferSize);
                    if (bytesRead == 0)
                        break;

                    int totalInBuffer = carryOver + bytesRead;

                    foreach (var pattern in patterns)
                    {
                        int searchEnd = totalInBuffer - pattern.Bytes.Length;
                        for (int pos = 0; pos <= searchEnd; pos++)
                        {
                            if (MatchAt(buffer, pos, pattern.Bytes))
                            {
                                long matchOffset = fileOffset + pos;
                                results.Add(new SearchResult(filePath, matchOffset, pattern));
                            }
                        }
                    }

                    // Keep tail bytes for cross-boundary matches
                    if (bytesRead == bufferSize && overlap > 0)
                    {
                        Array.Copy(buffer, totalInBuffer - overlap, buffer, 0, overlap);
                        carryOver = overlap;
                        fileOffset += bytesRead;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Check if pattern matches buffer at given position.
        /// </summary>
        private static bool MatchAt(byte[] buffer, int position, byte[] pattern)
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (buffer[position + i] != pattern[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Read context bytes around a match for display.
        /// </summary>
        private static string ReadContext(string filePath, long matchOffset, int patternLength, int contextBytes)
        {
            long start = Math.Max(0, matchOffset - contextBytes);
            long end = matchOffset + patternLength + contextBytes;

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (end > stream.Length)
                    end = stream.Length;

                int length = (int)(end - start);
                byte[] data = new byte[length];
                stream.Seek(start, SeekOrigin.Begin);
                stream.Read(data, 0, length);

                int highlightStart = (int)(matchOffset - start);
                int highlightEnd = highlightStart + patternLength;

                var sb = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    if (i == highlightStart) sb.Append('[');
                    sb.AppendFormat("{0:X2}", data[i]);
                    if (i == highlightEnd - 1) sb.Append(']');
                    if (i < data.Length - 1) sb.Append(' ');
                }

                // Also show ASCII representation
                sb.Append("  |");
                for (int i = 0; i < data.Length; i++)
                {
                    if (i == highlightStart) sb.Append('[');
                    char c = (data[i] >= 0x20 && data[i] < 0x7F) ? (char)data[i] : '.';
                    sb.Append(c);
                    if (i == highlightEnd - 1) sb.Append(']');
                }
                sb.Append('|');

                return sb.ToString();
            }
        }

        /// <summary>
        /// Format bytes as hex string for display.
        /// </summary>
        private static string FormatBytes(byte[] bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }
    }
}
