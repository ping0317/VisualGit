using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;
using SharpGit;

namespace VisualGit.Scc.UI
{
    [Flags]
    public enum DiffMode
    {
        Default = 0,
        PreferExternal = 1,
        PreferInternal = 2
    }

    public abstract class VisualGitDiffToolArgs
    {
        DiffMode _diffMode;

        /// <summary>
        /// Gets or sets the mode.
        /// </summary>
        /// <value>The mode.</value>
        public DiffMode Mode
        {
            get { return _diffMode; }
            set { _diffMode = value; }
        }

        /// <summary>
        /// Validates this instance.
        /// </summary>
        /// <returns></returns>
        public abstract bool Validate();
    }

    public class VisualGitDiffArgs : VisualGitDiffToolArgs
    {
        string _baseFile;
        string _baseTitle;

        string _mineFile;
        string _mineTitle;
        bool _readOnly;

        /// <summary>
        /// Gets or sets the base file.
        /// </summary>
        /// <value>The base file.</value>
        public string BaseFile
        {
            get { return _baseFile; }
            set { _baseFile = value; }
        }

        /// <summary>
        /// Gets or sets the mine file.
        /// </summary>
        /// <value>The mine file.</value>
        public string MineFile
        {
            get { return _mineFile; }
            set { _mineFile = value; }
        }

        /// <summary>
        /// Gets or sets the base title.
        /// </summary>
        /// <value>The base title.</value>
        public string BaseTitle
        {
            get { return _baseTitle; }
            set { _baseTitle = value; }
        }

        /// <summary>
        /// Gets or sets the mine title.
        /// </summary>
        /// <value>The mine title.</value>
        public string MineTitle
        {
            get { return _mineTitle; }
            set { _mineTitle = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the diff/merge should be presented as read only.
        /// </summary>
        /// <value><c>true</c> if [read only]; otherwise, <c>false</c>.</value>
        public bool ReadOnly
        {
            get { return _readOnly; }
            set { _readOnly = value; }
        }

        /// <summary>
        /// Validates this instance.
        /// </summary>
        /// <returns></returns>
        public override bool Validate()
        {
            return !string.IsNullOrEmpty(BaseFile) && !string.IsNullOrEmpty(MineFile);
        }
    }

    public class VisualGitMergeArgs : VisualGitDiffArgs
    {
        string _theirsFile;
        string _theirsTitle;
        string _mergedFile;
        string _mergedTitle;
        ICollection<string> _cleanupFiles;

        /// <summary>
        /// Gets or sets the theirs file.
        /// </summary>
        /// <value>The theirs file.</value>
        public string TheirsFile
        {
            get { return _theirsFile; }
            set { _theirsFile = value; }
        }

        /// <summary>
        /// Gets or sets the theirs title.
        /// </summary>
        /// <value>The theirs title.</value>
        public string TheirsTitle
        {
            get { return _theirsTitle; }
            set { _theirsTitle = value; }
        }

        public string MergedFile
        {
            get { return _mergedFile; }
            set { _mergedFile = value; }
        }

        public string MergedTitle
        {
            get { return _mergedTitle; }
            set { _mergedTitle = value; }
        }

        public ICollection<string> CleanupFiles
        {
            get { return _cleanupFiles; }
            set { _cleanupFiles = value; }
        }

        public override bool Validate()
        {
            return base.Validate() && !string.IsNullOrEmpty(TheirsFile) && !string.IsNullOrEmpty(MergedFile);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class VisualGitPatchArgs : VisualGitDiffToolArgs
    {
        string _patchFile;
        string _applyTo;

        /// <summary>
        /// Gets or sets the patch file.
        /// </summary>
        /// <value>The patch file.</value>
        public string PatchFile
        {
            get { return _patchFile; }
            set { _patchFile = value; }
        }

        /// <summary>
        /// Gets or sets the apply to.
        /// </summary>
        /// <value>The apply to.</value>
        public string ApplyTo
        {
            get { return _applyTo; }
            set { _applyTo = value; }
        }

        /// <summary>
        /// Validates this instance.
        /// </summary>
        /// <returns></returns>
        public override bool Validate()
        {
            return !string.IsNullOrEmpty(PatchFile) && !string.IsNullOrEmpty(ApplyTo);
        }
    }

    /// <summary>
    /// A template in the dialog above.
    /// </summary>
    public class VisualGitDiffArgumentDefinition
    {
        readonly string _key;
        readonly string[] _aliases;
        readonly string _description;

        /// <summary>
        /// Initializes a new instance of the <see cref="VisualGitDiffArgumentDefinition"/> class.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="description">The description.</param>
        /// <param name="aliases">The aliases.</param>
        public VisualGitDiffArgumentDefinition(string key, string description, params string[] aliases)
        {
            _key = key;
            _description = description;
            _aliases = aliases ?? new string[0];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VisualGitDiffArgumentDefinition"/> class.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="description">The description.</param>
        public VisualGitDiffArgumentDefinition(string key, string description)
            : this(key, description, (string[])null)
        {
        }

        /// <summary>
        /// Gets the key.
        /// </summary>
        /// <value>The key.</value>
        public string Key
        {
            get { return _key; }
        }

        /// <summary>
        /// Gets the description.
        /// </summary>
        /// <value>The description.</value>
        public string Description
        {
            get { return _description; }
        }

        /// <summary>
        /// Gets the aliases.
        /// </summary>
        /// <value>The aliases.</value>
        public string[] Aliases
        {
            get { return _aliases; }
        }
    }



    [DebuggerDisplay("{Name} ({Title})")]
    public abstract class VisualGitDiffTool
    {
        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public abstract string Name
        {
            get;
        }

        /// <summary>
        /// Gets the title.
        /// </summary>
        /// <value>The title.</value>
        public abstract string Title
        {
            get;
        }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        /// <value>The display name.</value>
        public string DisplayName
        {
            get { return string.Format("{0}{1}", Title, IsAvailable ? "" : " (Not Found)"); }
        }

        /// <summary>
        /// Gets the tool template.
        /// </summary>
        /// <value>The tool template.</value>
        public string ToolTemplate
        {
            get { return string.Format("$(AppTemplate({0}))", Name); }
        }

        /// <summary>
        /// Gets the tool name from template.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        public static string GetToolNameFromTemplate(string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentNullException("value");

            if (value.StartsWith("$(AppTemplate(", StringComparison.OrdinalIgnoreCase) &&
                value.EndsWith("))"))
                return value.Substring(14, value.Length - 16);

            return null;
        }

        /// <summary>
        /// Gets a value indicating whether this instance is available.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is available; otherwise, <c>false</c>.
        /// </value>
        public abstract bool IsAvailable
        {
            get;
        }

        /// <summary>
        /// Gets the program.
        /// </summary>
        /// <value>The program.</value>
        public abstract string Program
        {
            get;
        }

        /// <summary>
        /// Gets the arguments.
        /// </summary>
        /// <value>The arguments.</value>
        public abstract string Arguments
        {
            get;
        }

        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return Title ?? base.ToString();
        }
    }

    public interface IVisualGitDiffHandler
    {
        /// <summary>
        /// Runs the diff as specified by the args
        /// </summary>
        /// <param name="args">The args.</param>
        /// <returns></returns>
        bool RunDiff(VisualGitDiffArgs args);
        /// <summary>
        /// Runs the merge as specified by the args
        /// </summary>
        /// <param name="args">The args.</param>
        /// <returns></returns>
        bool RunMerge(VisualGitMergeArgs args);
        /// <summary>
        /// Runs the patch as specified by the args
        /// </summary>
        /// <param name="args">The args.</param>
        bool RunPatch(VisualGitPatchArgs args);

        /// <summary>
        /// Releases the diff.
        /// </summary>
        /// <param name="frameNumber">The frame number.</param>
        void ReleaseDiff(int frameNumber);

        /// <summary>
        /// Gets the temp file.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <param name="revision">The revision.</param>
        /// <param name="withProgress">if set to <c>true</c> [with progress].</param>
        /// <returns></returns>
        string GetTempFile(GitTarget target, GitRevision revision, bool withProgress);
        /// <summary>
        /// Gets the temp file.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <param name="revision">The revision.</param>
        /// <param name="withProgress">if set to <c>true</c> [with progress].</param>
        /// <returns></returns>
        string GetTempFile(GitItem target, GitRevision revision, bool withProgress);
        string[] GetTempFiles(GitTarget target, GitRevision first, GitRevision last, bool withProgress);
        string GetTitle(GitTarget target, GitRevision revision);
        string GetTitle(GitItem target, GitRevision revision);

        /// <summary>
        /// Gets a list of diff tool templates.
        /// </summary>
        /// <returns></returns>
        IList<VisualGitDiffTool> DiffToolTemplates { get; }
        /// <summary>
        /// Gets a list of merge tool templates.
        /// </summary>
        /// <returns></returns>
        IList<VisualGitDiffTool> MergeToolTemplates { get; }
        /// <summary>
        /// Gets a list of patch tools.
        /// </summary>
        /// <returns></returns>
        IList<VisualGitDiffTool> PatchToolTemplates { get; }

        /// <summary>
        /// Gets the arguments.
        /// </summary>
        /// <value>The arguments.</value>
        IList<VisualGitDiffArgumentDefinition> ArgumentDefinitions { get; }

        /// <summary>
        /// Gets the copy origin.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns></returns>
        GitTarget GetCopyOrigin(GitItem item);
    }
}
