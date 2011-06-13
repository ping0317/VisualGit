﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpGit
{
    public class GitRemoteRefsArgs : GitTransportClientArgs
    {
        public GitRemoteRefsArgs()
            : base(GitCommandType.RemoteRefs)
        {
        }
    }
}