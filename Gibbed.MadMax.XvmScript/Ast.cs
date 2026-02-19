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

using System.Collections.Generic;

namespace Gibbed.MadMax.XvmScript
{
    #region Module-level AST

    public class ScriptModule
    {
        public string Name;
        public uint SourceHash;
        public List<uint> Imports = new List<uint>();
        public List<ScriptFunction> Functions = new List<ScriptFunction>();
    }

    public class ScriptFunction
    {
        public string Name;
        public uint NameHash; // 0 = auto-compute from name
        public List<string> Parameters = new List<string>();
        public List<Stmt> Body = new List<Stmt>();
    }

    #endregion

    #region Expressions

    public enum BinaryOp
    {
        Add, Sub, Mul, Div, Mod,
        Eq, Ne, Gt, Ge,
        And, Or,
    }

    public enum UnaryOp
    {
        Not, Neg,
    }

    public abstract class Expr
    {
        public int Line;
        public int Col;
    }

    public class FloatLiteral : Expr
    {
        public float Value;
        public FloatLiteral(float value) { Value = value; }
    }

    public class BoolLiteral : Expr
    {
        public bool Value;
        public BoolLiteral(bool value) { Value = value; }
    }

    public class StringLiteral : Expr
    {
        public string Value;
        public StringLiteral(string value) { Value = value; }
    }

    public class BytesLiteral : Expr
    {
        public byte[] Value;
        public BytesLiteral(byte[] value) { Value = value; }
    }

    public class NoneLiteral : Expr { }

    public class IdentifierExpr : Expr
    {
        public string Name;
        public IdentifierExpr(string name) { Name = name; }
    }

    public class AttrAccessExpr : Expr
    {
        public Expr Object;
        public string Attribute;
        public AttrAccessExpr(Expr obj, string attr)
        {
            Object = obj;
            Attribute = attr;
        }
    }

    public class IndexAccessExpr : Expr
    {
        public Expr Object;
        public Expr Index;
        public IndexAccessExpr(Expr obj, Expr index)
        {
            Object = obj;
            Index = index;
        }
    }

    public class CallExpr : Expr
    {
        public Expr Callable;
        public List<Expr> Arguments;
        public CallExpr(Expr callable, List<Expr> args)
        {
            Callable = callable;
            Arguments = args;
        }
    }

    public class BinaryExpr : Expr
    {
        public Expr Left;
        public Expr Right;
        public BinaryOp Op;
        public BinaryExpr(Expr left, BinaryOp op, Expr right)
        {
            Left = left;
            Op = op;
            Right = right;
        }
    }

    public class UnaryExpr : Expr
    {
        public Expr Operand;
        public UnaryOp Op;
        public UnaryExpr(UnaryOp op, Expr operand)
        {
            Op = op;
            Operand = operand;
        }
    }

    public class ListExpr : Expr
    {
        public List<Expr> Elements;
        public ListExpr(List<Expr> elements) { Elements = elements; }
    }

    #endregion

    #region Statements

    public abstract class Stmt
    {
        public int Line;
        public int Col;
    }

    public class AssignStmt : Stmt
    {
        public string VariableName;
        public Expr Value;
        public AssignStmt(string name, Expr value)
        {
            VariableName = name;
            Value = value;
        }
    }

    public class AttrAssignStmt : Stmt
    {
        public Expr Object;
        public string Attribute;
        public Expr Value;
        public AttrAssignStmt(Expr obj, string attr, Expr value)
        {
            Object = obj;
            Attribute = attr;
            Value = value;
        }
    }

    public class IndexAssignStmt : Stmt
    {
        public Expr Object;
        public Expr Index;
        public Expr Value;
        public IndexAssignStmt(Expr obj, Expr index, Expr value)
        {
            Object = obj;
            Index = index;
            Value = value;
        }
    }

    public class ExprStmt : Stmt
    {
        public Expr Expression;
        public ExprStmt(Expr expr) { Expression = expr; }
    }

    public class IfStmt : Stmt
    {
        public Expr Condition;
        public List<Stmt> ThenBody;
        public List<Stmt> ElseBody; // null if no else
        public IfStmt(Expr condition, List<Stmt> thenBody, List<Stmt> elseBody)
        {
            Condition = condition;
            ThenBody = thenBody;
            ElseBody = elseBody;
        }
    }

    public class WhileStmt : Stmt
    {
        public Expr Condition;
        public List<Stmt> Body;
        public WhileStmt(Expr condition, List<Stmt> body)
        {
            Condition = condition;
            Body = body;
        }
    }

    public class ReturnStmt : Stmt
    {
        public Expr Value; // null for return without value
        public ReturnStmt(Expr value) { Value = value; }
    }

    public class AssertStmt : Stmt
    {
        public Expr Value;
        public AssertStmt(Expr value) { Value = value; }
    }

    public class BreakStmt : Stmt { }

    #endregion
}
