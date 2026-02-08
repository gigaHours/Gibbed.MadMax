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
using Gibbed.MadMax.XvmScript;

namespace Gibbed.MadMax.XvmCompile
{
    /// <summary>
    /// Resolves variable references: determines which identifiers are local variables,
    /// which are global objects, and assigns local variable slot indices.
    /// </summary>
    public class SemanticAnalysis
    {
        private readonly HashSet<string> _engineGlobals;
        private readonly ScriptModule _module;
        private readonly HashSet<string> _moduleFunctions;

        public SemanticAnalysis(ScriptModule module, string globalsFilePath = null)
        {
            _module = module;
            _moduleFunctions = new HashSet<string>();
            foreach (var f in module.Functions)
                _moduleFunctions.Add(f.Name);

            _engineGlobals = new HashSet<string>();
            if (globalsFilePath != null && File.Exists(globalsFilePath))
            {
                LoadGlobalsFromFile(globalsFilePath);
            }
            else
            {
                // Try to find xvm_globals.txt next to the executable
                var exeDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                var defaultPath = Path.Combine(exeDir, "xvm_globals.txt");
                if (File.Exists(defaultPath))
                {
                    LoadGlobalsFromFile(defaultPath);
                }
            }

            if (_engineGlobals.Count == 0)
            {
                Console.Error.WriteLine("Warning: no xvm_globals.txt found. " +
                    "All non-local identifiers will be treated as globals.");
            }
        }

        private void LoadGlobalsFromFile(string filePath)
        {
            foreach (var rawLine in File.ReadAllLines(filePath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;
                _engineGlobals.Add(line);
            }
        }

        /// <summary>
        /// For each function, compute the local variable table.
        /// Returns a map: function name -> FunctionScope
        /// </summary>
        public Dictionary<string, FunctionScope> Analyze()
        {
            var scopes = new Dictionary<string, FunctionScope>();

            foreach (var func in _module.Functions)
            {
                var scope = new FunctionScope();
                scope.ArgCount = func.Parameters.Count;

                // Parameters are locals 0..N-1
                for (int i = 0; i < func.Parameters.Count; i++)
                {
                    scope.Locals[func.Parameters[i]] = i;
                }
                scope.NextSlot = func.Parameters.Count;

                // Scan body for local variable assignments
                ScanStmts(func.Body, scope);

                scopes[func.Name] = scope;
            }

            return scopes;
        }

        private void ScanStmts(List<Stmt> stmts, FunctionScope scope)
        {
            foreach (var stmt in stmts)
            {
                if (stmt == null) continue;
                ScanStmt(stmt, scope);
            }
        }

        private void ScanStmt(Stmt stmt, FunctionScope scope)
        {
            if (stmt is AssignStmt assign)
            {
                // First assignment to an identifier creates a local
                if (!scope.Locals.ContainsKey(assign.VariableName) &&
                    !IsGlobal(assign.VariableName))
                {
                    scope.Locals[assign.VariableName] = scope.NextSlot++;
                }
                ScanExpr(assign.Value, scope);
            }
            else if (stmt is AttrAssignStmt attrAssign)
            {
                ScanExpr(attrAssign.Object, scope);
                ScanExpr(attrAssign.Value, scope);
            }
            else if (stmt is IndexAssignStmt indexAssign)
            {
                ScanExpr(indexAssign.Object, scope);
                ScanExpr(indexAssign.Index, scope);
                ScanExpr(indexAssign.Value, scope);
            }
            else if (stmt is ExprStmt exprStmt)
            {
                ScanExpr(exprStmt.Expression, scope);
            }
            else if (stmt is IfStmt ifStmt)
            {
                ScanExpr(ifStmt.Condition, scope);
                ScanStmts(ifStmt.ThenBody, scope);
                if (ifStmt.ElseBody != null)
                    ScanStmts(ifStmt.ElseBody, scope);
            }
            else if (stmt is WhileStmt whileStmt)
            {
                ScanExpr(whileStmt.Condition, scope);
                ScanStmts(whileStmt.Body, scope);
            }
            else if (stmt is ReturnStmt retStmt)
            {
                if (retStmt.Value != null)
                    ScanExpr(retStmt.Value, scope);
            }
            else if (stmt is AssertStmt assertStmt)
            {
                ScanExpr(assertStmt.Value, scope);
            }
        }

        private void ScanExpr(Expr expr, FunctionScope scope)
        {
            // We don't create new locals from expressions, just traverse
            if (expr is CallExpr call)
            {
                ScanExpr(call.Callable, scope);
                foreach (var arg in call.Arguments)
                    ScanExpr(arg, scope);
            }
            else if (expr is BinaryExpr bin)
            {
                ScanExpr(bin.Left, scope);
                ScanExpr(bin.Right, scope);
            }
            else if (expr is UnaryExpr unary)
            {
                ScanExpr(unary.Operand, scope);
            }
            else if (expr is AttrAccessExpr attr)
            {
                ScanExpr(attr.Object, scope);
            }
            else if (expr is IndexAccessExpr idx)
            {
                ScanExpr(idx.Object, scope);
                ScanExpr(idx.Index, scope);
            }
            else if (expr is ListExpr list)
            {
                foreach (var e in list.Elements)
                    ScanExpr(e, scope);
            }
        }

        /// <summary>
        /// Determines if an identifier refers to a global object or a module-level function.
        /// </summary>
        public bool IsGlobal(string name)
        {
            if (_moduleFunctions.Contains(name))
                return true;
            if (_engineGlobals.Count > 0)
                return _engineGlobals.Contains(name);
            // If no globals file loaded, don't restrict — CodeGenerator
            // will treat unknown identifiers as globals anyway
            return false;
        }
    }

    public class FunctionScope
    {
        public int ArgCount;
        public Dictionary<string, int> Locals = new Dictionary<string, int>();
        public int NextSlot;
    }
}
