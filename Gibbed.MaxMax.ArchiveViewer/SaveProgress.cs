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
using System.Threading;
using System.Windows.Forms;
using Gibbed.MadMax.FileFormats;
using Gibbed.IO;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using ICSharpCode.SharpZipLib.Zip.Compression;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Gibbed.MadMax.ArchiveViewer
{
	public partial class SaveProgress : Form
	{
		public SaveProgress()
		{
			InitializeComponent();
		}

		delegate void SetStatusDelegate(string status, int percent);
		private void SetStatus(string status, int percent)
		{
			if (progressBar.InvokeRequired || statusLabel.InvokeRequired)
			{
				var callback = new SetStatusDelegate(SetStatus);
				Invoke(callback, new object[] { status, percent });
				return;
			}

			statusLabel.Text = status;
			progressBar.Value = percent;
		}

		delegate void SaveDoneDelegate();
		private void SaveDone()
		{
			if (InvokeRequired)
			{
				var callback = new SaveDoneDelegate(SaveDone);
				Invoke(callback);
				return;
			}

			Close();
		}

		public void SaveAll(object oinfo)
		{
			var info = (SaveAllInformation)oinfo;
			var usedNames = new Dictionary<uint, string>();

		    IEnumerable<uint> saving = info.Saving ?? info.Table.Entries.Keys.AsEnumerable();

            SetStatus("", 0);

            int total = saving.Count();
            int current = 0;

            var buffer = new byte[0x4000];
			foreach (var nameHash in saving)
			{
                current++;

                ArchiveTableFile.EntryInfo entry;
                if (!info.Table.Entries.TryGetValue(nameHash, out entry))
                    continue;

                string name = info.FileNames[nameHash];
                if (name == null)
                {
                    if (info.Settings.SaveOnlyKnownFiles)
                    {
                        SetStatus("Skipping...", (int)((current / (float)total) * 100.0f));
                        continue;
                    }

                    var guess = new byte[32];
                    int read = 0;

                    if (info.Table.EntryChunks.ContainsKey(nameHash) == false)
                    {
                        info.Archive.Seek(entry.Offset, SeekOrigin.Begin);

                        if (entry.CompressedSize == entry.UncompressedSize)
                        {
                            read = info.Archive.Read(guess, 0, (int)Math.Min(guess.Length, entry.UncompressedSize));
                        }
                        else
                        {
                            var zlib = new InflaterInputStream(info.Archive, new Inflater(true));
                            read = zlib.Read(guess, 0, (int)Math.Min(guess.Length, entry.UncompressedSize));
                        }
                    }
                    else
                    {
                        var chunks = info.Table.EntryChunks[nameHash];
                        if (chunks.Count > 0)
                        {
                            var chunk = chunks[0];
                            var nextUncompressedOffset =
                                1 < chunks.Count
                                    ? chunks[1].UncompressedOffset
                                    : entry.UncompressedSize;
                            var uncompressedSize = nextUncompressedOffset - chunk.UncompressedOffset;
                            info.Archive.Seek(entry.Offset + chunk.CompressedOffset, SeekOrigin.Begin);
                            var zlib = new InflaterInputStream(info.Archive, new Inflater(true));
                            read = zlib.Read(guess, 0, (int)Math.Min(guess.Length, uncompressedSize));
                        }
                    }

                    var extension = FileDetection.Detect(guess, read);
                    name = nameHash.ToString("X8");
                    name = Path.ChangeExtension(name, "." + extension);
                    name = Path.Combine("__UNKNOWN", extension, name);
                }
                else
                {
                    if (info.DirectoryList.ContainsKey(name) == false)
                    {
                        name = Path.Combine("__UNSORTED", name);
                    }
                    else
                    {
                        name = info.DirectoryList[name].First();
                    }

                    if (name.StartsWith("/") == true)
                    {
                        name = name.Substring(1);
                    }
                    name = name.Replace('/', Path.DirectorySeparatorChar);
                }

                string entryPath = Path.Combine(info.BasePath, name);
                if (File.Exists(entryPath) == true && info.Settings.DontOverwriteFiles == true)
                {
                    SetStatus("Skipping...", (int)((current / (float)total) * 100.0f));
                    continue;
                }

                SetStatus(name, (int)((current / (float)total) * 100.0f));

                var entryDirectory = Path.GetDirectoryName(entryPath);
                if (entryDirectory != null)
                {
                    Directory.CreateDirectory(entryDirectory);
                }

                using (var output = File.Create(entryPath))
                {
                    if (info.Table.EntryChunks.ContainsKey(nameHash) == false)
                    {
                        info.Archive.Seek(entry.Offset, SeekOrigin.Begin);

                        if (entry.CompressedSize == entry.UncompressedSize)
                        {
                            output.WriteFromStream(info.Archive, entry.UncompressedSize);
                        }
                        else
                        {
                            var zlib = new InflaterInputStream(info.Archive, new Inflater(true));
                            output.WriteFromStream(zlib, entry.UncompressedSize);
                        }
                    }
                    else
                    {
                        info.Archive.Seek(entry.Offset, SeekOrigin.Begin);
                        using (var temp = info.Archive.ReadToMemoryStream((int)entry.CompressedSize))
                        {
                            var chunks = info.Table.EntryChunks[nameHash];
                            for (int i = 0; i < chunks.Count; i++)
                            {
                                var chunk = chunks[i];
                                var nextUncompressedOffset =
                                    i + 1 < chunks.Count
                                        ? chunks[i + 1].UncompressedOffset
                                        : entry.UncompressedSize;
                                var uncompressedSize = nextUncompressedOffset - chunk.UncompressedOffset;

                                //info.Archive.Seek(entry.Offset + chunk.CompressedOffset, SeekOrigin.Begin);
                                temp.Seek(chunk.CompressedOffset, SeekOrigin.Begin);
                                output.Seek(chunk.UncompressedOffset, SeekOrigin.Begin);

                                var zlib = new InflaterInputStream(temp, new Inflater(true));
                                output.WriteFromStream(zlib, uncompressedSize);
                            }
                        }
                    }
                }
			}

			SaveDone();
		}

        public struct SaveAllSettings
        {
            public bool SaveOnlyKnownFiles;
            public bool DontOverwriteFiles;
        }

		private struct SaveAllInformation
		{
			public string BasePath;
			public Stream Archive;
			public ArchiveTableFile Table;
            public List<uint> Saving;
            public ProjectData.HashList<uint> FileNames;
            public Dictionary<string, List<string>> DirectoryList;
            public SaveAllSettings Settings;
		}

		private Thread _SaveThread;
		public void ShowSaveProgress(
            IWin32Window owner,
            Stream archive,
            ArchiveTableFile table,
            List<uint> saving,
            ProjectData.HashList<uint> fileNames,
            Dictionary<string, List<string>> directoryList,
            string basePath,
            SaveAllSettings settings)
		{
			SaveAllInformation info;
			info.BasePath = basePath;
			info.Archive = archive;
			info.Table = table;
            info.Saving = saving;
			info.FileNames = fileNames;
            info.DirectoryList = directoryList;
            info.Settings = settings;

			progressBar.Value = 0;
			progressBar.Maximum = 100;

			_SaveThread = new Thread(SaveAll);
			_SaveThread.Start(info);
			ShowDialog(owner);
		}

		private void OnCancel(object sender, EventArgs e)
		{
			if (_SaveThread != null)
			{
				_SaveThread.Abort();
			}

			Close();
		}
	}
}
