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

namespace Gibbed.MadMax.XvmDecompile
{
    public class BasicBlock
    {
        public int Id;
        public int StartIndex;
        public int EndIndex; // inclusive
        public List<DecodedInstruction> Instructions = new List<DecodedInstruction>();
        public List<BasicBlock> Successors = new List<BasicBlock>();
        public List<BasicBlock> Predecessors = new List<BasicBlock>();

        // Dominator tree
        public BasicBlock ImmediateDominator;
        public List<BasicBlock> DominatorChildren = new List<BasicBlock>();
        public int ReversePostOrderIndex = -1;
    }

    public static class CfgBuilder
    {
        public static List<BasicBlock> Build(DecodedInstruction[] instructions)
        {
            if (instructions.Length == 0)
                return new List<BasicBlock>();

            // Step 1: Find block leaders (start of each basic block)
            var leaders = new HashSet<int> { 0 };

            for (int i = 0; i < instructions.Length; i++)
            {
                var opcode = instructions[i].Opcode;
                if (opcode == XvmOpcode.Jmp || opcode == XvmOpcode.Jz)
                {
                    var target = instructions[i].JumpTarget;
                    leaders.Add(target);
                    if (i + 1 < instructions.Length)
                        leaders.Add(i + 1);
                }
                else if (opcode == XvmOpcode.Ret)
                {
                    if (i + 1 < instructions.Length)
                        leaders.Add(i + 1);
                }
            }

            var sortedLeaders = leaders.Where(l => l >= 0 && l < instructions.Length)
                                       .OrderBy(l => l).ToList();

            // Step 2: Create basic blocks
            var blocks = new List<BasicBlock>();
            var indexToBlock = new Dictionary<int, BasicBlock>();

            for (int i = 0; i < sortedLeaders.Count; i++)
            {
                var start = sortedLeaders[i];
                var end = (i + 1 < sortedLeaders.Count)
                    ? sortedLeaders[i + 1] - 1
                    : instructions.Length - 1;

                var block = new BasicBlock();
                block.Id = blocks.Count;
                block.StartIndex = start;
                block.EndIndex = end;

                for (int j = start; j <= end; j++)
                    block.Instructions.Add(instructions[j]);

                blocks.Add(block);
                indexToBlock[start] = block;
            }

            // Step 3: Connect edges
            foreach (var block in blocks)
            {
                var lastInstr = block.Instructions[block.Instructions.Count - 1];
                var lastOpcode = lastInstr.Opcode;

                if (lastOpcode == XvmOpcode.Jmp)
                {
                    BasicBlock target;
                    if (indexToBlock.TryGetValue(lastInstr.JumpTarget, out target))
                    {
                        block.Successors.Add(target);
                        target.Predecessors.Add(block);
                    }
                }
                else if (lastOpcode == XvmOpcode.Jz)
                {
                    // Fall-through (condition is true / not zero)
                    var fallThrough = block.EndIndex + 1;
                    BasicBlock ftBlock;
                    if (indexToBlock.TryGetValue(fallThrough, out ftBlock))
                    {
                        block.Successors.Add(ftBlock); // index 0 = fall-through
                        ftBlock.Predecessors.Add(block);
                    }

                    // Jump target (condition is false / zero)
                    BasicBlock jzTarget;
                    if (indexToBlock.TryGetValue(lastInstr.JumpTarget, out jzTarget))
                    {
                        block.Successors.Add(jzTarget); // index 1 = jz target
                        jzTarget.Predecessors.Add(block);
                    }
                }
                else if (lastOpcode == XvmOpcode.Ret)
                {
                    // No successors
                }
                else
                {
                    // Fall-through
                    var fallThrough = block.EndIndex + 1;
                    BasicBlock ftBlock;
                    if (indexToBlock.TryGetValue(fallThrough, out ftBlock))
                    {
                        block.Successors.Add(ftBlock);
                        ftBlock.Predecessors.Add(block);
                    }
                }
            }

            // Step 4: Compute dominator tree
            ComputeDominators(blocks);

            return blocks;
        }

        private static void ComputeDominators(List<BasicBlock> blocks)
        {
            if (blocks.Count == 0) return;

            // Compute reverse postorder
            var visited = new HashSet<int>();
            var rpo = new List<BasicBlock>();
            DfsPostOrder(blocks[0], visited, rpo);
            rpo.Reverse();

            for (int i = 0; i < rpo.Count; i++)
                rpo[i].ReversePostOrderIndex = i;

            // Cooper-Harvey-Kennedy iterative dominator algorithm
            var entry = rpo[0];
            entry.ImmediateDominator = entry;

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 1; i < rpo.Count; i++)
                {
                    var b = rpo[i];
                    BasicBlock newIdom = null;

                    foreach (var pred in b.Predecessors)
                    {
                        if (pred.ImmediateDominator == null)
                            continue;
                        if (newIdom == null)
                        {
                            newIdom = pred;
                        }
                        else
                        {
                            newIdom = Intersect(pred, newIdom);
                        }
                    }

                    if (newIdom != null && b.ImmediateDominator != newIdom)
                    {
                        b.ImmediateDominator = newIdom;
                        changed = true;
                    }
                }
            }

            // Build dominator tree children
            foreach (var b in blocks)
            {
                if (b.ImmediateDominator != null && b.ImmediateDominator != b)
                {
                    b.ImmediateDominator.DominatorChildren.Add(b);
                }
            }
        }

        private static BasicBlock Intersect(BasicBlock b1, BasicBlock b2)
        {
            var finger1 = b1;
            var finger2 = b2;
            while (finger1 != finger2)
            {
                while (finger1.ReversePostOrderIndex > finger2.ReversePostOrderIndex)
                    finger1 = finger1.ImmediateDominator;
                while (finger2.ReversePostOrderIndex > finger1.ReversePostOrderIndex)
                    finger2 = finger2.ImmediateDominator;
            }
            return finger1;
        }

        private static void DfsPostOrder(BasicBlock block, HashSet<int> visited, List<BasicBlock> result)
        {
            if (!visited.Add(block.Id))
                return;
            foreach (var succ in block.Successors)
                DfsPostOrder(succ, visited, result);
            result.Add(block);
        }

        /// <summary>
        /// Checks if block a dominates block b.
        /// </summary>
        public static bool Dominates(BasicBlock a, BasicBlock b)
        {
            var cursor = b;
            while (cursor != null)
            {
                if (cursor == a) return true;
                if (cursor.ImmediateDominator == cursor) break;
                cursor = cursor.ImmediateDominator;
            }
            return false;
        }
    }
}
