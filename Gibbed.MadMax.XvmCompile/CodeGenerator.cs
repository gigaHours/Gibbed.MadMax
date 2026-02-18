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
using Gibbed.MadMax.XvmAssemble;

namespace Gibbed.MadMax.XvmCompile
{
    /// <summary>
    /// Generates XVM bytecode from an AST. Outputs DisParser.ParsedModule
    /// which can be fed into the existing Assembler pipeline.
    /// </summary>
    public class CodeGenerator
    {
        private readonly ScriptModule _module;
        private readonly Dictionary<string, FunctionScope> _scopes;
        private readonly SemanticAnalysis _semantics;

        // Per-function state
        private List<DisParser.ParsedInstruction> _instructions;
        private Dictionary<string, int> _labels;
        private int _labelCounter;
        private FunctionScope _currentScope;
        private ushort _debugLine;
        private ushort _debugCol;

        public CodeGenerator(
            ScriptModule module,
            Dictionary<string, FunctionScope> scopes,
            SemanticAnalysis semantics)
        {
            _module = module;
            _scopes = scopes;
            _semantics = semantics;
        }

        public DisParser.ParsedModule Generate()
        {
            var parsed = new DisParser.ParsedModule();
            parsed.Name = _module.Name;
            parsed.SourceHash = _module.SourceHash;
            parsed.HasDebugStrings = true;
            parsed.HasDebugInfo = true;

            foreach (var imp in _module.Imports)
                parsed.ImportHashes.Add(imp);

            foreach (var func in _module.Functions)
            {
                var pf = GenerateFunction(func);
                parsed.Functions.Add(pf);
            }

            return parsed;
        }

        private DisParser.ParsedFunction GenerateFunction(ScriptFunction func)
        {
            _instructions = new List<DisParser.ParsedInstruction>();
            _labels = new Dictionary<string, int>();
            _labelCounter = 0;
            _currentScope = _scopes[func.Name];
            _debugLine = 0;
            _debugCol = 0;

            // Generate body
            EmitStmts(func.Body);

            // Ensure function ends with a ret
            if (_instructions.Count == 0 ||
                _instructions[_instructions.Count - 1].Opcode != XvmOpcode.Ret)
            {
                Emit(XvmOpcode.Ret, DisParser.InstructionOperandType.Int, intOp: 0);
            }

            var pf = new DisParser.ParsedFunction();
            pf.Name = func.Name;
            pf.NameHash = func.NameHash;
            pf.ArgCount = (ushort)func.Parameters.Count;
            pf.Instructions = _instructions;
            pf.Labels = _labels;

            return pf;
        }

        #region Statement Emission

        private void EmitStmts(List<Stmt> stmts)
        {
            foreach (var stmt in stmts)
            {
                if (stmt != null)
                    EmitStmt(stmt);
            }
        }

        private void EmitStmt(Stmt stmt)
        {
            SetDebugPos(stmt);
            if (stmt is AssignStmt assign)
            {
                EmitExpr(assign.Value);
                int slot;
                if (_currentScope.Locals.TryGetValue(assign.VariableName, out slot))
                {
                    Emit(XvmOpcode.StoreLocal, DisParser.InstructionOperandType.Int, intOp: slot);
                }
                else
                {
                    throw new FormatException("Undefined variable: " + assign.VariableName);
                }
            }
            else if (stmt is AttrAssignStmt attrAssign)
            {
                // XVM stattr expects: push value, push obj → stattr
                EmitExpr(attrAssign.Value);
                EmitExpr(attrAssign.Object);
                Emit(XvmOpcode.StoreAttr, DisParser.InstructionOperandType.String,
                     stringOp: attrAssign.Attribute);
            }
            else if (stmt is IndexAssignStmt indexAssign)
            {
                // XVM stitem expects: push value, push obj, push index → stitem
                EmitExpr(indexAssign.Value);
                EmitExpr(indexAssign.Object);
                EmitExpr(indexAssign.Index);
                Emit(XvmOpcode.StoreItem, DisParser.InstructionOperandType.None);
            }
            else if (stmt is ExprStmt exprStmt)
            {
                EmitExpr(exprStmt.Expression);
                Emit(XvmOpcode.Pop, DisParser.InstructionOperandType.None);
            }
            else if (stmt is IfStmt ifStmt)
            {
                EmitIf(ifStmt);
            }
            else if (stmt is WhileStmt whileStmt)
            {
                EmitWhile(whileStmt);
            }
            else if (stmt is ReturnStmt retStmt)
            {
                if (retStmt.Value != null)
                {
                    EmitExpr(retStmt.Value);
                    Emit(XvmOpcode.Ret, DisParser.InstructionOperandType.Int, intOp: 1);
                }
                else
                {
                    Emit(XvmOpcode.Ret, DisParser.InstructionOperandType.Int, intOp: 0);
                }
            }
            else if (stmt is AssertStmt assertStmt)
            {
                EmitExpr(assertStmt.Value);
                Emit(XvmOpcode.Assert, DisParser.InstructionOperandType.None);
            }
        }

        private void EmitIf(IfStmt ifStmt)
        {
            EmitExpr(ifStmt.Condition);

            if (ifStmt.ElseBody == null || ifStmt.ElseBody.Count == 0)
            {
                // if-then (no else)
                var endLabel = NewLabel();
                EmitJz(endLabel);
                EmitStmts(ifStmt.ThenBody);
                PlaceLabel(endLabel);
            }
            else
            {
                // if-then-else
                var elseLabel = NewLabel();
                var endLabel = NewLabel();
                EmitJz(elseLabel);
                EmitStmts(ifStmt.ThenBody);
                EmitJmp(endLabel);
                PlaceLabel(elseLabel);
                EmitStmts(ifStmt.ElseBody);
                PlaceLabel(endLabel);
            }
        }

        private void EmitWhile(WhileStmt whileStmt)
        {
            var loopLabel = NewLabel();
            var endLabel = NewLabel();
            PlaceLabel(loopLabel);
            EmitExpr(whileStmt.Condition);
            EmitJz(endLabel);
            EmitStmts(whileStmt.Body);
            EmitJmp(loopLabel);
            PlaceLabel(endLabel);
        }

        #endregion

        #region Expression Emission

        private void EmitExpr(Expr expr)
        {
            SetDebugPos(expr);
            if (expr is FloatLiteral floatLit)
            {
                Emit(XvmOpcode.LoadConst, DisParser.InstructionOperandType.Float,
                     floatOp: floatLit.Value);
            }
            else if (expr is BoolLiteral boolLit)
            {
                Emit(XvmOpcode.LoadBool, DisParser.InstructionOperandType.Int,
                     intOp: boolLit.Value ? 1 : 0);
            }
            else if (expr is StringLiteral strLit)
            {
                Emit(XvmOpcode.LoadConst, DisParser.InstructionOperandType.String,
                     stringOp: strLit.Value);
            }
            else if (expr is BytesLiteral bytesLit)
            {
                Emit(XvmOpcode.LoadConst, DisParser.InstructionOperandType.Bytes,
                     bytesOp: bytesLit.Value);
            }
            else if (expr is NoneLiteral)
            {
                Emit(XvmOpcode.LoadConst, DisParser.InstructionOperandType.IsNone);
            }
            else if (expr is IdentifierExpr ident)
            {
                int slot;
                if (_currentScope.Locals.TryGetValue(ident.Name, out slot))
                {
                    Emit(XvmOpcode.LoadLocal, DisParser.InstructionOperandType.Int, intOp: slot);
                }
                else
                {
                    // Anything not local is a global (ldglob)
                    Emit(XvmOpcode.LoadGlobal, DisParser.InstructionOperandType.String,
                         stringOp: ident.Name);
                }
            }
            else if (expr is AttrAccessExpr attr)
            {
                EmitExpr(attr.Object);
                Emit(XvmOpcode.LoadAttr, DisParser.InstructionOperandType.String,
                     stringOp: attr.Attribute);
            }
            else if (expr is IndexAccessExpr idx)
            {
                EmitExpr(idx.Object);
                EmitExpr(idx.Index);
                Emit(XvmOpcode.LoadItem, DisParser.InstructionOperandType.None);
            }
            else if (expr is CallExpr call)
            {
                EmitCall(call);
            }
            else if (expr is BinaryExpr bin)
            {
                EmitExpr(bin.Left);
                EmitExpr(bin.Right);
                Emit(BinaryOpToOpcode(bin.Op), DisParser.InstructionOperandType.None);
            }
            else if (expr is UnaryExpr unary)
            {
                // Constant folding: -floatLiteral → single ldfloat with negated value
                if (unary.Op == UnaryOp.Neg && unary.Operand is FloatLiteral negFloat)
                {
                    Emit(XvmOpcode.LoadConst, DisParser.InstructionOperandType.Float,
                         floatOp: -negFloat.Value);
                }
                else
                {
                    EmitExpr(unary.Operand);
                    Emit(unary.Op == UnaryOp.Not ? XvmOpcode.Not : XvmOpcode.Neg,
                         DisParser.InstructionOperandType.None);
                }
            }
            else if (expr is ListExpr list)
            {
                foreach (var elem in list.Elements)
                    EmitExpr(elem);
                Emit(XvmOpcode.MakeList, DisParser.InstructionOperandType.Int,
                     intOp: list.Elements.Count);
            }
        }

        private void EmitCall(CallExpr call)
        {
            // XVM calling convention: push args first, then callable, then call N
            foreach (var arg in call.Arguments)
                EmitExpr(arg);

            EmitExpr(call.Callable);

            Emit(XvmOpcode.Call, DisParser.InstructionOperandType.Int,
                 intOp: call.Arguments.Count);
        }

        #endregion

        #region Helpers

        private string NewLabel()
        {
            return string.Format("label_{0}", _labelCounter++);
        }

        private void PlaceLabel(string label)
        {
            _labels[label] = _instructions.Count;
        }

        private void EmitJz(string label)
        {
            Emit(XvmOpcode.Jz, DisParser.InstructionOperandType.Label, labelOp: label);
        }

        private void EmitJmp(string label)
        {
            Emit(XvmOpcode.Jmp, DisParser.InstructionOperandType.Label, labelOp: label);
        }

        private void SetDebugPos(Expr expr)
        {
            if (expr.Line > 0)
            {
                _debugLine = (ushort)expr.Line;
                _debugCol = (ushort)expr.Col;
            }
        }

        private void SetDebugPos(Stmt stmt)
        {
            if (stmt.Line > 0)
            {
                _debugLine = (ushort)stmt.Line;
                _debugCol = (ushort)stmt.Col;
            }
        }

        private void Emit(
            XvmOpcode opcode,
            DisParser.InstructionOperandType operandType,
            int intOp = 0,
            float floatOp = 0,
            string stringOp = null,
            byte[] bytesOp = null,
            string labelOp = null)
        {
            var instr = new DisParser.ParsedInstruction();
            instr.Opcode = opcode;
            instr.OperandType = operandType;
            instr.IntOperand = intOp;
            instr.FloatOperand = floatOp;
            instr.StringOperand = stringOp;
            instr.BytesOperand = bytesOp;
            instr.LabelOperand = labelOp;
            instr.SourceLine = 0;
            instr.DebugLine = _debugLine;
            instr.DebugCol = _debugCol;
            _instructions.Add(instr);
        }

        private static XvmOpcode BinaryOpToOpcode(BinaryOp op)
        {
            switch (op)
            {
                case BinaryOp.Add: return XvmOpcode.Add;
                case BinaryOp.Sub: return XvmOpcode.Sub;
                case BinaryOp.Mul: return XvmOpcode.Mul;
                case BinaryOp.Div: return XvmOpcode.Div;
                case BinaryOp.Mod: return XvmOpcode.Mod;
                case BinaryOp.Eq: return XvmOpcode.CmpEq;
                case BinaryOp.Ne: return XvmOpcode.CmpNe;
                case BinaryOp.Gt: return XvmOpcode.CmpG;
                case BinaryOp.Ge: return XvmOpcode.CmpGe;
                case BinaryOp.And: return XvmOpcode.And;
                case BinaryOp.Or: return XvmOpcode.Or;
                default: throw new ArgumentException("Unknown binary op: " + op);
            }
        }

        #endregion
    }
}
