using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.IO;
using VisualGit.Scc;
using VisualGit.UI;
using VisualGit.Services.PendingChanges;
using VisualGit.VS;
using VisualGit.UI.SccManagement;
using SharpGit;

namespace VisualGit.Services.PendingChanges
{
    /// <summary>
    /// 
    /// </summary>
    [GlobalService(typeof(IPendingChangeHandler))]
    partial class PendingChangeHandler : VisualGitService, IPendingChangeHandler
    {
        public PendingChangeHandler(IVisualGitServiceProvider context)
            : base(context)
        {
        }

        public bool ApplyChanges(IEnumerable<PendingChange> changes, PendingChangeApplyArgs args)
        {
            using (PendingCommitState state = new PendingCommitState(Context, changes))
            {
                if (!PreCommit_SaveDirty(state))
                    return false;

                if (!PreCommit_AddNewFiles(state))
                    return false;

                if (!PreCommit_HandleMissingFiles(state))
                    return false;

                state.FlushState();

                if (!PreCommit_AddNeededParents(state))
                    return false;

                return true;
            }
        }

        public bool CreatePatch(IEnumerable<PendingChange> changes, PendingChangeCreatePatchArgs args)
        {
            using (PendingCommitState state = new PendingCommitState(Context, changes))
            {
                if (!PreCommit_VerifySingleRoot(state)) // Verify single root 'first'
                    return false;

                if (!PreCommit_SaveDirty(state))
                    return false;

                if (args.AddUnversionedFiles)
                {
                    if (!PreCommit_AddNewFiles(state))
                        return false;

                    if (!PreCommit_HandleMissingFiles(state))
                        return false;
                }
                state.FlushState();

                if (!PreCommit_AddNeededParents(state))
                    return false;

                if (!PreCommit_VerifySingleRoot(state)) // Verify single root 'again'
                    return false;
            }

            string relativeToPath = args.RelativeToPath;
            string relativeToPathP = relativeToPath.EndsWith("\\") ? relativeToPath : (relativeToPath + "\\");
            string fileName = args.FileName;
            GitRevisionRange revRange = new GitRevisionRange(GitRevision.Base, GitRevision.Working);

            GitDiffArgs a = new GitDiffArgs();
            a.IgnoreAncestry = true;
            a.NoDeleted = false;
            a.Depth = GitDepth.Empty;

            using (MemoryStream stream = new MemoryStream())
            {
                GetService<IProgressRunner>().RunModal(PccStrings.DiffTitle,
                    delegate(object sender, ProgressWorkerArgs e)
                    {
                        foreach (PendingChange pc in changes)
                        {
                            GitItem item = pc.GitItem;
                            GitWorkingCopy wc;
                            if (!string.IsNullOrEmpty(relativeToPath)
                                && item.FullPath.StartsWith(relativeToPathP, StringComparison.OrdinalIgnoreCase))
                                a.RelativeToPath = relativeToPath;
                            else if ((wc = item.WorkingCopy) != null)
                                a.RelativeToPath = wc.FullPath;
                            else
                                a.RelativeToPath = null;

                            e.Client.Diff(item.FullPath, revRange, a, stream);
                        }

                        stream.Flush();
                        stream.Position = 0;
                    });
                using (StreamReader sr = new StreamReader(stream))
                {
                    string line;

                    // Parse to lines to resolve EOL issues
                    using (StreamWriter sw = File.CreateText(fileName))
                    {
                        while (null != (line = sr.ReadLine()))
                            sw.WriteLine(line);
                    }
                }
            }
            return true;
        }

        public IEnumerable<PendingCommitState> GetCommitRoots(IEnumerable<PendingChange> changes)
        {
            List<GitWorkingCopy> wcs = new List<GitWorkingCopy>();
            List<List<PendingChange>> pcs = new List<List<PendingChange>>();

            foreach (PendingChange pc in changes)
            {
                GitItem item = pc.GitItem;
                GitWorkingCopy wc = item.WorkingCopy;

                if (wc != null)
                {
                    int n = wcs.IndexOf(wc);

                    List<PendingChange> wcChanges;
                    if (n < 0)
                    {
                        wcs.Add(wc);
                        pcs.Add(wcChanges = new List<PendingChange>());
                    }
                    else
                        wcChanges = pcs[n];

                    wcChanges.Add(pc);
                }
            }

            if (wcs.Count <= 1)
            {
                yield return new PendingCommitState(Context, changes);
                yield break;
            }

            using (MultiWorkingCopyCommit dlg = new MultiWorkingCopyCommit())
            {
                dlg.SetInfo(wcs, pcs);

                if (dlg.ShowDialog(Context) != DialogResult.OK || dlg.ChangeGroups.Count == 0)
                {
                    yield return null;
                    yield break;
                }

                foreach (List<PendingChange> chg in dlg.ChangeGroups)
                {
                    yield return new PendingCommitState(Context, chg);
                }
            }
        }


        public bool Commit(IEnumerable<PendingChange> changes, PendingChangeCommitArgs args)
        {
            // Ok, to make a commit happen we have to take 'a few' steps
            ILastChangeInfo ci = GetService<ILastChangeInfo>();

            if (ci != null)
                ci.SetLastChange(null, null);

            bool storeMessage = args.StoreMessageOnError;

            foreach (PendingCommitState state in GetCommitRoots(changes))
            {
                if (state == null)
                    return false;

                using (state)
                    try
                    {
                        state.LogMessage = args.LogMessage;

                        if (!PreCommit_VerifySingleRoot(state)) // Verify single root 'first'
                            return false;

                        if (!PreCommit_VerifyLogMessage(state))
                            return false;

                        if (!PreCommit_VerifyNoConflicts(state))
                            return false;

                        if (!PreCommit_SaveDirty(state))
                            return false;

                        if (!PreCommit_AddNewFiles(state))
                            return false;

                        if (!PreCommit_HandleMissingFiles(state))
                            return false;

                        state.FlushState();

                        if (!PreCommit_AddNeededParents(state))
                            return false;

                        if (!PreCommit_VerifySingleRoot(state)) // Verify single root 'again'
                            return false;
                        // if(!PreCommit_....())
                        //  return;

                        bool ok = false;
                        using (DocumentLock dl = GetService<IVisualGitOpenDocumentTracker>().LockDocuments(state.CommitPaths, DocumentLockType.NoReload))
                        using (dl.MonitorChangesForReload()) // Monitor files that are changed by keyword expansion
                        {
                            if (Commit_CommitToRepository(state))
                            {
                                storeMessage = true;
                                ok = true;
                            }
                        }

                        if (!ok)
                            return false;
                    }
                    finally
                    {
                        if (storeMessage)
                        {
                            if (state.LogMessage != null && state.LogMessage.Trim().Length > 0)
                            {
                                IVisualGitConfigurationService config = GetService<IVisualGitConfigurationService>();

                                if (config != null)
                                {
                                    config.GetRecentLogMessages().Add(state.LogMessage);
                                }
                            }
                        }
                    }
            }

            return true;
        }

        private bool PreCommit_VerifyNoConflicts(PendingCommitState state)
        {
            foreach (PendingChange pc in state.Changes)
            {
                GitItem item = pc.GitItem;

                if(item.IsConflicted)
                {
                    state.MessageBox.Show(PccStrings.OneOrMoreItemsConflicted,
                        "",
                        MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    
                    return false;
                }
            }

            return true;
        }

        private bool PreCommit_VerifySingleRoot(PendingCommitState state)
        {
            GitWorkingCopy wc = null;
            foreach (PendingChange pc in state.Changes)
            {
                GitItem item = pc.GitItem;

                if (item.IsVersioned || item.IsVersionable)
                {
                    GitWorkingCopy w = item.WorkingCopy;

                    if (wc == null)
                        wc = w;
                    else if (w != null && w != wc)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine(PccStrings.CommitSingleWc);
                        sb.AppendFormat(PccStrings.WorkingCopyX, wc.FullPath);
                        sb.AppendLine();
                        sb.AppendFormat(PccStrings.WorkingCopyX, w.FullPath);
                        sb.AppendLine();

                        state.MessageBox.Show(sb.ToString(), "", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

                        return false;
                    }
                }
            }
            return true;
        }
        /// <summary>
        /// Verifies if the log message is valid for the current policy
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns></returns>
        private bool PreCommit_VerifyLogMessage(PendingCommitState state)
        {
            if (state.LogMessage == null)
                return true; // Skip checks

            // And after checking whether the message is valid: Normalize the message the way the CLI would
            // * No whitespace at the end of lines
            // * Always a newline at the end

            StringBuilder sb = new StringBuilder();
            foreach (string line in state.LogMessage.Replace("\r", "").Split('\n'))
            {
                sb.AppendLine(line.TrimEnd());
            }

            string msg = sb.ToString();

            // And make sure the log message ends with a single newline
            state.LogMessage = msg.TrimEnd() + Environment.NewLine;

            return true; // Logmessage always ok for now
        }

        /// <summary>
        /// Save all documents in the selection
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns></returns>
        private bool PreCommit_SaveDirty(PendingCommitState state)
        {
            IVisualGitOpenDocumentTracker tracker = state.GetService<IVisualGitOpenDocumentTracker>();

            if (!tracker.SaveDocuments(state.CommitPaths))
            {
                state.MessageBox.Show(PccStrings.FailedToSaveBeforeCommit, "", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Exclamation);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Adds all files which are marked as to be added to Git
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns></returns>
        private bool PreCommit_AddNewFiles(PendingCommitState state)
        {
            foreach (PendingChange pc in state.Changes)
            {
                if (pc.Change != null && pc.Change.State == PendingChangeKind.New)
                {
                    GitItem item = pc.GitItem;

                    // HACK: figure out why PendingChangeKind.New is still true
                    if (item.IsVersioned)
                        continue; // No need to add

                    GitAddArgs a = new GitAddArgs();
                    a.AddParents = true;
                    a.Depth = GitDepth.Empty;
                    a.ThrowOnError = false;

                    if (!state.Client.Add(pc.FullPath, a))
                    {
                        if (a.LastException != null && a.LastException.ErrorCode == GitErrorCode.PathNoRepository)
                        {
                            state.MessageBox.Show(a.LastException.Message + Environment.NewLine + Environment.NewLine
                                + PccStrings.YouCanDownloadVisualGit, "", MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                            return false;
                        }
                        else if (state.MessageBox.Show(a.LastException.Message, "", MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Error) == DialogResult.Cancel)
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Adds all new parents of files to add to Git
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns></returns>
        private bool PreCommit_AddNeededParents(PendingCommitState state)
        {
            foreach (string path in new List<string>(state.CommitPaths))
            {
                GitItem item = state.Cache[path];

                if (item.IsNewAddition)
                {
                    GitItem parent = item.Parent;
                    GitWorkingCopy wc = item.WorkingCopy;

                    if (wc == null)
                    {
                        // This should be impossible. A node can't be added and not in a WC
                        item.MarkDirty();
                        continue;
                    }

                    string wcPath = wc.FullPath;

                    while (parent != null && 
                           !string.Equals(parent.FullPath, wcPath, StringComparison.OrdinalIgnoreCase)
                           && parent.IsNewAddition)
                    {
                        if (!state.CommitPaths.Contains(parent.FullPath))
                            state.CommitPaths.Add(parent.FullPath);

                        parent = parent.Parent;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Fixes up missing files by fixing their casing or deleting them
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns></returns>
        private bool PreCommit_HandleMissingFiles(PendingCommitState state)
        {
            foreach (string path in new List<string>(state.CommitPaths))
            {
                GitItem item = state.Cache[path];

                if (item.Status.State != GitStatus.Missing)
                    continue;

                if (item.IsCasingConflicted)
                {
                    string correctCasing = GetGitCasing(item);
                    string actualCasing = GitTools.GetTruePath(item.FullPath);

                    if (correctCasing == null || actualCasing == null || !string.Equals(correctCasing, actualCasing, StringComparison.OrdinalIgnoreCase))
                        continue; // Nothing to fix here :(

                    string correctFile = Path.GetFileName(correctCasing);
                    string actualFile = Path.GetFileName(actualCasing);

                    if (correctFile == actualFile)
                        continue; // Casing issue is not in the file; can't fix :(

                    IVisualGitOpenDocumentTracker odt = GetService<IVisualGitOpenDocumentTracker>();
                    using (odt.LockDocument(correctCasing, DocumentLockType.NoReload))
                    using (odt.LockDocument(actualCasing, DocumentLockType.NoReload))
                    {
                        try
                        {
                            File.Move(actualCasing, correctCasing);

                            // Fix the name in the commit list
                            state.CommitPaths[state.CommitPaths.IndexOf(path)] = actualCasing;
                        }
                        catch
                        { }
                        finally
                        {
                            item.MarkDirty();
                            GetService<IFileStatusMonitor>().ScheduleGlyphUpdate(item.FullPath);
                        }
                    }
                }
                else if (!item.Exists)
                {
                    GitDeleteArgs da = new GitDeleteArgs();
                    da.KeepLocal = true;
                    da.ThrowOnError = false;

                    if (!state.Client.Delete(path, da))
                    {
                        state.MessageBox.Show(da.LastException.Message, "", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                        return false;
                    }
                }
            }

            return true;
        }

        static string GetGitCasing(GitItem item)
        {
            throw new NotImplementedException();
#if false
            string name = null;
            // Find the correct casing
            using (SvnWorkingCopyClient wcc = new SvnWorkingCopyClient())
            {
                SvnWorkingCopyEntriesArgs ea = new SvnWorkingCopyEntriesArgs();
                ea.ThrowOnCancel = false;
                ea.ThrowOnError = false;

                wcc.ListEntries(item.Directory, ea,
                    delegate(object sender, SvnWorkingCopyEntryEventArgs e)
                    {
                        if (string.Equals(e.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            name = e.FullPath;
                        }
                    });
            }

            return name;
#endif
        }

        /// <summary>
        /// Finalizes the action by committing to the repository
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns></returns>
        private bool Commit_CommitToRepository(PendingCommitState state)
        {
            bool ok = false;
            GitCommitResult rslt = null;

            GitDepth depth = state.CalculateCommitDepth();

            if (depth == GitDepth.Unknown)
                return false;

            StringBuilder outOfDateMessage = null;
            ProgressRunnerResult r = state.GetService<IProgressRunner>().RunModal(PccStrings.CommitTitle,
                delegate(object sender, ProgressWorkerArgs e)
                {
                    GitCommitArgs ca = new GitCommitArgs();
                    ca.Depth = depth;
                    ca.LogMessage = state.LogMessage;
                    ca.AddExpectedError(GitErrorCode.OutOfDate);

                    ok = e.Client.Commit(
                        state.CommitPaths,
                        ca, out rslt);

                    if(!ok && ca.LastException != null)
                    {
                        if (ca.LastException.ErrorCode == GitErrorCode.OutOfDate)
                        {
                            outOfDateMessage = new StringBuilder();
                            Exception ex = ca.LastException;

                            while(ex != null)
                            {
                                outOfDateMessage.AppendLine(ex.Message);
                                ex = ex.InnerException;
                            }
                        }
                    }
                });

            if (outOfDateMessage != null)
            {
                state.MessageBox.Show(outOfDateMessage.ToString(),
                                      PccStrings.OutOfDateCaption,
                                      MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (rslt != null)
            {
                ILastChangeInfo ci = GetService<ILastChangeInfo>();

                if (ci != null && rslt.Revision != null)
                    ci.SetLastChange(PccStrings.CommittedPrefix, rslt.Revision.ToString());

                if (!string.IsNullOrEmpty(rslt.PostCommitError))
                    state.MessageBox.Show(rslt.PostCommitError, PccStrings.PostCommitError, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            return ok;
        }
    }
}
