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
using Gibbed.MadMax.FileFormats;
using Gibbed.MadMax.XvmScript;

namespace Gibbed.MadMax.XvmDecompile
{
    /// <summary>
    /// Result of processing a basic block through expression recovery.
    /// </summary>
    public class BlockResult
    {
        public BasicBlock Block;
        public List<Stmt> Statements = new List<Stmt>();
        public Expr BranchCondition; // non-null if block ends with jz
        public bool HasReturn;
        public Expr ReturnValue; // non-null if ret 1
    }

    /// <summary>
    /// Simulates the XVM stack to recover high-level expressions and statements
    /// from linear bytecode within basic blocks.
    /// </summary>
    public static class ExpressionRecovery
    {
        public static BlockResult ProcessBlock(
            BasicBlock block,
            int argCount,
            Dictionary<int, string> localNames)
        {
            var result = new BlockResult();
            result.Block = block;
            var stack = new Stack<Expr>();

            foreach (var instr in block.Instructions)
            {
                switch (instr.Opcode)
                {
                    case XvmOpcode.LoadLocal:
                    {
                        string name;
                        if (!localNames.TryGetValue(instr.Operand, out name))
                        {
                            name = instr.Operand < argCount
                                ? string.Format("arg{0}", instr.Operand)
                                : string.Format("local{0}", instr.Operand);
                            localNames[instr.Operand] = name;
                        }
                        stack.Push(new IdentifierExpr(name));
                        break;
                    }

                    case XvmOpcode.StoreLocal:
                    {
                        string name;
                        if (!localNames.TryGetValue(instr.Operand, out name))
                        {
                            name = instr.Operand < argCount
                                ? string.Format("arg{0}", instr.Operand)
                                : string.Format("local{0}", instr.Operand);
                            localNames[instr.Operand] = name;
                        }
                        var val = SafePop(stack);
                        result.Statements.Add(new AssignStmt(name, val));
                        break;
                    }

                    case XvmOpcode.LoadGlobal:
                    {
                        var name = instr.AttrName ?? string.Format("_global_{0}", instr.Operand);
                        stack.Push(new IdentifierExpr(name));
                        break;
                    }

                    case XvmOpcode.LoadAttr:
                    {
                        var obj = SafePop(stack);
                        var name = instr.AttrName ?? string.Format("_attr_{0}", instr.Operand);
                        stack.Push(new AttrAccessExpr(obj, name));
                        break;
                    }

                    case XvmOpcode.StoreAttr:
                    {
                        // XVM stack: push value, push obj → TOS=obj
                        var obj = SafePop(stack);
                        var val = SafePop(stack);
                        var name = instr.AttrName ?? string.Format("_attr_{0}", instr.Operand);
                        result.Statements.Add(new AttrAssignStmt(obj, name, val));
                        break;
                    }

                    case XvmOpcode.LoadConst:
                    {
                        switch (instr.ConstKind)
                        {
                            case ConstantKind.None:
                                stack.Push(new NoneLiteral());
                                break;
                            case ConstantKind.Float:
                                stack.Push(new FloatLiteral(instr.FloatValue));
                                break;
                            case ConstantKind.String:
                                stack.Push(new StringLiteral(instr.StringValue));
                                break;
                            case ConstantKind.Bytes:
                                stack.Push(new BytesLiteral(instr.BytesValue));
                                break;
                        }
                        break;
                    }

                    case XvmOpcode.LoadBool:
                    {
                        stack.Push(new BoolLiteral(instr.BoolValue));
                        break;
                    }

                    case XvmOpcode.LoadItem:
                    {
                        var index = SafePop(stack);
                        var obj = SafePop(stack);
                        stack.Push(new IndexAccessExpr(obj, index));
                        break;
                    }

                    case XvmOpcode.StoreItem:
                    {
                        var val = SafePop(stack);
                        var index = SafePop(stack);
                        var obj = SafePop(stack);
                        result.Statements.Add(new IndexAssignStmt(obj, index, val));
                        break;
                    }

                    case XvmOpcode.Add:
                        EmitBinaryOp(stack, BinaryOp.Add);
                        break;
                    case XvmOpcode.Sub:
                        EmitBinaryOp(stack, BinaryOp.Sub);
                        break;
                    case XvmOpcode.Mul:
                        EmitBinaryOp(stack, BinaryOp.Mul);
                        break;
                    case XvmOpcode.Div:
                        EmitBinaryOp(stack, BinaryOp.Div);
                        break;
                    case XvmOpcode.Mod:
                        EmitBinaryOp(stack, BinaryOp.Mod);
                        break;
                    case XvmOpcode.CmpEq:
                        EmitBinaryOp(stack, BinaryOp.Eq);
                        break;
                    case XvmOpcode.CmpNe:
                        EmitBinaryOp(stack, BinaryOp.Ne);
                        break;
                    case XvmOpcode.CmpG:
                        EmitBinaryOp(stack, BinaryOp.Gt);
                        break;
                    case XvmOpcode.CmpGe:
                        EmitBinaryOp(stack, BinaryOp.Ge);
                        break;
                    case XvmOpcode.And:
                        EmitBinaryOp(stack, BinaryOp.And);
                        break;
                    case XvmOpcode.Or:
                        EmitBinaryOp(stack, BinaryOp.Or);
                        break;

                    case XvmOpcode.Not:
                    {
                        var operand = SafePop(stack);
                        stack.Push(new UnaryExpr(UnaryOp.Not, operand));
                        break;
                    }

                    case XvmOpcode.Neg:
                    {
                        var operand = SafePop(stack);
                        stack.Push(new UnaryExpr(UnaryOp.Neg, operand));
                        break;
                    }

                    case XvmOpcode.Call:
                    {
                        var callable = SafePop(stack);
                        int argCountCall = instr.Operand;
                        var args = new List<Expr>();
                        for (int i = 0; i < argCountCall; i++)
                            args.Insert(0, SafePop(stack));
                        stack.Push(new CallExpr(callable, args));
                        break;
                    }

                    case XvmOpcode.MakeList:
                    {
                        int count = instr.Operand;
                        var elements = new List<Expr>();
                        for (int i = 0; i < count; i++)
                            elements.Insert(0, SafePop(stack));
                        stack.Push(new ListExpr(elements));
                        break;
                    }

                    case XvmOpcode.Pop:
                    {
                        var expr = SafePop(stack);
                        result.Statements.Add(new ExprStmt(expr));
                        break;
                    }

                    case XvmOpcode.Assert:
                    {
                        var val = SafePop(stack);
                        result.Statements.Add(new AssertStmt(val));
                        break;
                    }

                    case XvmOpcode.DebugOut:
                    {
                        var val = SafePop(stack);
                        // Emit as a function call for readability
                        result.Statements.Add(new ExprStmt(
                            new CallExpr(
                                new IdentifierExpr("__debugout"),
                                new List<Expr> { val })));
                        break;
                    }

                    case XvmOpcode.Jz:
                    {
                        var cond = SafePop(stack);
                        result.BranchCondition = cond;
                        break;
                    }

                    case XvmOpcode.Jmp:
                        // Unconditional jump — no stack effect
                        break;

                    case XvmOpcode.Ret:
                    {
                        result.HasReturn = true;
                        if (instr.Operand == 1)
                        {
                            result.ReturnValue = SafePop(stack);
                        }
                        break;
                    }
                }
            }

            return result;
        }

        private static void EmitBinaryOp(Stack<Expr> stack, BinaryOp op)
        {
            var right = SafePop(stack);
            var left = SafePop(stack);
            stack.Push(new BinaryExpr(left, op, right));
        }

        private static Expr SafePop(Stack<Expr> stack)
        {
            if (stack.Count > 0)
                return stack.Pop();
            return new IdentifierExpr("__stack_underflow");
        }
    }
}
