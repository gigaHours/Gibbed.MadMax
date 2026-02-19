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
using System.Text;

namespace Gibbed.MadMax.XvmCompile
{
    public class Lexer
    {
        private readonly string[] _lines;
        private readonly List<Token> _tokens = new List<Token>();
        private readonly Stack<int> _indentStack = new Stack<int>();

        private static readonly Dictionary<string, TokenType> Keywords = new Dictionary<string, TokenType>
        {
            { "def", TokenType.Def },
            { "if", TokenType.If },
            { "elif", TokenType.Elif },
            { "else", TokenType.Else },
            { "while", TokenType.While },
            { "return", TokenType.Return },
            { "assert", TokenType.Assert },
            { "module", TokenType.Module },
            { "import", TokenType.Import },
            { "and", TokenType.And },
            { "or", TokenType.Or },
            { "not", TokenType.Not },
            { "true", TokenType.True },
            { "false", TokenType.False },
            { "none", TokenType.None },
            { "pass", TokenType.Pass },
            { "break", TokenType.Break },
        };

        public Lexer(string source)
        {
            _lines = source.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            _indentStack.Push(0);
        }

        public List<Token> Tokenize()
        {
            for (int lineNum = 0; lineNum < _lines.Length; lineNum++)
            {
                var line = _lines[lineNum];

                // Skip empty lines and comment-only lines
                var stripped = line.TrimStart();
                if (string.IsNullOrEmpty(stripped))
                    continue;

                // Skip comment-only lines (including #! directives — parsed separately)
                if (stripped.StartsWith("#"))
                    continue;

                // Calculate indentation
                int indent = 0;
                for (int i = 0; i < line.Length; i++)
                {
                    if (line[i] == ' ') indent++;
                    else if (line[i] == '\t') indent += 4;
                    else break;
                }

                // Emit INDENT/DEDENT tokens
                var currentIndent = _indentStack.Peek();
                if (indent > currentIndent)
                {
                    _indentStack.Push(indent);
                    _tokens.Add(new Token(TokenType.Indent, "", lineNum + 1, 1));
                }
                else
                {
                    while (_indentStack.Count > 1 && indent < _indentStack.Peek())
                    {
                        _indentStack.Pop();
                        _tokens.Add(new Token(TokenType.Dedent, "", lineNum + 1, 1));
                    }
                }

                // Tokenize line content
                TokenizeLine(stripped, lineNum + 1);

                // Emit NEWLINE
                _tokens.Add(new Token(TokenType.Newline, "\\n", lineNum + 1, stripped.Length + indent + 1));
            }

            // Emit remaining DEDENTs
            while (_indentStack.Count > 1)
            {
                _indentStack.Pop();
                _tokens.Add(new Token(TokenType.Dedent, "", _lines.Length + 1, 1));
            }

            _tokens.Add(new Token(TokenType.Eof, "", _lines.Length + 1, 1));
            return _tokens;
        }

        private void TokenizeLine(string line, int lineNum)
        {
            int pos = 0;

            while (pos < line.Length)
            {
                char c = line[pos];

                // Skip whitespace
                if (c == ' ' || c == '\t')
                {
                    pos++;
                    continue;
                }

                // Skip comments
                if (c == '#')
                    break;

                int col = pos + 1;

                // String literal
                if (c == '"')
                {
                    pos++;
                    var sb = new StringBuilder();
                    while (pos < line.Length && line[pos] != '"')
                    {
                        if (line[pos] == '\\' && pos + 1 < line.Length)
                        {
                            pos++;
                            switch (line[pos])
                            {
                                case 'n': sb.Append('\n'); break;
                                case 't': sb.Append('\t'); break;
                                case 'r': sb.Append('\r'); break;
                                case '\\': sb.Append('\\'); break;
                                case '"': sb.Append('"'); break;
                                default: sb.Append('\\'); sb.Append(line[pos]); break;
                            }
                        }
                        else
                        {
                            sb.Append(line[pos]);
                        }
                        pos++;
                    }
                    if (pos < line.Length) pos++; // skip closing quote
                    _tokens.Add(new Token(TokenType.StringLiteral, sb.ToString(), lineNum, col));
                    continue;
                }

                // Bytes literal: @XXXXXXXX
                if (c == '@')
                {
                    pos++;
                    var sb = new StringBuilder();
                    while (pos < line.Length && IsHexDigit(line[pos]))
                    {
                        sb.Append(line[pos]);
                        pos++;
                    }
                    _tokens.Add(new Token(TokenType.BytesLiteral, sb.ToString(), lineNum, col));
                    continue;
                }

                // Number (float)
                if (char.IsDigit(c) || (c == '-' && pos + 1 < line.Length && char.IsDigit(line[pos + 1]) && ShouldBeUnaryMinus(pos)))
                {
                    var sb = new StringBuilder();
                    if (c == '-') { sb.Append(c); pos++; }
                    while (pos < line.Length && (char.IsDigit(line[pos]) || line[pos] == '.'))
                    {
                        sb.Append(line[pos]);
                        pos++;
                    }
                    // Handle exponent
                    if (pos < line.Length && (line[pos] == 'e' || line[pos] == 'E'))
                    {
                        sb.Append(line[pos]); pos++;
                        if (pos < line.Length && (line[pos] == '+' || line[pos] == '-'))
                        {
                            sb.Append(line[pos]); pos++;
                        }
                        while (pos < line.Length && char.IsDigit(line[pos]))
                        {
                            sb.Append(line[pos]); pos++;
                        }
                    }
                    _tokens.Add(new Token(TokenType.FloatLiteral, sb.ToString(), lineNum, col));
                    continue;
                }

                // Identifier or keyword
                if (char.IsLetter(c) || c == '_')
                {
                    var sb = new StringBuilder();
                    while (pos < line.Length && (char.IsLetterOrDigit(line[pos]) || line[pos] == '_'))
                    {
                        sb.Append(line[pos]);
                        pos++;
                    }
                    var word = sb.ToString();
                    TokenType kwType;
                    if (Keywords.TryGetValue(word, out kwType))
                        _tokens.Add(new Token(kwType, word, lineNum, col));
                    else
                        _tokens.Add(new Token(TokenType.Identifier, word, lineNum, col));
                    continue;
                }

                // Two-character operators
                if (pos + 1 < line.Length)
                {
                    var two = line.Substring(pos, 2);
                    switch (two)
                    {
                        case "==": _tokens.Add(new Token(TokenType.Eq, two, lineNum, col)); pos += 2; continue;
                        case "!=": _tokens.Add(new Token(TokenType.Ne, two, lineNum, col)); pos += 2; continue;
                        case ">=": _tokens.Add(new Token(TokenType.Ge, two, lineNum, col)); pos += 2; continue;
                    }
                }

                // Single-character operators and punctuation
                switch (c)
                {
                    case '.': _tokens.Add(new Token(TokenType.Dot, ".", lineNum, col)); break;
                    case ',': _tokens.Add(new Token(TokenType.Comma, ",", lineNum, col)); break;
                    case ':': _tokens.Add(new Token(TokenType.Colon, ":", lineNum, col)); break;
                    case '(': _tokens.Add(new Token(TokenType.LParen, "(", lineNum, col)); break;
                    case ')': _tokens.Add(new Token(TokenType.RParen, ")", lineNum, col)); break;
                    case '[': _tokens.Add(new Token(TokenType.LBracket, "[", lineNum, col)); break;
                    case ']': _tokens.Add(new Token(TokenType.RBracket, "]", lineNum, col)); break;
                    case '=': _tokens.Add(new Token(TokenType.Assign, "=", lineNum, col)); break;
                    case '>': _tokens.Add(new Token(TokenType.Gt, ">", lineNum, col)); break;
                    case '+': _tokens.Add(new Token(TokenType.Plus, "+", lineNum, col)); break;
                    case '-': _tokens.Add(new Token(TokenType.Minus, "-", lineNum, col)); break;
                    case '*': _tokens.Add(new Token(TokenType.Star, "*", lineNum, col)); break;
                    case '/': _tokens.Add(new Token(TokenType.Slash, "/", lineNum, col)); break;
                    case '%': _tokens.Add(new Token(TokenType.Percent, "%", lineNum, col)); break;
                    default:
                        throw new FormatException(string.Format(
                            "Line {0}, col {1}: unexpected character '{2}'", lineNum, col, c));
                }
                pos++;
            }
        }

        private bool ShouldBeUnaryMinus(int pos)
        {
            // The '-' at pos should only be treated as part of a number literal
            // if the previous token is an operator or start of expression
            // This is a heuristic - the parser handles unary minus in expressions
            return false;
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
