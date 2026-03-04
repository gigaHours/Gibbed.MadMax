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

namespace Gibbed.MadMax.ConvertAdf
{
    internal class RuntimeTypeLibrary
    {
        public readonly Dictionary<uint, FileFormats.AdfFile.TypeDefinition> TypeDefinitions;

        public RuntimeTypeLibrary()
        {
            this.TypeDefinitions = new Dictionary<uint, FileFormats.AdfFile.TypeDefinition>();
        }

        public FileFormats.AdfFile.TypeDefinition GetTypeDefinition(uint nameHash)
        {
            //Console.WriteLine(nameHash);
            if (this.TypeDefinitions.ContainsKey(nameHash) == false)
            {
                Console.WriteLine("GetTypeDefinition Unk {0:X}", nameHash);
                return new FileFormats.AdfFile.TypeDefinition();
            }
            //Console.WriteLine("GetTypeDefinition " + this.TypeDefinitions[nameHash].Name + " " + nameHash);
            return this.TypeDefinitions[nameHash];
        }

        public FileFormats.AdfFile.TypeDefinition GetTypeDefinitionByName(string name)
        {
            foreach (var kvp in this.TypeDefinitions)
            {
                if (kvp.Value.Name == name)
                {
                    return kvp.Value;
                }
            }
            throw new KeyNotFoundException("Type definition not found: " + name);
        }

        public void AddTypeDefinitions(FileFormats.AdfFile adf)
        {
            foreach (var typeDefinition in adf.TypeDefinitions)
            {
                if (this.TypeDefinitions.ContainsKey(typeDefinition.NameHash) == true)
                {
                    throw new InvalidOperationException();
                }

                this.TypeDefinitions.Add(typeDefinition.NameHash, typeDefinition);
            }
        }
    }
}
