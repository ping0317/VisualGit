// VisualGit.Scc\VisualGitSccProvider.cs
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
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;


using VisualGit.Commands;
using VisualGit.Scc.ProjectMap;
using VisualGit.Selection;
using VisualGit.VS;

namespace VisualGit.Scc
{
    [GuidAttribute(VisualGitId.SccServiceId), ComVisible(true), CLSCompliant(false)]
    public interface ITheVisualGitSccProvider : IVsSccProvider
    {
    }

    [GlobalService(typeof(VisualGitSccProvider))]
    [GlobalService(typeof(IVisualGitSccService))]
    [GlobalService(typeof(ITheVisualGitSccProvider), true)]
    partial class VisualGitSccProvider : VisualGitService, ITheVisualGitSccProvider, IVsSccProvider, IVsSccControlNewSolution, IVisualGitSccService, IVsSccEnlistmentPathTranslation
    {
        bool _active;
        IFileStatusCache _statusCache;
        IVisualGitOpenDocumentTracker _documentTracker;
        VisualGitSccSettingStorage _sccSettings;

        public VisualGitSccProvider(IVisualGitServiceProvider context)
            : base(context)
        {
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();

            GetService<VisualGitServiceEvents>().RuntimeStarted
                += delegate
                {
                    IVisualGitCommandStates states;

                    states = GetService<IVisualGitCommandStates>();

                    if (states == null || !states.SccProviderActive)
                        return;

                    // Ok, Visual Studio decided to activate the user context with our GUID
                    // This tells us VS wants us to be the active SCC
                    //
                    // This is not documented directly. But it is documented that we should
                    // enable our commands on that context

                    // Set us active; this makes VS initialize the provider
                    RegisterAsPrimarySccProvider();
                };
        }

        public void RegisterAsPrimarySccProvider()
        {
            IVsRegisterScciProvider rscp = GetService<IVsRegisterScciProvider>();
            if (rscp != null)
            {
                ErrorHandler.ThrowOnFailure(rscp.RegisterSourceControlProvider(VisualGitId.SccProviderGuid));
            }
        }

        /// <summary>
        /// Gets the status cache.
        /// </summary>
        /// <value>The status cache.</value>
        IFileStatusCache StatusCache
        {
            get { return _statusCache ?? (_statusCache = GetService<IFileStatusCache>()); }
        }

        IVisualGitOpenDocumentTracker DocumentTracker
        {
            get { return _documentTracker ?? (_documentTracker = GetService<IVisualGitOpenDocumentTracker>()); }
        }
        
        VisualGitSccSettingStorage SccStore
        {
            get { return _sccSettings ?? (_sccSettings = GetService<VisualGitSccSettingStorage>(typeof(ISccSettingsStore))); }
        }


        /// <summary>
        /// Determines if any item in the solution are under source control.
        /// </summary>
        /// <param name="pfResult">[out] Returns non-zero (TRUE) if there is at least one item under source control; otherwise, returns zero (FALSE).</param>
        /// <returns>
        /// The method returns <see cref="F:Microsoft.VisualStudio.VSConstants.S_OK"></see>.
        /// </returns>
        public int AnyItemsUnderSourceControl(out int pfResult)
        {
            // Set pfResult to false when the solution can change to an other scc provider
            bool oneManaged = _active && IsSolutionManaged;

            if (_active && !oneManaged)
            {
                foreach (SccProjectData data in _projectMap.Values)
                {
                    if (data.IsManaged)
                    {
                        oneManaged = true;
                        break;
                    }
                }
            }
            pfResult = oneManaged ? 1 : 0;
            return VSConstants.S_OK;
        }

        /// <summary>
        /// Called by environment to mark a particular source control package as active.
        /// </summary>
        /// <returns>
        /// The method returns <see cref="F:Microsoft.VisualStudio.VSConstants.S_OK"></see>.
        /// </returns>
        public int SetActive()
        {
            if (!_active)
            {
                _active = true;

                // Delayed flush all glyphs of all projects when a user enables us.
                IFileStatusMonitor pn = GetService<IFileStatusMonitor>();

                if (pn != null)
                {
                    List<GitProject> allProjects = new List<GitProject>(GetAllProjects());
                    allProjects.Add(GitProject.Solution);

                    pn.ScheduleGlyphOnlyUpdate(allProjects);
                }
            }

            _ensureIcons = true;
            RegisterForSccCleanup();

            IVisualGitCommandStates states = GetService<IVisualGitCommandStates>();

            //if (states.UIShellAvailable)
            //{
            //    IVisualGitMigrationService migrate = GetService<IVisualGitMigrationService>();
            //
            //    if (migrate != null)
            //        migrate.MaybeMigrate();
            //}

            GetService<IVisualGitServiceEvents>().OnSccProviderActivated(EventArgs.Empty);

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Called by environment to mark a particular source control package as inactive.
        /// </summary>
        /// <returns>
        /// The method returns <see cref="F:Microsoft.VisualStudio.VSConstants.S_OK"></see>.
        /// </returns>
        public int SetInactive()
        {
            if (_active)
            {
                _active = false;

                // Disable our custom glyphs before an other SCC provider is initialized!
                IVisualGitSolutionExplorerWindow solutionExplorer = GetService<IVisualGitSolutionExplorerWindow>();

                if (solutionExplorer != null)
                    solutionExplorer.EnableVisualGitIcons(false);

                // If VS asked us for c ustom glyphs, we can release the handle now
                if (_glyphList != null)
                {
                    _glyphList.Dispose();
                    _glyphList = null;
                }

                // Remove all glyphs currently set
                foreach (SccProjectData pd in _projectMap.Values)
                {
                    pd.NotifyGlyphsChanged();
                }

                ClearSolutionGlyph();
            }

            GetService<IVisualGitServiceEvents>().OnSccProviderDeactivated(EventArgs.Empty);

            return VSConstants.S_OK;
        }

        /// <summary>
        /// This function determines whether the source control package is installed. 
        /// Source control packages should always return S_OK and pbInstalled = nonzero..
        /// </summary>
        /// <param name="pbInstalled">The pb installed.</param>
        /// <returns></returns>
        public int IsInstalled(out int pbInstalled)
        {
            pbInstalled = 1; // We are always installed as we have no external dependencies

            return VSConstants.S_OK;
        }

        /// <summary>
        /// This method is called by projects that are under source control 
        /// when they are first opened to register project settings.
        /// </summary>
        /// <param name="pscp2Project">The PSCP2 project.</param>
        /// <param name="pszSccProjectName">Name of the PSZ SCC project.</param>
        /// <param name="pszSccAuxPath">The PSZ SCC aux path.</param>
        /// <param name="pszSccLocalPath">The PSZ SCC local path.</param>
        /// <param name="pszProvider">The PSZ provider.</param>
        /// <returns></returns>
        public int RegisterSccProject(IVsSccProject2 pscp2Project, string pszSccProjectName, string pszSccAuxPath, string pszSccLocalPath, string pszProvider)
        {
            SccProjectData data;
            if (!_projectMap.TryGetValue(pscp2Project, out data))
            {
                // This method is called before the OpenProject calls
                _projectMap.Add(pscp2Project, data = new SccProjectData(Context, pscp2Project));
            }

            data.IsManaged = (pszProvider == VisualGitId.GitSccName);
            data.IsRegistered = true;

            _syncMap = true;
            RegisterForSccCleanup();

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Called by projects registered with the source control portion of the environment before they are closed.
        /// </summary>
        /// <param name="pscp2Project">The PSCP2 project.</param>
        /// <returns></returns>
        public int UnregisterSccProject(IVsSccProject2 pscp2Project)
        {
            SccProjectData data;
            if (_projectMap.TryGetValue(pscp2Project, out data))
            {
                data.IsRegistered = false;
            }

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Gets a value indicating whether the VisualGit Scc service is active
        /// </summary>
        /// <value><c>true</c> if this instance is active; otherwise, <c>false</c>.</value>
        public bool IsActive
        {
            get { return _active; }
        }

        #region // Obsolete Methods
        /// <summary>
        /// Obsolete: returns E_NOTIMPL.
        /// </summary>
        /// <param name="pbstrDirectory">The PBSTR directory.</param>
        /// <param name="pfOK">The pf OK.</param>
        /// <returns></returns>
        public int BrowseForProject(out string pbstrDirectory, out int pfOK)
        {
            pbstrDirectory = null;
            pfOK = 0;

            return VSConstants.E_NOTIMPL;
        }

        /// <summary>
        /// Obsolete: returns E_NOTIMPL.
        /// </summary>
        /// <returns></returns>
        public int CancelAfterBrowseForProject()
        {
            return VSConstants.E_NOTIMPL;
        }
        #endregion
    }
}
