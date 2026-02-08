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

namespace Gibbed.MadMax.XvmCompile
{
    public enum TokenType
    {
        // Structural
        Indent,
        Dedent,
        Newline,
        Eof,

        // Keywords
        Def,
        If,
        Elif,
        Else,
        While,
        Return,
        Assert,
        Module,
        Import,
        And,
        Or,
        Not,
        True,
        False,
        None,
        Pass,

        // Literals
        Identifier,
        FloatLiteral,
        StringLiteral,
        BytesLiteral, // @XXXX

        // Metadata directive: #! key: value
        Directive,

        // Punctuation
        Dot,
        Comma,
        Colon,
        LParen,
        RParen,
        LBracket,
        RBracket,

        // Operators
        Assign,    // =
        Eq,        // ==
        Ne,        // !=
        Gt,        // >
        Ge,        // >=
        Plus,
        Minus,
        Star,
        Slash,
        Percent,
    }

    public class Token
    {
        public TokenType Type;
        public string Value;
        public int Line;
        public int Column;

        public Token(TokenType type, string value, int line, int column)
        {
            Type = type;
            Value = value;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return string.Format("{0} '{1}' at {2}:{3}", Type, Value, Line, Column);
        }
    }
}
