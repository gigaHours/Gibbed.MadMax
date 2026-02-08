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

namespace Gibbed.MadMax.XvmScript
{
    public class AstPrinter
    {
        private readonly TextWriter _writer;
        private int _indent;
        private const string IndentStr = "    ";

        public AstPrinter(TextWriter writer)
        {
            _writer = writer;
            _indent = 0;
        }

        public void Print(ScriptModule module)
        {
            if (!string.IsNullOrEmpty(module.Name))
            {
                WriteLine("module {0}", module.Name);
            }

            if (module.SourceHash != 0)
            {
                WriteLine("#! source_hash: 0x{0:X8}", module.SourceHash);
            }

            foreach (var imp in module.Imports)
            {
                WriteLine("import @{0:X8}", imp);
            }

            if (module.Imports.Count > 0 || !string.IsNullOrEmpty(module.Name))
            {
                _writer.WriteLine();
            }

            for (int i = 0; i < module.Functions.Count; i++)
            {
                if (i > 0) _writer.WriteLine();
                PrintFunction(module.Functions[i]);
            }
        }

        private void PrintFunction(ScriptFunction func)
        {
            if (func.NameHash != 0)
            {
                WriteLine("#! hash: 0x{0:X8}", func.NameHash);
            }
            var paramsStr = string.Join(", ", func.Parameters);
            WriteLine("def {0}({1}):", func.Name, paramsStr);
            _indent++;
            if (func.Body.Count == 0)
            {
                WriteLine("pass");
            }
            else
            {
                foreach (var stmt in func.Body)
                {
                    PrintStmt(stmt);
                }
            }
            _indent--;
        }

        private void PrintStmt(Stmt stmt)
        {
            if (stmt is AssignStmt assign)
            {
                WriteIndent();
                _writer.Write("{0} = ", assign.VariableName);
                PrintExpr(assign.Value);
                _writer.WriteLine();
            }
            else if (stmt is AttrAssignStmt attrAssign)
            {
                WriteIndent();
                PrintExpr(attrAssign.Object);
                _writer.Write(".{0} = ", attrAssign.Attribute);
                PrintExpr(attrAssign.Value);
                _writer.WriteLine();
            }
            else if (stmt is IndexAssignStmt indexAssign)
            {
                WriteIndent();
                PrintExpr(indexAssign.Object);
                _writer.Write("[");
                PrintExpr(indexAssign.Index);
                _writer.Write("] = ");
                PrintExpr(indexAssign.Value);
                _writer.WriteLine();
            }
            else if (stmt is ExprStmt exprStmt)
            {
                WriteIndent();
                PrintExpr(exprStmt.Expression);
                _writer.WriteLine();
            }
            else if (stmt is IfStmt ifStmt)
            {
                WriteIndent();
                _writer.Write("if ");
                PrintExpr(ifStmt.Condition);
                _writer.WriteLine(":");
                _indent++;
                foreach (var s in ifStmt.ThenBody)
                    PrintStmt(s);
                if (ifStmt.ThenBody.Count == 0)
                    WriteLine("pass");
                _indent--;

                if (ifStmt.ElseBody != null && ifStmt.ElseBody.Count > 0)
                {
                    PrintElseOrElif(ifStmt.ElseBody);
                }
            }
            else if (stmt is WhileStmt whileStmt)
            {
                WriteIndent();
                _writer.Write("while ");
                PrintExpr(whileStmt.Condition);
                _writer.WriteLine(":");
                _indent++;
                foreach (var s in whileStmt.Body)
                    PrintStmt(s);
                if (whileStmt.Body.Count == 0)
                    WriteLine("pass");
                _indent--;
            }
            else if (stmt is ReturnStmt retStmt)
            {
                if (retStmt.Value != null)
                {
                    WriteIndent();
                    _writer.Write("return ");
                    PrintExpr(retStmt.Value);
                    _writer.WriteLine();
                }
                else
                {
                    WriteLine("return");
                }
            }
            else if (stmt is AssertStmt assertStmt)
            {
                WriteIndent();
                _writer.Write("assert ");
                PrintExpr(assertStmt.Value);
                _writer.WriteLine();
            }
        }

        private void PrintElseOrElif(List<Stmt> elseBody)
        {
            // Detect elif pattern: else body is a single IfStmt
            if (elseBody.Count == 1 && elseBody[0] is IfStmt elifStmt)
            {
                WriteIndent();
                _writer.Write("elif ");
                PrintExpr(elifStmt.Condition);
                _writer.WriteLine(":");
                _indent++;
                foreach (var s in elifStmt.ThenBody)
                    PrintStmt(s);
                if (elifStmt.ThenBody.Count == 0)
                    WriteLine("pass");
                _indent--;

                if (elifStmt.ElseBody != null && elifStmt.ElseBody.Count > 0)
                {
                    // Recursively handle more elif/else
                    PrintElseOrElif(elifStmt.ElseBody);
                }
            }
            else
            {
                WriteLine("else:");
                _indent++;
                foreach (var s in elseBody)
                    PrintStmt(s);
                _indent--;
            }
        }

        private void PrintExpr(Expr expr, int parentPrecedence = 0)
        {
            if (expr is FloatLiteral floatLit)
            {
                // Use "R" format for round-trip; ensure at least one decimal point
                var s = floatLit.Value.ToString("R", CultureInfo.InvariantCulture);
                if (!s.Contains(".") && !s.Contains("E") && !s.Contains("e"))
                    s += ".0";
                _writer.Write(s);
            }
            else if (expr is BoolLiteral boolLit)
            {
                _writer.Write(boolLit.Value ? "true" : "false");
            }
            else if (expr is StringLiteral strLit)
            {
                _writer.Write("\"{0}\"", Escape(strLit.Value));
            }
            else if (expr is BytesLiteral bytesLit)
            {
                _writer.Write("@");
                foreach (var b in bytesLit.Value)
                    _writer.Write("{0:X2}", b);
            }
            else if (expr is NoneLiteral)
            {
                _writer.Write("none");
            }
            else if (expr is IdentifierExpr ident)
            {
                _writer.Write(ident.Name);
            }
            else if (expr is AttrAccessExpr attr)
            {
                PrintExpr(attr.Object, 100);
                _writer.Write(".{0}", attr.Attribute);
            }
            else if (expr is IndexAccessExpr index)
            {
                PrintExpr(index.Object, 100);
                _writer.Write("[");
                PrintExpr(index.Index);
                _writer.Write("]");
            }
            else if (expr is CallExpr call)
            {
                PrintExpr(call.Callable, 100);
                _writer.Write("(");
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    if (i > 0) _writer.Write(", ");
                    PrintExpr(call.Arguments[i]);
                }
                _writer.Write(")");
            }
            else if (expr is BinaryExpr bin)
            {
                int prec = GetPrecedence(bin.Op);
                bool needParens = prec < parentPrecedence;
                if (needParens) _writer.Write("(");
                PrintExpr(bin.Left, prec);
                _writer.Write(" {0} ", GetOpString(bin.Op));
                PrintExpr(bin.Right, prec + 1);
                if (needParens) _writer.Write(")");
            }
            else if (expr is UnaryExpr unary)
            {
                if (unary.Op == UnaryOp.Not)
                {
                    bool needParens = 15 < parentPrecedence;
                    if (needParens) _writer.Write("(");
                    _writer.Write("not ");
                    PrintExpr(unary.Operand, 15);
                    if (needParens) _writer.Write(")");
                }
                else // Neg
                {
                    _writer.Write("-");
                    PrintExpr(unary.Operand, 90);
                }
            }
            else if (expr is ListExpr list)
            {
                _writer.Write("[");
                for (int i = 0; i < list.Elements.Count; i++)
                {
                    if (i > 0) _writer.Write(", ");
                    PrintExpr(list.Elements[i]);
                }
                _writer.Write("]");
            }
        }

        private static int GetPrecedence(BinaryOp op)
        {
            switch (op)
            {
                case BinaryOp.Or: return 10;
                case BinaryOp.And: return 20;
                case BinaryOp.Eq:
                case BinaryOp.Ne:
                case BinaryOp.Gt:
                case BinaryOp.Ge: return 30;
                case BinaryOp.Add:
                case BinaryOp.Sub: return 40;
                case BinaryOp.Mul:
                case BinaryOp.Div:
                case BinaryOp.Mod: return 50;
                default: return 0;
            }
        }

        private static string GetOpString(BinaryOp op)
        {
            switch (op)
            {
                case BinaryOp.Add: return "+";
                case BinaryOp.Sub: return "-";
                case BinaryOp.Mul: return "*";
                case BinaryOp.Div: return "/";
                case BinaryOp.Mod: return "%";
                case BinaryOp.Eq: return "==";
                case BinaryOp.Ne: return "!=";
                case BinaryOp.Gt: return ">";
                case BinaryOp.Ge: return ">=";
                case BinaryOp.And: return "and";
                case BinaryOp.Or: return "or";
                default: return "??";
            }
        }

        private static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private void WriteIndent()
        {
            for (int i = 0; i < _indent; i++)
                _writer.Write(IndentStr);
        }

        private void WriteLine(string format, params object[] args)
        {
            WriteIndent();
            _writer.WriteLine(format, args);
        }

        private void WriteLine(string text)
        {
            WriteIndent();
            _writer.WriteLine(text);
        }
    }
}
