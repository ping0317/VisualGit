// VisualGit.Package\VisualGitPackage.About.cs
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
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio;
using System.Reflection;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using SharpGit;

namespace VisualGit.VSPackage
{
    // This attribute is used to register the informations needed to show the this package
    // in the Help/About dialog of Visual Studio.
#pragma warning disable 618
    [InstalledProductRegistration(true, null, null, null)]
#pragma warning restore 618
    [VisualGit.VSPackage.Attributes.ProvideUIVersion]
    public partial class VisualGitPackage : IVsInstalledProduct
    {
        Version _uiVersion, _packageVersion;
        /// <summary>
        /// Gets the UI version. Retrieved from the registry after being installed by our MSI
        /// </summary>
        /// <value>The UI version.</value>
        public Version UIVersion
        {
            get { return _uiVersion ?? (_uiVersion = GetUIVersion() ?? PackageVersion); }
        }

        /// <summary>
        /// Gets the UI version (as might be remapped by the MSI)
        /// </summary>
        /// <returns></returns>
        private Version GetUIVersion()
        {
            // We can't use our services here to help us :(
            // This code might be used from devenv.exe /setup

            ILocalRegistry3 lr = GetService<ILocalRegistry3>(typeof(SLocalRegistry));

            if(lr == null)
                return null;

            string root;
            if (!ErrorHandler.Succeeded(lr.GetLocalRegistryRoot(out root)))
                return null;
            
            RegistryKey baseKey = Registry.LocalMachine;

            // Simple hack via 2005 api to support 2008+ RANU cases.
            if (root.EndsWith("\\UserSettings"))
            {
                root = root.Substring(0, root.Length - 13) + "\\Configuration";
                baseKey = Registry.CurrentUser;
            }

            using (RegistryKey rk = baseKey.OpenSubKey(root + "\\Packages\\" + typeof(VisualGitPackage).GUID.ToString("b"), RegistryKeyPermissionCheck.ReadSubTree))
            {
                if(rk == null)
                    return null;

                string v = rk.GetValue(VisualGit.VSPackage.Attributes.ProvideUIVersionAttribute.RemapName) as string;

                if(string.IsNullOrEmpty(v))
                    return null;

                if(v.IndexOf('[') >= 0)
                    return null; // When not using remapping we can expect "[ProductVersion]" as value

                try
                {
                    return new Version(v);
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets the package version. The assembly version of VisualGit.Package.dll
        /// </summary>
        /// <value>The package version.</value>
        public Version PackageVersion
        {
            get { return _packageVersion ?? (_packageVersion = typeof(VisualGitPackage).Assembly.GetName().Version); }
        }

        #region IVsInstalledProduct Members

        public int IdBmpSplash(out uint pIdBmp)
        {
            pIdBmp = 0; // Not used by VS2005+
            return VSConstants.E_NOTIMPL;
        }

        public int IdIcoLogoForAboutbox(out uint pIdIco)
        {
            pIdIco = 400;
            return VSConstants.S_OK;
        }

        public int OfficialName(out string pbstrName)
        {
            if (InCommandLineMode)
            {
                // We are running in /setup. The text is cached for the about box
                pbstrName = Resources.AboutTitleNameShort;
            }
            else
            {
                // We are running with full UI. Probably used for the about box
                pbstrName = Resources.AboutTitleName;
            }
            return VSConstants.S_OK;
        }

        public int ProductDetails(out string pbstrProductDetails)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(Resources.AboutDetails,
                UIVersion.ToString(),
                PackageVersion.ToString(),
                GitClient.SharpGitVersion,
                GitClient.NGitVersion
            );

            sb.AppendLine();
            sb.AppendLine();
            sb.Append(Resources.AboutLinkedTo);

            bool hadOne = false;
            foreach (SharpGit.Implementation.GitLibrary lib in GitClient.GitLibraries)
            {
                if (hadOne)
                    sb.Append(", ");
                else
                    hadOne = true;

                sb.AppendFormat("{0} {1}", lib.Name, lib.VersionString);
                
            }

            sb.AppendLine();

            pbstrProductDetails = sb.ToString();

            return VSConstants.S_OK;
        }

        public int ProductID(out string pbstrPID)
        {
            pbstrPID = UIVersion.ToString();

            return VSConstants.S_OK;
        }

        #endregion
    }
}
