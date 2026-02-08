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
using Gibbed.MadMax.XvmScript;

namespace Gibbed.MadMax.XvmCompile
{
    public class Parser
    {
        private readonly List<Token> _tokens;
        private int _pos;

        public Parser(List<Token> tokens)
        {
            _tokens = tokens;
            _pos = 0;
        }

        private Token Current => _pos < _tokens.Count ? _tokens[_pos] : _tokens[_tokens.Count - 1];
        private Token Peek(int offset = 0) => (_pos + offset) < _tokens.Count ? _tokens[_pos + offset] : _tokens[_tokens.Count - 1];

        private Token Consume(TokenType expected)
        {
            var t = Current;
            if (t.Type != expected)
                throw new FormatException(string.Format(
                    "Line {0}: expected {1}, got {2} ('{3}')",
                    t.Line, expected, t.Type, t.Value));
            _pos++;
            return t;
        }

        private Token Consume()
        {
            var t = Current;
            _pos++;
            return t;
        }

        private bool Match(TokenType type)
        {
            if (Current.Type == type)
            {
                _pos++;
                return true;
            }
            return false;
        }

        private void SkipNewlines()
        {
            while (Current.Type == TokenType.Newline)
                _pos++;
        }

        public ScriptModule ParseModule()
        {
            var module = new ScriptModule();
            SkipNewlines();

            // module declaration
            if (Current.Type == TokenType.Module)
            {
                Consume(TokenType.Module);
                module.Name = Consume(TokenType.Identifier).Value;
                Consume(TokenType.Newline);
                SkipNewlines();
            }

            // import declarations
            while (Current.Type == TokenType.Import)
            {
                Consume(TokenType.Import);
                var hashToken = Consume(TokenType.BytesLiteral);
                module.Imports.Add(ParseHexUint(hashToken.Value));
                Consume(TokenType.Newline);
                SkipNewlines();
            }

            // function definitions
            while (Current.Type == TokenType.Def)
            {
                module.Functions.Add(ParseFunction());
                SkipNewlines();
            }

            return module;
        }

        private ScriptFunction ParseFunction()
        {
            var func = new ScriptFunction();
            Consume(TokenType.Def);
            func.Name = Consume(TokenType.Identifier).Value;
            Consume(TokenType.LParen);

            // Parameters
            if (Current.Type != TokenType.RParen)
            {
                func.Parameters.Add(Consume(TokenType.Identifier).Value);
                while (Match(TokenType.Comma))
                {
                    func.Parameters.Add(Consume(TokenType.Identifier).Value);
                }
            }
            Consume(TokenType.RParen);
            Consume(TokenType.Colon);
            Consume(TokenType.Newline);

            // Body
            func.Body = ParseBlock();
            return func;
        }

        private List<Stmt> ParseBlock()
        {
            Consume(TokenType.Indent);
            var stmts = new List<Stmt>();

            while (Current.Type != TokenType.Dedent && Current.Type != TokenType.Eof)
            {
                SkipNewlines();
                if (Current.Type == TokenType.Dedent || Current.Type == TokenType.Eof)
                    break;
                var stmt = ParseStatement();
                if (stmt != null)
                    stmts.Add(stmt);
            }

            if (Current.Type == TokenType.Dedent)
                Consume(TokenType.Dedent);

            return stmts;
        }

        private Stmt ParseStatement()
        {
            switch (Current.Type)
            {
                case TokenType.If:
                    return ParseIf();
                case TokenType.While:
                    return ParseWhile();
                case TokenType.Return:
                    return ParseReturn();
                case TokenType.Assert:
                    return ParseAssert();
                case TokenType.Pass:
                    Consume(TokenType.Pass);
                    Consume(TokenType.Newline);
                    return null; // pass is a no-op, skip
                default:
                    return ParseAssignOrExpr();
            }
        }

        private Stmt ParseIf()
        {
            // Consume 'if' or 'elif'
            int line = Current.Line, col = Current.Column;
            if (Current.Type == TokenType.If)
                Consume(TokenType.If);
            else
                Consume(TokenType.Elif);

            var condition = ParseExpression();
            Consume(TokenType.Colon);
            Consume(TokenType.Newline);
            var thenBody = ParseBlock();
            SkipNewlines();

            List<Stmt> elseBody = null;

            if (Current.Type == TokenType.Elif)
            {
                // elif becomes nested if in else
                var elifStmt = ParseIf();
                elseBody = new List<Stmt> { elifStmt };
            }
            else if (Current.Type == TokenType.Else)
            {
                Consume(TokenType.Else);
                Consume(TokenType.Colon);
                Consume(TokenType.Newline);
                elseBody = ParseBlock();
                SkipNewlines();
            }

            return new IfStmt(condition, thenBody, elseBody) { Line = line, Col = col };
        }

        private Stmt ParseWhile()
        {
            int line = Current.Line, col = Current.Column;
            Consume(TokenType.While);
            var condition = ParseExpression();
            Consume(TokenType.Colon);
            Consume(TokenType.Newline);
            var body = ParseBlock();
            return new WhileStmt(condition, body) { Line = line, Col = col };
        }

        private Stmt ParseReturn()
        {
            int line = Current.Line, col = Current.Column;
            Consume(TokenType.Return);
            if (Current.Type == TokenType.Newline)
            {
                Consume(TokenType.Newline);
                return new ReturnStmt(null) { Line = line, Col = col };
            }
            var expr = ParseExpression();
            Consume(TokenType.Newline);
            return new ReturnStmt(expr) { Line = line, Col = col };
        }

        private Stmt ParseAssert()
        {
            int line = Current.Line, col = Current.Column;
            Consume(TokenType.Assert);
            var expr = ParseExpression();
            Consume(TokenType.Newline);
            return new AssertStmt(expr) { Line = line, Col = col };
        }

        private Stmt ParseAssignOrExpr()
        {
            int line = Current.Line, col = Current.Column;
            var expr = ParseExpression();

            if (Current.Type == TokenType.Assign)
            {
                Consume(TokenType.Assign);
                var value = ParseExpression();
                Consume(TokenType.Newline);

                // Determine assignment type
                if (expr is IdentifierExpr ident)
                {
                    return new AssignStmt(ident.Name, value) { Line = line, Col = col };
                }
                else if (expr is AttrAccessExpr attr)
                {
                    return new AttrAssignStmt(attr.Object, attr.Attribute, value) { Line = line, Col = col };
                }
                else if (expr is IndexAccessExpr idx)
                {
                    return new IndexAssignStmt(idx.Object, idx.Index, value) { Line = line, Col = col };
                }
                else
                {
                    throw new FormatException(string.Format(
                        "Line {0}: invalid assignment target", Current.Line));
                }
            }

            Consume(TokenType.Newline);
            return new ExprStmt(expr) { Line = line, Col = col };
        }

        #region Expression Parsing (Precedence Climbing)

        private Expr ParseExpression()
        {
            return ParseOr();
        }

        private Expr ParseOr()
        {
            var left = ParseAnd();
            while (Current.Type == TokenType.Or)
            {
                int line = Current.Line, col = Current.Column;
                Consume();
                left = new BinaryExpr(left, BinaryOp.Or, ParseAnd()) { Line = line, Col = col };
            }
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseNot();
            while (Current.Type == TokenType.And)
            {
                int line = Current.Line, col = Current.Column;
                Consume();
                left = new BinaryExpr(left, BinaryOp.And, ParseNot()) { Line = line, Col = col };
            }
            return left;
        }

        private Expr ParseNot()
        {
            if (Current.Type == TokenType.Not)
            {
                int line = Current.Line, col = Current.Column;
                Consume();
                return new UnaryExpr(UnaryOp.Not, ParseNot()) { Line = line, Col = col };
            }
            return ParseComparison();
        }

        private Expr ParseComparison()
        {
            var left = ParseAddSub();
            while (true)
            {
                BinaryOp op;
                int line = Current.Line, col = Current.Column;
                switch (Current.Type)
                {
                    case TokenType.Eq: op = BinaryOp.Eq; break;
                    case TokenType.Ne: op = BinaryOp.Ne; break;
                    case TokenType.Gt: op = BinaryOp.Gt; break;
                    case TokenType.Ge: op = BinaryOp.Ge; break;
                    default: return left;
                }
                Consume();
                left = new BinaryExpr(left, op, ParseAddSub()) { Line = line, Col = col };
            }
        }

        private Expr ParseAddSub()
        {
            var left = ParseMulDiv();
            while (Current.Type == TokenType.Plus || Current.Type == TokenType.Minus)
            {
                int line = Current.Line, col = Current.Column;
                var op = Current.Type == TokenType.Plus ? BinaryOp.Add : BinaryOp.Sub;
                Consume();
                left = new BinaryExpr(left, op, ParseMulDiv()) { Line = line, Col = col };
            }
            return left;
        }

        private Expr ParseMulDiv()
        {
            var left = ParseUnary();
            while (Current.Type == TokenType.Star || Current.Type == TokenType.Slash || Current.Type == TokenType.Percent)
            {
                BinaryOp op;
                int line = Current.Line, col = Current.Column;
                switch (Current.Type)
                {
                    case TokenType.Star: op = BinaryOp.Mul; break;
                    case TokenType.Slash: op = BinaryOp.Div; break;
                    default: op = BinaryOp.Mod; break;
                }
                Consume();
                left = new BinaryExpr(left, op, ParseUnary()) { Line = line, Col = col };
            }
            return left;
        }

        private Expr ParseUnary()
        {
            if (Current.Type == TokenType.Minus)
            {
                int line = Current.Line, col = Current.Column;
                Consume();
                return new UnaryExpr(UnaryOp.Neg, ParsePostfix()) { Line = line, Col = col };
            }
            return ParsePostfix();
        }

        private Expr ParsePostfix()
        {
            var expr = ParsePrimary();

            while (true)
            {
                if (Current.Type == TokenType.Dot)
                {
                    int line = Current.Line, col = Current.Column;
                    Consume();
                    var attr = Consume(TokenType.Identifier).Value;

                    // Check for method call: obj.method(args)
                    if (Current.Type == TokenType.LParen)
                    {
                        Consume(TokenType.LParen);
                        var args = ParseArgList();
                        Consume(TokenType.RParen);
                        var attrExpr = new AttrAccessExpr(expr, attr) { Line = line, Col = col };
                        expr = new CallExpr(attrExpr, args) { Line = line, Col = col };
                    }
                    else
                    {
                        expr = new AttrAccessExpr(expr, attr) { Line = line, Col = col };
                    }
                }
                else if (Current.Type == TokenType.LBracket)
                {
                    int line = Current.Line, col = Current.Column;
                    Consume(TokenType.LBracket);
                    var index = ParseExpression();
                    Consume(TokenType.RBracket);
                    expr = new IndexAccessExpr(expr, index) { Line = line, Col = col };
                }
                else if (Current.Type == TokenType.LParen && expr is IdentifierExpr)
                {
                    // Direct function call: FuncName(args)
                    int line = expr.Line, col = expr.Col;
                    Consume(TokenType.LParen);
                    var args = ParseArgList();
                    Consume(TokenType.RParen);
                    expr = new CallExpr(expr, args) { Line = line, Col = col };
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expr ParsePrimary()
        {
            int line = Current.Line, col = Current.Column;
            switch (Current.Type)
            {
                case TokenType.FloatLiteral:
                {
                    var val = float.Parse(Consume().Value, CultureInfo.InvariantCulture);
                    return new FloatLiteral(val) { Line = line, Col = col };
                }

                case TokenType.StringLiteral:
                {
                    return new StringLiteral(Consume().Value) { Line = line, Col = col };
                }

                case TokenType.BytesLiteral:
                {
                    var hex = Consume().Value;
                    return new BytesLiteral(ParseHexBytes(hex)) { Line = line, Col = col };
                }

                case TokenType.True:
                    Consume();
                    return new BoolLiteral(true) { Line = line, Col = col };

                case TokenType.False:
                    Consume();
                    return new BoolLiteral(false) { Line = line, Col = col };

                case TokenType.None:
                    Consume();
                    return new NoneLiteral() { Line = line, Col = col };

                case TokenType.Identifier:
                    return new IdentifierExpr(Consume().Value) { Line = line, Col = col };

                case TokenType.LParen:
                {
                    Consume(TokenType.LParen);
                    var expr = ParseExpression();
                    Consume(TokenType.RParen);
                    return expr; // keep inner expression's position
                }

                case TokenType.LBracket:
                {
                    Consume(TokenType.LBracket);
                    var elements = new List<Expr>();
                    if (Current.Type != TokenType.RBracket)
                    {
                        elements.Add(ParseExpression());
                        while (Match(TokenType.Comma))
                            elements.Add(ParseExpression());
                    }
                    Consume(TokenType.RBracket);
                    return new ListExpr(elements) { Line = line, Col = col };
                }

                default:
                    throw new FormatException(string.Format(
                        "Line {0}: unexpected token {1} ('{2}')",
                        Current.Line, Current.Type, Current.Value));
            }
        }

        private List<Expr> ParseArgList()
        {
            var args = new List<Expr>();
            if (Current.Type != TokenType.RParen)
            {
                args.Add(ParseExpression());
                while (Match(TokenType.Comma))
                    args.Add(ParseExpression());
            }
            return args;
        }

        #endregion

        #region Helpers

        private static uint ParseHexUint(string hex)
        {
            if (hex.StartsWith("0x") || hex.StartsWith("0X"))
                hex = hex.Substring(2);
            return uint.Parse(hex, NumberStyles.HexNumber);
        }

        private static byte[] ParseHexBytes(string hex)
        {
            if (hex.Length % 2 != 0)
                throw new FormatException("Bytes literal must have even number of hex digits: @" + hex);
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber);
            return bytes;
        }

        #endregion
    }
}
