// VisualGit.VS\VisualGitVSModule.cs
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
using System.Reflection;
using System.Text;

using VisualGit.Scc;

namespace VisualGit.VS
{
    /// <summary>
    /// 
    /// </summary>
    public class VisualGitVSModule : Module
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VisualGitVSModule"/> class.
        /// </summary>
        /// <param name="runtime">The runtime.</param>
        public VisualGitVSModule(VisualGitRuntime runtime)
            : base(runtime)
        {
        }

        /// <summary>
        /// Called when added to the <see cref="VisualGitRuntime"/>
        /// </summary>
        public override void OnPreInitialize()
        {
            Assembly thisAssembly = typeof(VisualGitVSModule).Assembly;

            Runtime.CommandMapper.LoadFrom(thisAssembly);

            Runtime.LoadServices(Container, thisAssembly, Context);

        }

        /// <summary>
        /// Called when <see cref="VisualGitRuntime.Start"/> is called
        /// </summary>
        public override void OnInitialize()
        {
            EnsureService<IFileStatusCache>();
            EnsureService<IStatusImageMapper>();
        }
    }
}
