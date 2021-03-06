// VisualGit.UI\Annotate\AnnotateEditorControl.cs
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
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Forms;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;


using VisualGit.Commands;
using VisualGit.Scc;
using VisualGit.Scc.UI;
using VisualGit.UI.PendingChanges;
using VisualGit.UI.VSSelectionControls;
using System.Drawing;
using SharpGit;

namespace VisualGit.UI.Annotate
{
    public partial class AnnotateEditorControl : VSEditorControl, ISelectionMapOwner<IAnnotateSection>
    {
        List<AnnotateRegion> blameSections = new List<AnnotateRegion>();
        SelectionItemMap _map;
        GitOrigin _origin;

        public AnnotateEditorControl()
        {
            InitializeComponent();
            editor.ReadOnly = true;

            if (VSVersion.VS2008OrOlder)
                editor.EnableNavigationBar = true;
        }

        protected override void OnFrameCreated(EventArgs e)
        {
            base.OnFrameCreated(e);

            if (DesignMode)
                return;

            _map = SelectionItemMap.Create<IAnnotateSection>(this);
            _map.Context = Context;

            if (SelectionChanged != null)
                SelectionChanged(this, EventArgs.Empty);
            // Set Notify that we have a selection, otherwise the first selection request fails.
            _map.NotifySelectionUpdated();

            CommandContext = VisualGitId.AnnotateContextGuid;
            KeyboardContext = new Guid(0x8B382828, 0x6202, 0x11d1, 0x88, 0x70, 0x00, 0x00, 0xF8, 0x75, 0x79, 0xD2); // Editor
            SetFindTarget(editor.FindTarget);

            blameMarginControl1.Init(Context, this, blameSections);
        }

        public void LoadFile(string annotateFile)
        {
            // Does this anything?
            this.Text = Path.GetFileName(annotateFile) + " (Annotated)";

            editor.LoadFile(annotateFile, false);
        }

        public void SetLanguageService(Guid language)
        {
            editor.ForceLanguageService = language;
        }

        internal int GetLineHeight()
        {
            return editor.LineHeight;
        }

        internal int GetTopLine()
        {
            // TODO: implement real fix for VS2010
            if (VSVersion.VS2010)
                return 0;

            Point p = editor.EditorClientTopLeft;
            return PointToClient(p).Y;
        }

        public void AddLines(GitOrigin origin, Collection<GitBlameEventArgs> blameResult, Dictionary<string, string> logMessages)
        {
            _origin = origin;

            SortedList<string, AnnotateSource> _sources = new SortedList<string, AnnotateSource>();

            AnnotateRegion section = null;
            blameSections.Clear();

            var minTime = DateTime.MaxValue;
            var maxTime = DateTime.MinValue;
            string min = null;
            string max = null;
            foreach (GitBlameEventArgs e in blameResult)
            {
                AnnotateSource src;
                if (!_sources.TryGetValue(e.Revision, out src))
                {
                    _sources.Add(e.Revision, src = new AnnotateSource(e, origin));

                    string msg;
                    if (logMessages.TryGetValue(e.Revision, out msg))
                        src.LogMessage = msg ?? "";
                    else
                    {
                        if (e.Time < minTime)
                        {
                            min = e.Revision;
                            minTime = e.Time;
                        }

                        if (e.Time > maxTime)
                        {
                            max = e.Revision;
                            maxTime = e.Time;
                        }
                    }
                }

                int line = (int)e.LineNumber;

                if (section == null || section.Source != src)
                {
                    section = new AnnotateRegion(line, src);
                    blameSections.Add(section);
                }
                else
                {
                    section.EndLine = line;
                }
            }
            blameMarginControl1.Invalidate();

            RetrieveLogs(origin, _sources, min, max);
        }

        private void RetrieveLogs(GitOrigin origin, SortedList<string, AnnotateSource> _sources, string min, string max)
        {
            if (_sources.Count == 0 || min == null || max == null)
                return;

            EventHandler<GitLogEventArgs> lr =
                delegate(object sender, GitLogEventArgs e)
                {
                    AnnotateSource src;
                    if (_sources.TryGetValue(e.Revision, out src))
                        src.LogMessage = e.LogMessage ?? "";
                };

            VisualGitAction aa = delegate()
            {
                using (GitClient cl = Context.GetService<IGitClientPool>().GetClient())
                {
                    GitLogArgs la = new GitLogArgs();
                    la.OperationalRevision = origin.Target.Revision;
                    la.Start = max;
                    la.End = (GitRevision)min - 1;
                    la.ThrowOnError = false;
                    la.Log += lr;

                    cl.Log(origin.Target.FullPath, la);
                }
            };

            aa.BeginInvoke(null, null); // Start retrieving logs
        }

        private void logMessageEditor1_VerticalScroll(object sender, VSTextEditorScrollEventArgs e)
        {
            blameMarginControl1.NotifyVerticalScroll(e);
        }

        AnnotateSource _selected;
        internal AnnotateSource Selected
        {
            get { return _selected; }
        }

        internal void SetSelection(IAnnotateSection section)
        {
            // Check if necessary
            //Focus();
            //Select();

            _selected = (AnnotateSource)section;

            if (SelectionChanged != null)
                SelectionChanged(this, EventArgs.Empty);

            _map.NotifySelectionUpdated();
        }

        bool _disabledOutlining;
        protected override void OnFrameLoaded(EventArgs e)
        {
            base.OnFrameLoaded(e);

            editor.Select(); // This should enable the first OnIdle to disable the outlining
            DisableOutliningOnIdle();
        }

        void DisableOutliningOnIdle()
        {
            Context.GetService<IVisualGitCommandService>().PostIdleAction(DisableOutlining);
        }

        void DisableOutlining()
        {
            if (!IsDisposed && !_disabledOutlining)
            {
                Guid vs2kcmds = VSConstants.VSStd2K;
                if (editor.ContainsFocus)
                {
                    int n = editor.EditorCommandTarget.Exec(ref vs2kcmds, (uint)VSConstants.VSStd2KCmdID.OUTLN_STOP_HIDING_ALL, (uint)OLECMDEXECOPT.OLECMDEXECOPT_DODEFAULT, IntPtr.Zero, IntPtr.Zero);

                    if (n == 0)
                    {
                        _disabledOutlining = true;
                        return;
                    }
                }

                DisableOutliningOnIdle();
            }
        }

        #region ISelectionMapOwner<IAnnotateSection> Members

        public event EventHandler SelectionChanged;

        System.Collections.IList ISelectionMapOwner<IAnnotateSection>.Selection
        {
            get
            {
                if (_selected == null)
                    return new AnnotateSource[0];

                return new AnnotateSource[] { _selected };
            }
        }

        System.Collections.IList ISelectionMapOwner<IAnnotateSection>.AllItems
        {
            get { return ((ISelectionMapOwner<IAnnotateSection>)this).Selection; }
        }

        IntPtr ISelectionMapOwner<IAnnotateSection>.GetImageList()
        {
            return IntPtr.Zero;
        }

        int ISelectionMapOwner<IAnnotateSection>.GetImageListIndex(IAnnotateSection item)
        {
            return 0;
        }

        string ISelectionMapOwner<IAnnotateSection>.GetText(IAnnotateSection item)
        {
            return item.Revision.ToString();
        }

        public object GetSelectionObject(IAnnotateSection item)
        {
            return item;
        }

        public IAnnotateSection GetItemFromSelectionObject(object item)
        {
            return (IAnnotateSection)item;
        }

        void ISelectionMapOwner<IAnnotateSection>.SetSelection(IAnnotateSection[] items)
        {
            if (items.Length > 0)
                SetSelection(items[0]);
            else
                SetSelection((IAnnotateSection)null);
        }

        /// <summary>
        /// Gets the canonical (path / uri) of the item. Used by packages to determine a selected file
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>A canonical name or null</returns>
        string ISelectionMapOwner<IAnnotateSection>.GetCanonicalName(IAnnotateSection item)
        {
            return null;
        }

        #endregion

        Control ISelectionMapOwner<IAnnotateSection>.Control
        {
            get { return this; }
        }
    }
}
