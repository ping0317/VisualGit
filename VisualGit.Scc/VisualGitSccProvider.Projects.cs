// VisualGit.Scc\VisualGitSccProvider.Projects.cs
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
using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell.Interop;

using VisualGit.Commands;
using VisualGit.Scc.ProjectMap;
using VisualGit.Selection;
using VisualGit.VS;
using System.IO;
using SharpGit;
using VisualGit.Scc.StatusCache;

namespace VisualGit.Scc
{
    partial class VisualGitSccProvider
    {
        readonly Dictionary<IVsSccProject2, SccProjectData> _projectMap = new Dictionary<IVsSccProject2, SccProjectData>();
        readonly Dictionary<string, string> _backupMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool _managedSolution;
        List<string> _delayedDelete;
        List<FixUp> _delayedMove;

        class FixUp
        {
            public readonly string From;
            public readonly string To;

            public FixUp(string from, string to)
            {
                From = from;
                To = to;
            }
        }
        bool _isDirty;

        public bool IsSolutionManaged
        {
            get { return _managedSolution; }
        }

        public void LoadingManagedSolution(bool asPrimarySccProvider)
        {
            // Called by the package when a solution is loaded which is marked as managed by us
            _managedSolution = asPrimarySccProvider;
        }

        public bool IsProjectManaged(GitProject project)
        {
            if (!IsActive)
                return false;

            if (project == null)
                return IsSolutionManaged;

            return IsProjectManagedRaw(project.RawHandle);
        }

        public bool IsProjectManagedRaw(object project)
        {
            if (!IsActive)
                return false;

            if (project == null)
                return IsSolutionManaged;

            IVsSccProject2 sccProject = project as IVsSccProject2;

            if (sccProject == null)
                return false;

            SccProjectData data;

            if (_projectMap.TryGetValue(sccProject, out data))
                return data.IsManaged;

            return false;
        }

        public void SetProjectManaged(GitProject project, bool managed)
        {
            if (!IsActive)
                return; // Perhaps allow clearing management settings?

            if (project == null)
                SetProjectManagedRaw(null, managed);
            else
                SetProjectManagedRaw(project.RawHandle, managed);
        }

        public void EnsureCheckOutReference(GitProject project)
        {
            // NOOP for today
        }

        public void SetProjectManagedRaw(object project, bool managed)
        {
            if (!IsActive)
                return;

            if (project == null)
            {
                // We are talking about the solution

                if (managed != IsSolutionManaged)
                {
                    _managedSolution = managed;
                    IsSolutionDirty = true;

                    foreach (SccProjectData p in _projectMap.Values)
                    {
                        if (p.IsSolutionFolder || p.IsWebSite)
                        {
                            p.SetManaged(managed);

                            if (managed)
                                p.NotifyGlyphsChanged();
                        }
                    }
                }
                return;
            }

            IVsSccProject2 sccProject = project as IVsSccProject2;

            if (sccProject == null)
            {
                if (project is IVsSolution)
                    SetProjectManagedRaw(null, managed);

                return;
            }

            SccProjectData data;

            if (!_projectMap.TryGetValue(sccProject, out data))
                return;

            if (managed == data.IsManaged)
                return; // Nothing to change

            data.SetManaged(managed);
        }

        internal SccProjectData GetSccProject(Guid projectId)
        {
            foreach (SccProjectData pd in _projectMap.Values)
            {
                if (pd.ProjectGuid == projectId)
                    return pd;
            }

            return null;
        }

        public bool IsSolutionDirty
        {
            // TODO: Only return true if the solution was not previously managed by VisualGit
            get { return _isDirty; }
            set { _isDirty = value; }
        }

        public bool IsSolutionLoaded
        {
            get { return _solutionLoaded; }
        }

        /// <summary>
        /// Called by ProjectDocumentTracker when a solution is opened 
        /// </summary>
        internal void OnSolutionOpened(bool onLoad)
        {
            _solutionLoaded = true;
            _solutionFile = _solutionDirectory = null;

            GitTools.InvalidateDirectory(SolutionDirectory);

            foreach (SccProjectData data in _projectMap.Values)
            {
                if (!String.IsNullOrEmpty(data.ProjectDirectory))
                    GitTools.InvalidateDirectory(data.ProjectDirectory);
            }

            if (!IsActive)
            {
                IVisualGitCommandStates states = GetService<IVisualGitCommandStates>();

                if (states == null || states.OtherSccProviderActive)
                    return;
            }

            if (!IsSolutionManaged)
            {
                string dir = SolutionDirectory;

                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    if (!GitTools.IsBelowManagedPath(dir))
                        return; // Not for us

                    // VisualGit 0.x and 1.x place VisualGit.Load files to trigger loading
                    if (File.Exists(Path.Combine(dir, "VisualGit.Load")))
                    {
                        // Ok there is no (other) Scc provider active and there is an VisualGit.load

                        if (!IsActive)
                            RegisterAsPrimarySccProvider(); // Make us active (Yes!)

                        SetProjectManaged(null, true);

                        Debug.Assert(IsActive, "We should be active now!");
                    }
                    else if (File.Exists(Path.Combine(dir, "VisualGit.NoLoad")))
                        return; // Explicit don't load VisualGit, can be overridden from the .sln
                    else
                    {
                        if (!IsActive)
                            RegisterAsPrimarySccProvider(); // Set us active; we know there is a .svn

                        // Ask the user whether we should be registered in the solution?
                    }

                    // BH: Many users seem to have .load and .noload files checked in
                    // so we can't just remove them without issues.
                }
            }

            if (!IsActive)
                return;

            IVisualGitSolutionExplorerWindow window = GetService<IVisualGitSolutionExplorerWindow>();

            if (window != null)
                window.EnableVisualGitIcons(true);

            foreach (SccProjectData data in _projectMap.Values)
            {
                if (data.IsSolutionFolder)
                {
                    // Solution folders don't save their Scc management state
                    // We let them follow the solution settings

                    if (IsSolutionManaged)
                        data.SetManaged(true);
                }

                if (data.IsSolutionFolder || data.IsWebSite)
                {
                    // Flush the glyph cache of solution folders
                    // (Well known VS bug: Initially clear)
                    data.NotifyGlyphsChanged();
                }
            }

            IPendingChangesManager mgr = GetService<IPendingChangesManager>();
            if (mgr != null && mgr.IsActive)
                mgr.FullRefresh(false);

            using (new FileStatusRefreshHint(true))
            {
                UpdateSolutionGlyph();
            }
        }

        string _solutionFile;
        string _solutionDirectory;
        public string SolutionFilename
        {
            get
            {
                if (_solutionFile == null)
                    LoadSolutionInfo();

                return _solutionFile.Length > 0 ? _solutionFile : null;
            }
        }

        public string SolutionDirectory
        {
            get
            {
                if (_solutionDirectory == null)
                    LoadSolutionInfo();

                return _solutionDirectory;
            }
        }

        void LoadSolutionInfo()
        {
            string dir, path, user;

            IVsSolution sol = GetService<IVsSolution>(typeof(SVsSolution));

            if (sol == null ||
                !ErrorHandler.Succeeded(sol.GetSolutionInfo(out dir, out path, out user)))
            {
                _solutionDirectory = _solutionFile = "";
                return;
            }

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(path))
            {
                // Cache negative result; will be returned as null
                _solutionDirectory = _solutionFile = "";
            }
            else
            {
                if (GitItem.IsValidPath(dir))
                    _solutionDirectory = GitTools.GetTruePath(dir, true) ?? GitTools.GetNormalizedFullPath(dir);
                else
                    _solutionDirectory = "";

                if (GitItem.IsValidPath(path))
                    _solutionFile = GitTools.GetTruePath(path, true) ?? GitTools.GetNormalizedFullPath(path);
                else
                    _solutionFile = "";
            }
        }

        /// <summary>
        /// Called by ProjectDocumentTracker just before a solution is closed
        /// </summary>
        /// <remarks>At this time the closing can not be canceled.</remarks>
        internal void OnStartedSolutionClose()
        {
            if (IsActive)
            {
                IVisualGitSolutionExplorerWindow window = GetService<IVisualGitSolutionExplorerWindow>();

                if (window != null)
                    window.EnableVisualGitIcons(false);
            }

            foreach (SccProjectData pd in _projectMap.Values)
            {
                pd.Dispose();
            }

#if !DEBUG
            // Skip file by file cleanup of the project<-> file mapping
            // Should proably always be enabled around the release of VisualGit 2.0
            _projectMap.Clear();
            _fileMap.Clear();
#endif
        }

        /// <summary>
        /// Called by ProjectDocumentTracker when a solution is closed
        /// </summary>
        internal void OnSolutionClosed()
        {
            Debug.Assert(_projectMap.Count == 0);
            Debug.Assert(_fileMap.Count == 0);

            _solutionFile = _solutionDirectory = null;
            _projectMap.Clear();
            _fileMap.Clear();
            _unreloadable.Clear();
            StatusCache.ClearCache();

            // Clear status for reopening solution
            _managedSolution = false;
            _isDirty = false;
            _solutionLoaded = false;
            _reloading = null;
            ClearEnlistState();

            IPendingChangesManager mgr = GetService<IPendingChangesManager>();
            if (mgr != null)
                mgr.Clear();
        }

        /// <summary>
        /// Called by ProjectDocumentTracker when a scc-capable project is loaded
        /// </summary>
        /// <param name="project"></param>
        internal void OnProjectLoaded(IVsSccProject2 project)
        {
            if (!_projectMap.ContainsKey(project))
                _projectMap.Add(project, new SccProjectData(Context, project));
        }

        internal void OnProjectRenamed(IVsSccProject2 project)
        {
            if (string.IsNullOrEmpty(SolutionFilename))
                return;

            SccProjectData data;
            if (!_projectMap.TryGetValue(project, out data))
                return;

            string oldLocation = data.ProjectLocation;
            try
            {
                using (SccProjectData newData = new SccProjectData(Context, project))
                {
                    string newLocation = newData.ProjectLocation;

                    if (newLocation == null && oldLocation == null)
                        return; // No need to do anything for this case (e.g. solution folders)

                    SccStore.OnProjectRenamed(oldLocation, newLocation);

                    if (string.Equals(newData.ProjectFile, data.ProjectFile, StringComparison.OrdinalIgnoreCase))
                        return; // Project rename, without renaming the project file (C++ project for instance)
                }
            }
            finally
            {
                // Mark the sln file edited, so it shows up in Pending Changes/Commit
                if (!string.IsNullOrEmpty(SolutionFilename))
                    DocumentTracker.CheckDirty(SolutionFilename);

                data.Reload(); // Reload project name, etc.
            }
        }

        /// <summary>
        /// Called by ProjectDocumentTracker when a scc-capable project is opened
        /// </summary>
        /// <param name="project">The loaded project</param>
        /// <param name="added">The project was added after opening</param>
        internal void OnProjectOpened(IVsSccProject2 project, bool added)
        {
            SccProjectData data;
            if (!_projectMap.TryGetValue(project, out data))
                _projectMap.Add(project, data = new SccProjectData(Context, project));

            if (data.IsSolutionFolder || data.IsWebSite)
            {
                if (IsSolutionManaged)
                {
                    // We let them follow the solution settings (See OnSolutionOpen() for the not added case
                    if (added && data.IsSolutionFolder)
                        data.SetManaged(true);
                }

                // Solution folders are projects without Scc state
                data.NotifyGlyphsChanged();
            }

            _syncMap = true;

            // Don't take the focus from naming the folder. The rename will perform the .Load()
            // and dirty check
            if (added && data.IsSolutionFolder)
                return;

            bool isReload = (_reloading == data.ProjectLocation);
            _reloading = null;
            if (added && !isReload)
            {
                if (!string.IsNullOrEmpty(SolutionFilename))
                    DocumentTracker.CheckDirty(SolutionFilename);
            }

            RegisterForSccCleanup();
        }

        /// <summary>
        /// Called when a project is explicitly refreshed
        /// </summary>
        /// <param name="project"></param>
        internal void RefreshProject(IVsSccProject2 project)
        {
            if (project == null)
                throw new ArgumentNullException("project");

            SccProjectData data;
            if (_projectMap.TryGetValue(project, out data))
            {
                data.Reload();
            }
        }

        IVsSolutionBuildManager2 _buildManager;
        IVsSolutionBuildManager2 BuildManager
        {
            get { return _buildManager ?? (_buildManager = GetService<IVsSolutionBuildManager2>(typeof(SVsSolutionBuildManager))); }
        }

        internal bool TrackProjectChanges(IVsSccProject2 project)
        {
            // We can be called with a null project
            SccProjectData data;

            if (project != null && _projectMap.TryGetValue(project, out data))
            {
                if (!data.IsWebSite &&_syncMap)
                    data.Load();

                return data.TrackProjectChanges(); // Allows temporary disabling changes
            }

            return false;
        }

        string _reloading;

        /// <summary>
        /// Called by ProjectDocumentTracker when a scc-capable project is closed
        /// </summary>
        /// <param name="project">The project.</param>
        /// <param name="removed">if set to <c>true</c> the project is being removed or unloaded from the solution.</param>
        internal void OnProjectClosed(IVsSccProject2 project, bool removed)
        {
            SccProjectData data;
            _reloading = null;

            if (_projectMap.TryGetValue(project, out data))
            {
                data.OnClose();
                _projectMap.Remove(project);

                if (data.Unloading)
                    _reloading = data.ProjectLocation;
            }

            if (removed && _reloading == null)
            {
                if (!string.IsNullOrEmpty(SolutionFilename))
                    DocumentTracker.CheckDirty(SolutionFilename);
            }
        }

        internal void OnProjectBeforeUnload(IVsSccProject2 project, IVsHierarchy pStubHierarchy)
        {
            SccProjectData data;
            if (_projectMap.TryGetValue(project, out data))
            {
                data.Unloading = true;
                Monitor.ScheduleMonitor(data.GetAllFiles()); // Keep track of changes in files of unloaded project
            }
        }

        bool _ensureIcons;
        bool _registeredSccCleanup;
        internal void OnSccCleanup(CommandEventArgs e)
        {
            _registeredSccCleanup = false;

            if ((_ensureIcons || _syncMap) && IsActive)
            {
                // Enable our custom glyphs when we are set active
                IVisualGitSolutionExplorerWindow solutionExplorer = GetService<IVisualGitSolutionExplorerWindow>();

                if (solutionExplorer != null)
                    solutionExplorer.EnableVisualGitIcons(true);
            }

            if (_syncMap)
            {
                _syncMap = false;

                foreach (SccProjectData pd in _projectMap.Values)
                    pd.Load();
            }

            if (_delayedDelete != null)
            {
                List<string> files = _delayedDelete;
                _delayedDelete = null;

                using (GitSccContext git = new GitSccContext(Context))
                {
                    foreach (string file in files)
                    {
                        if (!File.Exists(file))
                        {
                            git.SafeDeleteFile(file);
                            MarkDirty(file);
                        }
                    }
                }
            }

            if (_delayedMove != null)
            {
                List<FixUp> files = _delayedMove;
                _delayedMove = null;

                using (GitSccContext git = new GitSccContext(Context))
                {
                    foreach (FixUp fu in files)
                    {
                        if (!git.IsUnversioned(fu.From) && git.IsUnversioned(fu.To))
                        {
                            git.SafeWcMoveFixup(fu.From, fu.To);
                        }
                    }
                }
            }
            if (_backupMap.Count > 0)
            {
                using (GitSccContext git = new GitSccContext(Context))
                {
                    foreach (KeyValuePair<string, string> dir in _backupMap)
                    {
                        string originalDir = dir.Key;
                        string backupDir = dir.Value;

                        if (!Directory.Exists(backupDir))
                            continue; // No backupdir, we can't delete or move it

                        if (Directory.Exists(originalDir))
                        {
                            // The original has not been deleted by visual studio, must be an exclude.
                            git.DeleteDirectory(backupDir);
                        }
                        else
                        {
                            // Original is gone, must be a delete, put back backup so we can git-delete it
                            GitSccContext.RetriedRename(backupDir, originalDir); // Use retried rename, to prevent virus-scanner locks
                            git.WcDelete(originalDir);
                        }
                    }
                }
                _backupMap.Clear();
            }
        }

        public void ScheduleGitRefresh(List<GitClientAction> sccRefreshItems)
        {
            if (sccRefreshItems == null)
                throw new ArgumentNullException("sccRefreshItems");

            CommandService.PostIdleAction(
                delegate
                {
                    foreach (SccProjectData project in _projectMap.Values)
                    {
                        if (project.RequiresForcedRefresh() && !string.IsNullOrEmpty(project.ProjectDirectory))
                        {
                            string dir = project.ProjectDirectory;

                            if (!dir.EndsWith("\\"))
                                dir += "\\";

                            foreach (GitClientAction action in sccRefreshItems)
                            {
                                if (action.FullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                                {
                                    project.PerformRefresh(sccRefreshItems);
                                    break;
                                }
                            }
                        }
                    }
                });
        }

        IVisualGitCommandService _commandService;
        IVisualGitCommandService CommandService
        {
            get { return _commandService ?? (_commandService = GetService<IVisualGitCommandService>()); }
        }

        void RegisterForSccCleanup()
        {
            if (_registeredSccCleanup)
                return;

            IVisualGitCommandService cmd = CommandService;

            if (cmd != null)
                cmd.PostTickCommand(ref _registeredSccCleanup, VisualGitCommand.SccFinishTasks);
        }

        int _disableGitUpdates;
        public IDisposable DisableGitUpdates()
        {
            _disableGitUpdates++;

            return new UndisableGitUpdate(this);
        }

        public bool GitUpdatesDisabled
        {
            [DebuggerStepThrough]
            get { return _disableGitUpdates > 0; }
        }

        sealed class UndisableGitUpdate : IDisposable
        {
            readonly VisualGitSccProvider _provider;
            bool _disposed;
            public UndisableGitUpdate(VisualGitSccProvider provider)
            {
                _provider = provider;
            }
            #region IDisposable Members

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _provider._disableGitUpdates--;
            }

            #endregion
        }
    }
}
