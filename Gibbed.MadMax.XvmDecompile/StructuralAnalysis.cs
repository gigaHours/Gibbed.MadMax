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
using System.Linq;
using Gibbed.MadMax.FileFormats;
using Gibbed.MadMax.XvmScript;

namespace Gibbed.MadMax.XvmDecompile
{
    /// <summary>
    /// Recovers high-level control flow structures (if/else, while, break) from a CFG.
    /// Uses a region-based approach: processes blocks in reverse postorder,
    /// recognizing patterns and reducing them to structured AST nodes.
    /// </summary>
    public class StructuralAnalysis
    {
        private readonly List<BasicBlock> _blocks;
        private readonly Dictionary<int, BlockResult> _blockResults;
        private readonly HashSet<int> _visited;

        // Stack of while-loop exit blocks for break detection
        private readonly Stack<BasicBlock> _whileExitStack = new Stack<BasicBlock>();

        public StructuralAnalysis(
            List<BasicBlock> blocks,
            Dictionary<int, BlockResult> blockResults)
        {
            _blocks = blocks;
            _blockResults = blockResults;
            _visited = new HashSet<int>();
        }

        public List<Stmt> Analyze()
        {
            if (_blocks.Count == 0)
                return new List<Stmt>();

            return ProcessRegion(_blocks[0], null);
        }

        /// <summary>
        /// Processes a region of blocks starting at 'block' and ending before 'exitBlock'.
        /// Returns a list of statements representing the structured code.
        /// </summary>
        private List<Stmt> ProcessRegion(BasicBlock block, BasicBlock exitBlock)
        {
            var stmts = new List<Stmt>();
            var current = block;

            while (current != null && current != exitBlock && !_visited.Contains(current.Id))
            {
                _visited.Add(current.Id);
                BlockResult br;
                _blockResults.TryGetValue(current.Id, out br);

                var lastInstr = current.Instructions[current.Instructions.Count - 1];

                // Check for while loop: back edge exists from some block to current
                if (IsWhileHeader(current, exitBlock))
                {
                    stmts.AddRange(EmitWhile(current, exitBlock));
                    // Find the exit of the while (jz target)
                    current = GetWhileExit(current);
                    continue;
                }

                // Check for if/else: block ends with jz
                if (lastInstr.Opcode == XvmOpcode.Jz && current.Successors.Count == 2)
                {
                    stmts.AddRange(EmitIfElse(current, exitBlock));
                    // Find the merge point after if/else
                    var merge = FindMergePoint(current, exitBlock);
                    current = merge;
                    continue;
                }

                // Check if this block jumps to a while exit (break)
                if (lastInstr.Opcode == XvmOpcode.Jmp && IsBreakTarget(lastInstr.JumpTarget))
                {
                    if (br != null)
                        stmts.AddRange(br.Statements);
                    stmts.Add(new BreakStmt());
                    return stmts;
                }

                // Linear block
                if (br != null)
                {
                    stmts.AddRange(br.Statements);
                    if (br.HasReturn)
                    {
                        stmts.Add(new ReturnStmt(br.ReturnValue));
                        return stmts;
                    }
                }

                // Follow the single successor
                if (current.Successors.Count == 1)
                {
                    current = current.Successors[0];
                }
                else
                {
                    break;
                }
            }

            return stmts;
        }

        /// <summary>
        /// Checks if a jump target is the exit of a containing while loop.
        /// </summary>
        private bool IsBreakTarget(int jumpTarget)
        {
            foreach (var exitBlock in _whileExitStack)
            {
                if (exitBlock != null && exitBlock.StartIndex == jumpTarget)
                    return true;
            }
            return false;
        }

        #region While Detection

        private bool IsWhileHeader(BasicBlock block, BasicBlock exitBlock)
        {
            // A while header has:
            // 1. A jz instruction (condition check)
            // 2. A back edge: some descendant jumps back to this block
            var lastInstr = block.Instructions[block.Instructions.Count - 1];
            if (lastInstr.Opcode != XvmOpcode.Jz)
                return false;

            // Check for back edge: is there any predecessor that comes after this block?
            return block.Predecessors.Any(p =>
                p.StartIndex > block.StartIndex &&
                HasPathWithoutPassing(p, block, block));
        }

        private bool HasPathWithoutPassing(BasicBlock from, BasicBlock target, BasicBlock header)
        {
            // Simple check: does from have a jmp to target?
            var lastInstr = from.Instructions[from.Instructions.Count - 1];
            if (lastInstr.Opcode == XvmOpcode.Jmp && lastInstr.JumpTarget == target.StartIndex)
                return true;
            return false;
        }

        private List<Stmt> EmitWhile(BasicBlock header, BasicBlock outerExit)
        {
            var stmts = new List<Stmt>();
            BlockResult headerResult;
            _blockResults.TryGetValue(header.Id, out headerResult);

            // Emit any statements before the condition in the header
            if (headerResult != null)
                stmts.AddRange(headerResult.Statements);

            var condition = headerResult?.BranchCondition ?? new BoolLiteral(true);

            // The jz target is the exit (after the while)
            var jzTarget = header.Successors.Count >= 2 ? header.Successors[1] : null;
            // The fall-through is the body start
            var bodyStart = header.Successors.Count >= 1 ? header.Successors[0] : null;

            List<Stmt> body;
            if (bodyStart != null && bodyStart != jzTarget)
            {
                // Push while exit for break detection, then process body
                _whileExitStack.Push(jzTarget);
                body = ProcessRegion(bodyStart, jzTarget);
                _whileExitStack.Pop();
            }
            else
            {
                body = new List<Stmt>();
            }

            stmts.Add(new WhileStmt(condition, body));
            return stmts;
        }

        private BasicBlock GetWhileExit(BasicBlock header)
        {
            // The jz target of the while header
            if (header.Successors.Count >= 2)
                return header.Successors[1];
            return null;
        }

        #endregion

        #region If/Else Detection

        private List<Stmt> EmitIfElse(BasicBlock condBlock, BasicBlock outerExit)
        {
            var stmts = new List<Stmt>();
            BlockResult condResult;
            _blockResults.TryGetValue(condBlock.Id, out condResult);

            // Emit any statements before the branch
            if (condResult != null)
                stmts.AddRange(condResult.Statements);

            var condition = condResult?.BranchCondition ?? new BoolLiteral(true);

            // Successors: [0] = fall-through (then), [1] = jz target (else/merge)
            var thenStart = condBlock.Successors[0];
            var jzTarget = condBlock.Successors[1];

            // Find merge point
            var merge = FindMergePoint(condBlock, outerExit);

            if (merge == jzTarget)
            {
                // Simple if-then (no else)
                var thenBody = ProcessRegion(thenStart, merge);
                stmts.Add(new IfStmt(condition, thenBody, null));
            }
            else
            {
                // If-then-else
                var thenBody = ProcessRegion(thenStart, merge);
                var elseBody = ProcessRegion(jzTarget, merge);
                stmts.Add(new IfStmt(condition, thenBody, elseBody));
            }

            return stmts;
        }

        /// <summary>
        /// Find the merge point after an if/else construct.
        /// The merge point is where both branches converge.
        /// </summary>
        private BasicBlock FindMergePoint(BasicBlock condBlock, BasicBlock exitBlock)
        {
            if (condBlock.Successors.Count < 2)
                return null;

            var thenStart = condBlock.Successors[0];
            var jzTarget = condBlock.Successors[1];

            // Strategy 1: If then-branch ends with jmp, that jmp target is the merge
            var thenEnd = FindLinearEnd(thenStart, jzTarget);
            if (thenEnd != null)
            {
                var thenLast = thenEnd.Instructions[thenEnd.Instructions.Count - 1];
                if (thenLast.Opcode == XvmOpcode.Jmp)
                {
                    var jmpTarget = thenLast.JumpTarget;

                    // If the jmp target is a while exit (break), don't use it as merge.
                    // The merge should be the jzTarget instead.
                    if (IsBreakTarget(jmpTarget))
                    {
                        return jzTarget;
                    }

                    // The jmp target is the merge point
                    foreach (var succ in thenEnd.Successors)
                    {
                        if (succ.StartIndex == jmpTarget)
                            return succ;
                    }
                }
            }

            // Strategy 2: jz target is the merge (simple if-then, no else)
            return jzTarget;
        }

        /// <summary>
        /// Follow linear (non-branching) path from start, stopping before stopBlock.
        /// Returns the last block in the linear chain.
        /// </summary>
        private BasicBlock FindLinearEnd(BasicBlock start, BasicBlock stopBlock)
        {
            var current = start;
            var visited = new HashSet<int>();

            while (current != null && current != stopBlock && visited.Add(current.Id))
            {
                var lastInstr = current.Instructions[current.Instructions.Count - 1];

                // If this block ends with jmp, ret, or jz — it's the end
                if (lastInstr.Opcode == XvmOpcode.Jmp ||
                    lastInstr.Opcode == XvmOpcode.Ret)
                    return current;

                if (lastInstr.Opcode == XvmOpcode.Jz)
                {
                    // This is a nested if — follow it to its merge, then continue
                    var merge = FindMergePoint(current, stopBlock);
                    if (merge == null || merge == stopBlock)
                        return current;
                    current = merge;
                    continue;
                }

                if (current.Successors.Count == 1)
                {
                    // Don't advance past stopBlock — return current block as the end
                    if (current.Successors[0] == stopBlock)
                        return current;
                    current = current.Successors[0];
                }
                else
                    break;
            }

            return current;
        }

        #endregion
    }
}
