namespace Gibbed.MadMax.ArchiveViewer
{
    partial class Viewer
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Viewer));
            this.mainToolStrip = new System.Windows.Forms.ToolStrip();
            this.projectComboBox = new System.Windows.Forms.ToolStripComboBox();
            this.openButton = new System.Windows.Forms.ToolStripButton();
            this.saveAllButton = new System.Windows.Forms.ToolStripButton();
            this.reloadListsButton = new System.Windows.Forms.ToolStripSplitButton();
            this.saveKnownListToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsButton = new System.Windows.Forms.ToolStripDropDownButton();
            this.saveOnlyKnownFilesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.dontOverwriteFilesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fileList = new System.Windows.Forms.TreeView();
            this.fileMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.saveToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openDialog = new System.Windows.Forms.OpenFileDialog();
            this.saveFilesDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.saveFileDialog = new System.Windows.Forms.SaveFileDialog();
            this.saveKnownFileListDialog = new System.Windows.Forms.SaveFileDialog();
            this.mainToolStrip.SuspendLayout();
            this.fileMenuStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // mainToolStrip
            // 
            this.mainToolStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.mainToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.projectComboBox,
            this.openButton,
            this.saveAllButton,
            this.reloadListsButton,
            this.settingsButton});
            this.mainToolStrip.Location = new System.Drawing.Point(0, 0);
            this.mainToolStrip.Name = "mainToolStrip";
            this.mainToolStrip.Size = new System.Drawing.Size(853, 28);
            this.mainToolStrip.TabIndex = 0;
            this.mainToolStrip.Text = "toolStrip1";
            // 
            // projectComboBox
            // 
            this.projectComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.projectComboBox.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.projectComboBox.Name = "projectComboBox";
            this.projectComboBox.Size = new System.Drawing.Size(160, 28);
            this.projectComboBox.Sorted = true;
            this.projectComboBox.SelectedIndexChanged += new System.EventHandler(this.OnProjectSelected);
            // 
            // openButton
            // 
            this.openButton.Image = global::Gibbed.MadMax.ArchiveViewer.Properties.Resources.OpenArchive;
            this.openButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.openButton.Name = "openButton";
            this.openButton.Size = new System.Drawing.Size(69, 25);
            this.openButton.Text = "&Open";
            this.openButton.Click += new System.EventHandler(this.OnOpen);
            // 
            // saveAllButton
            // 
            this.saveAllButton.Image = global::Gibbed.MadMax.ArchiveViewer.Properties.Resources.SaveAllFiles;
            this.saveAllButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.saveAllButton.Name = "saveAllButton";
            this.saveAllButton.Size = new System.Drawing.Size(86, 25);
            this.saveAllButton.Text = "Save &All";
            this.saveAllButton.Click += new System.EventHandler(this.OnSaveAll);
            // 
            // reloadListsButton
            // 
            this.reloadListsButton.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveKnownListToolStripMenuItem});
            this.reloadListsButton.Image = global::Gibbed.MadMax.ArchiveViewer.Properties.Resources.ReloadLists;
            this.reloadListsButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.reloadListsButton.Name = "reloadListsButton";
            this.reloadListsButton.Size = new System.Drawing.Size(127, 25);
            this.reloadListsButton.Text = "&Reload Lists";
            this.reloadListsButton.ButtonClick += new System.EventHandler(this.OnReloadLists);
            // 
            // saveKnownListToolStripMenuItem
            // 
            this.saveKnownListToolStripMenuItem.Name = "saveKnownListToolStripMenuItem";
            this.saveKnownListToolStripMenuItem.Size = new System.Drawing.Size(198, 26);
            this.saveKnownListToolStripMenuItem.Text = "Save Known &List";
            this.saveKnownListToolStripMenuItem.Click += new System.EventHandler(this.OnSaveKnownFileList);
            // 
            // settingsButton
            // 
            this.settingsButton.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveOnlyKnownFilesMenuItem,
            this.dontOverwriteFilesMenuItem});
            this.settingsButton.Image = global::Gibbed.MadMax.ArchiveViewer.Properties.Resources.Settings;
            this.settingsButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.settingsButton.Name = "settingsButton";
            this.settingsButton.Size = new System.Drawing.Size(96, 25);
            this.settingsButton.Text = "Settings";
            // 
            // saveOnlyKnownFilesMenuItem
            // 
            this.saveOnlyKnownFilesMenuItem.CheckOnClick = true;
            this.saveOnlyKnownFilesMenuItem.Name = "saveOnlyKnownFilesMenuItem";
            this.saveOnlyKnownFilesMenuItem.Size = new System.Drawing.Size(233, 26);
            this.saveOnlyKnownFilesMenuItem.Text = "Save only &known files";
            // 
            // dontOverwriteFilesMenuItem
            // 
            this.dontOverwriteFilesMenuItem.CheckOnClick = true;
            this.dontOverwriteFilesMenuItem.Name = "dontOverwriteFilesMenuItem";
            this.dontOverwriteFilesMenuItem.Size = new System.Drawing.Size(233, 26);
            this.dontOverwriteFilesMenuItem.Text = "Don\'t &overwrite files";
            // 
            // fileList
            // 
            this.fileList.ContextMenuStrip = this.fileMenuStrip;
            this.fileList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.fileList.Location = new System.Drawing.Point(0, 28);
            this.fileList.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.fileList.Name = "fileList";
            this.fileList.Size = new System.Drawing.Size(853, 366);
            this.fileList.TabIndex = 1;
            // 
            // fileMenuStrip
            // 
            this.fileMenuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.fileMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveToolStripMenuItem});
            this.fileMenuStrip.Name = "fileMenuStrip";
            this.fileMenuStrip.Size = new System.Drawing.Size(110, 28);
            // 
            // saveToolStripMenuItem
            // 
            this.saveToolStripMenuItem.Name = "saveToolStripMenuItem";
            this.saveToolStripMenuItem.Size = new System.Drawing.Size(109, 24);
            this.saveToolStripMenuItem.Text = "&Save";
            this.saveToolStripMenuItem.Click += new System.EventHandler(this.OnSave);
            // 
            // openDialog
            // 
            this.openDialog.Filter = "Mad Max Archives (*.tab)|*.tab|All Files (*.*)|*.*";
            // 
            // saveFilesDialog
            // 
            this.saveFilesDialog.Description = "Select a directory to save all files from the archive to.";
            // 
            // saveFileDialog
            // 
            this.saveFileDialog.Filter = "All Files (*.*)|*.*";
            // 
            // saveKnownFileListDialog
            // 
            this.saveKnownFileListDialog.DefaultExt = "filelist";
            this.saveKnownFileListDialog.Filter = "File List (*.filelist)|*.filelist";
            // 
            // Viewer
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(853, 394);
            this.Controls.Add(this.fileList);
            this.Controls.Add(this.mainToolStrip);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "Viewer";
            this.Text = "Gibbed\'s Mad Max Archive Viewer";
            this.Load += new System.EventHandler(this.OnLoad);
            this.mainToolStrip.ResumeLayout(false);
            this.mainToolStrip.PerformLayout();
            this.fileMenuStrip.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip mainToolStrip;
        private System.Windows.Forms.ToolStripButton openButton;
        private System.Windows.Forms.TreeView fileList;
        private System.Windows.Forms.OpenFileDialog openDialog;
        private System.Windows.Forms.FolderBrowserDialog saveFilesDialog;
        private System.Windows.Forms.ToolStripComboBox projectComboBox;
        private System.Windows.Forms.ContextMenuStrip fileMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem saveToolStripMenuItem;
        private System.Windows.Forms.SaveFileDialog saveFileDialog;
        private System.Windows.Forms.ToolStripButton saveAllButton;
        private System.Windows.Forms.ToolStripDropDownButton settingsButton;
        private System.Windows.Forms.ToolStripMenuItem saveOnlyKnownFilesMenuItem;
        private System.Windows.Forms.ToolStripMenuItem dontOverwriteFilesMenuItem;
        private System.Windows.Forms.ToolStripSplitButton reloadListsButton;
        private System.Windows.Forms.ToolStripMenuItem saveKnownListToolStripMenuItem;
        private System.Windows.Forms.SaveFileDialog saveKnownFileListDialog;
    }
}

