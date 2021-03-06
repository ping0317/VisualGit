// VisualGit\Services\PendingChanges\PendingChangeHandler.cs
//
// Copyright 2008-2011 The AnkhSVN Project
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//
// Changes and additions made for VisualGit Copyright 2011 Pieter van Ginkel.

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
                        state.AmendLastCommit = args.AmendLastCommit;

                        if (!PreCommit_VerifyConfiguration(state))
                            return false;

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

        private bool PreCommit_VerifyConfiguration(PendingCommitState state)
        {
            using (GitPoolClient client = state.GetService<IGitClientPool>().GetNoUIClient())
            {
                IGitConfig config = client.GetUserConfig();

                if (
                    String.IsNullOrEmpty(config.GetString("user", null, "name")) ||
                    String.IsNullOrEmpty(config.GetString("user", null, "email"))
                ) {
                    state.MessageBox.Show(PccStrings.NameOrEmailNotSet,
                        "",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);

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
            if (String.IsNullOrWhiteSpace(state.LogMessage))
            {
                DialogResult result = state.MessageBox.Show(PccStrings.NoMessageProvided,
                    "",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.No)
                    return false;
            }

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

                    try
                    {
                        state.Client.Add(pc.FullPath, a);
                    }
                    catch (GitException ex)
                    {
                        GetService<IVisualGitErrorHandler>().OnWarning(ex);
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

                    try
                    {
                        state.Client.Delete(path, da);
                    }
                    catch (GitException ex)
                    {
                        GetService<IVisualGitErrorHandler>().OnWarning(ex);
                        return false;
                    }
                }
            }

            return true;
        }

        string GetGitCasing(GitItem item)
        {
            string name = null;
            // Find the correct casing
            using (GitClient client = Context.GetService<IGitClientPool>().GetNoUIClient())
            {
                GitStatusArgs args = new GitStatusArgs();

                args.Depth = GitDepth.Files;
                args.RetrieveAllEntries = false;
                args.RetrieveIgnoredEntries = false;
                args.ThrowOnCancel = false;
                args.ThrowOnError = false;

                client.Status(item.Directory, args,
                    delegate(object sender, GitStatusEventArgs ea)
                    {
                        if (string.Equals(ea.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            name = ea.FullPath;
                        }
                    });
            }

            return name;
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

            ProgressRunnerResult r = state.GetService<IProgressRunner>().RunModal(PccStrings.CommitTitle,
                delegate(object sender, ProgressWorkerArgs e)
                {
                    GitCommitArgs ca = new GitCommitArgs();
                    ca.Depth = depth;
                    ca.LogMessage = state.LogMessage;
                    ca.AmendLastCommit = state.AmendLastCommit;

                    try
                    {
                        ok = e.Client.Commit(
                            state.CommitPaths,
                            ca, out rslt);
                    }
                    catch (GitException ex)
                    {
                        GetService<IVisualGitErrorHandler>().OnWarning(ex);
                        ok = false;
                    }
                });

            if (rslt != null)
            {
                ILastChangeInfo ci = GetService<ILastChangeInfo>();

                if (ci != null && rslt.Revision != null)
                    ci.SetLastChange(PccStrings.CommittedPrefix, rslt.Revision.ToString());
            }

            return ok;
        }
    }
}
