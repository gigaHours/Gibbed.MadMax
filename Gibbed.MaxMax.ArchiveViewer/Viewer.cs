/* Copyright (c) 2012 Rick (rick 'at' gibbed 'dot' us)
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
using System.Linq;
using System.Windows.Forms;
using Gibbed.MadMax.FileFormats;

namespace Gibbed.MadMax.ArchiveViewer
{
    public partial class Viewer : Form
    {
        public Viewer()
        {
            InitializeComponent();
        }

        private ProjectData.Manager _Manager;
        private ProjectData.HashList<uint> _Hashes;
        private Dictionary<string, List<string>> _DirectoryList;

        private void OnLoad(object sender, EventArgs e)
        {
            LoadProject();
        }

        private void LoadProject()
        {
            try
            {
                _Manager = ProjectData.Manager.Load();
                projectComboBox.Items.AddRange(_Manager
                                                        .Cast<object>()
                                                        .ToArray());
                SetProject(_Manager.ActiveProject);
            }
            catch (Exception e)
            {
                MessageBox.Show(
                    "There was an error while loading project data." +
                    Environment.NewLine + Environment.NewLine +
                    e +
                    Environment.NewLine + Environment.NewLine +
                    "(You can press Ctrl+C to copy the contents of this dialog)",
                    "Critical Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Close();
            }
        }

        private void SetProject(ProjectData.Project project)
        {
            if (project != null)
            {
                try
                {
                    openDialog.InitialDirectory = project.InstallPath;
                    saveKnownFileListDialog.InitialDirectory = project.ListsPath;
                }
                catch (Exception e)
                {
                    MessageBox.Show(
                        "There was an error while loading project data." +
                        Environment.NewLine + Environment.NewLine +
                        e +
                        Environment.NewLine + Environment.NewLine +
                        "(You can press Ctrl+C to copy the contents of this dialog)",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    project = null;
                }

                _Hashes = project.LoadFileLists(null);
                _DirectoryList = project.LoadDirectoryList();
            }

            // ReSharper disable RedundantCheckBeforeAssignment
            if (project != _Manager.ActiveProject) // ReSharper restore RedundantCheckBeforeAssignment
            {
                _Manager.ActiveProject = project;
            }

            projectComboBox.SelectedItem = project;
        }

        private ArchiveTableFile _Table;

        private void BuildFileTree()
        {
            fileList.Nodes.Clear();
            fileList.BeginUpdate();

            if (_Table != null)
            {
                var dirNodes = new Dictionary<string, TreeNode>();

                var baseNode = new TreeNode(Path.GetFileName(openDialog.FileName), 0, 0);
                var knownNode = new TreeNode("Known", 1, 1);
                var unknownNode = new TreeNode("Unknown", 1, 1);

                foreach (uint hash in _Table.Entries.Keys
                    .OrderBy(k => k, new FileNameHashComparer(_Hashes)))
                {
                    ArchiveTableFile.EntryInfo entry = _Table.Entries[hash];
                    TreeNode node;

                    if (_Hashes != null && _Hashes.Contains(hash) == true)
                    {
                        string fileName = _Hashes[hash];
                        string pathName = Path.GetDirectoryName(fileName);
                        TreeNodeCollection parentNodes = knownNode.Nodes;

                        if (string.IsNullOrEmpty(pathName) == false)
                        {
                            string[] dirs = pathName.Split(new[]
                            {
                                '\\'
                            });

                            foreach (string dir in dirs)
                            {
                                if (parentNodes.ContainsKey(dir))
                                {
                                    parentNodes = parentNodes[dir].Nodes;
                                }
                                else
                                {
                                    TreeNode parentNode = parentNodes.Add(dir, dir, 2, 2);
                                    parentNodes = parentNode.Nodes;
                                }
                            }
                        }

                        node = parentNodes.Add(null, Path.GetFileName(fileName), 3, 3);
                    }
                    else
                    {
                        node = unknownNode.Nodes.Add(null, hash.ToString("X8"), 3, 3);
                    }

                    node.Tag = hash;
                }

                if (knownNode.Nodes.Count > 0)
                {
                    baseNode.Nodes.Add(knownNode);
                }

                if (unknownNode.Nodes.Count > 0)
                {
                    baseNode.Nodes.Add(unknownNode);
                    unknownNode.Text = "Unknown (" +
                                       unknownNode.Nodes.Count.ToString(
                                           System.Globalization.CultureInfo.InvariantCulture) + ")";
                }

                if (knownNode.Nodes.Count > 0)
                {
                    knownNode.Expand();
                }
                else if (unknownNode.Nodes.Count > 0)
                {
                    //unknownNode.Expand();
                }

                baseNode.Expand();
                fileList.Nodes.Add(baseNode);
            }

            //fileList.Sort();
            fileList.EndUpdate();
        }

        private void OnOpen(object sender, EventArgs e)
        {
            if (openDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            // ReSharper disable RedundantCheckBeforeAssignment
            if (openDialog.InitialDirectory != null) // ReSharper restore RedundantCheckBeforeAssignment
            {
                openDialog.InitialDirectory = null;
            }

            using (var input = openDialog.OpenFile())
            {
                var table = new ArchiveTableFile();
                table.Deserialize(input);
                _Table = table;
            }

            /*
            TextWriter writer = new StreamWriter("all_file_hashes.txt");
            foreach (var hash in table.Keys.OrderBy(k => k))
            {
                writer.WriteLine(hash.ToString("X8"));
            }
            writer.Close();
            */

            BuildFileTree();
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (fileList.SelectedNode == null)
            {
                return;
            }

            string basePath;
            List<uint> saving;

            SaveProgress.SaveAllSettings settings;
            settings.SaveOnlyKnownFiles = false;
            settings.DontOverwriteFiles = dontOverwriteFilesMenuItem.Checked;

            var root = fileList.SelectedNode;
            if (root.Nodes.Count == 0)
            {
                saveFileDialog.FileName = root.Text;

                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                saving = new List<uint>()
                {
                    (uint)root.Tag,
                };

                // ReSharper disable UseObjectOrCollectionInitializer
                var lookup = new Dictionary<uint, string>();
                // ReSharper restore UseObjectOrCollectionInitializer
                lookup.Add((uint)root.Tag, Path.GetFileName(saveFileDialog.FileName));
                basePath = Path.GetDirectoryName(saveFileDialog.FileName);

                settings.DontOverwriteFiles = false;
            }
            else
            {
                if (saveFilesDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                saving = new List<uint>();

                var nodes = new List<TreeNode>()
                {
                    root,
                };

                while (nodes.Count > 0)
                {
                    var node = nodes[0];
                    nodes.RemoveAt(0);

                    if (node.Nodes.Count > 0)
                    {
                        foreach (TreeNode child in node.Nodes)
                        {
                            if (child.Nodes.Count > 0)
                            {
                                nodes.Add(child);
                            }
                            else
                            {
                                saving.Add((uint)child.Tag);
                            }
                        }
                    }
                }

                basePath = saveFilesDialog.SelectedPath;
            }

            var input = File.OpenRead(Path.ChangeExtension(openDialog.FileName, ".arc"));

            var progress = new SaveProgress();
            progress.ShowSaveProgress(
                this,
                input,
                _Table,
                saving,
                _Hashes,
                _DirectoryList,
                basePath,
                settings);

            input.Close();
        }

        private void OnSaveAll(object sender, EventArgs e)
        {
            if (saveFilesDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var input = File.OpenRead(Path.ChangeExtension(openDialog.FileName, ".arc"));

            string basePath = saveFilesDialog.SelectedPath;

            SaveProgress.SaveAllSettings settings;
            settings.SaveOnlyKnownFiles = saveOnlyKnownFilesMenuItem.Checked;
            settings.DontOverwriteFiles = dontOverwriteFilesMenuItem.Checked;

            var progress = new SaveProgress();
            progress.ShowSaveProgress(
                this,
                input,
                _Table,
                null,
                _Hashes,
                _DirectoryList,
                basePath,
                settings);

            input.Close();
        }

        private void OnReloadLists(object sender, EventArgs e)
        {
            if (_Manager.ActiveProject != null)
            {
                _Hashes = _Manager.ActiveProject.LoadFileLists(null);
                _DirectoryList = _Manager.ActiveProject.LoadDirectoryList();
            }
            else
            {
                _Hashes = null;
                _DirectoryList = null;
            }

            BuildFileTree();
        }

        private void OnProjectSelected(object sender, EventArgs e)
        {
            projectComboBox.Invalidate();
            var project = projectComboBox.SelectedItem as ProjectData.Project;
            if (project == null)
            {
                projectComboBox.Items.Remove(projectComboBox.SelectedItem);
            }
            SetProject(project);
            BuildFileTree();
        }

        private void OnSaveKnownFileList(object sender, EventArgs e)
        {
            if (saveKnownFileListDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var names = new List<string>();

            if (_Table != null &&
                _Manager.ActiveProject != null)
            {
                names.AddRange(from hash in _Table.Entries.Keys
                               where _Hashes.Contains(hash) == true
                               select _Hashes[hash]);
            }

            names.Sort();

            TextWriter output = new StreamWriter(saveKnownFileListDialog.OpenFile());
            foreach (string name in names)
            {
                output.WriteLine(name);
            }
            output.Close();
        }
    }
}
