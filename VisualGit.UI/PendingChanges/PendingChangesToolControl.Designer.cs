namespace VisualGit.UI.PendingChanges
{
    partial class PendingChangesToolControl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PendingChangesToolControl));
            this.pendingChangesTabs = new System.Windows.Forms.ToolStrip();
            this.fileChangesButton = new System.Windows.Forms.ToolStripButton();
            this.panel1 = new System.Windows.Forms.Panel();
            this.pendingChangesTabs.SuspendLayout();
            this.SuspendLayout();
            // 
            // pendingChangesTabs
            // 
            resources.ApplyResources(this.pendingChangesTabs, "pendingChangesTabs");
            this.pendingChangesTabs.GripMargin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.pendingChangesTabs.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.pendingChangesTabs.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.pendingChangesTabs.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileChangesButton});
            this.pendingChangesTabs.Name = "pendingChangesTabs";
            this.pendingChangesTabs.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.pendingChangesTabs.TabStop = true;
            // 
            // fileChangesButton
            // 
            this.fileChangesButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            resources.ApplyResources(this.fileChangesButton, "fileChangesButton");
            this.fileChangesButton.Name = "fileChangesButton";
            this.fileChangesButton.Click += new System.EventHandler(this.fileChangesButton_Click);
            // 
            // panel1
            // 
            resources.ApplyResources(this.panel1, "panel1");
            this.panel1.Name = "panel1";
            // 
            // PendingChangesToolControl
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.pendingChangesTabs);
            this.Name = "PendingChangesToolControl";
            this.pendingChangesTabs.ResumeLayout(false);
            this.pendingChangesTabs.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip pendingChangesTabs;
        private System.Windows.Forms.ToolStripButton fileChangesButton;
        private System.Windows.Forms.Panel panel1;
    }
}
