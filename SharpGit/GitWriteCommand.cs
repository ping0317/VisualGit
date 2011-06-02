﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using NGit;
using NGit.Revwalk;
using NGit.Treewalk;

namespace SharpGit
{
    internal sealed class GitWriteCommand : GitCommand<GitWriteArgs>
    {
        public GitWriteCommand(GitClient client, GitClientArgs args)
            : base(client, args)
        {
        }

        public void Execute(GitTarget path, Stream stream)
        {
            if (path == null)
                throw new ArgumentNullException("path");
            if (stream == null)
                throw new ArgumentNullException("stream");

            var pathTarget = path as GitPathTarget;

            if (pathTarget == null)
                throw new NotImplementedException();

            var repositoryEntry = RepositoryManager.GetRepository(pathTarget.FullPath);

            if (repositoryEntry == null)
                throw new GitNoRepositoryException();

            lock (repositoryEntry.SyncLock)
            {
                var repository = repositoryEntry.Repository;

                if (Args.Revision == GitRevision.Working)
                    WriteWorkingRevision(repository, stream, pathTarget.FullPath);
                else if (Args.Revision == GitRevision.Base)
                    WriteBaseRevision(repository, stream, pathTarget.FullPath);
                else if (Args.Revision.RevisionType == GitRevisionType.Hash)
                    WriteSpecificRevision(repository, stream, pathTarget.FullPath, Args.Revision.Revision);
                else
                    throw new NotImplementedException();
            }
        }

        private void WriteSpecificRevision(Repository repository, Stream stream, string path, string revision)
        {
            var objectId = repository.Resolve(revision);

            if (objectId == null)
                throw new GitException(GitErrorCode.RevisionNotFound);

            string relativePath = repository.GetRepositoryPath(path);

            var objectReader = repository.NewObjectReader();

            try
            {
                RevWalk revWalk = new RevWalk(repository);

                try
                {
                    TreeWalk startWalk = new TreeWalk(revWalk.GetObjectReader());

                    try
                    {
                        startWalk.AddTree(revWalk.ParseCommit(objectId).Tree);

                        bool found = false;

                        while (startWalk.Next())
                        {
                            if (String.Equals(startWalk.PathString, relativePath, FileSystemUtil.StringComparison))
                            {
                                LoadObject(objectReader, startWalk.GetObjectId(0), stream);

                                found = true;
                                break;
                            }
                        }

                        if (!found)
                            throw new GitException(GitErrorCode.CouldNotFindPathInRevision);
                    }
                    finally
                    {
                        startWalk.Release();
                    }
                }
                finally
                {
                    revWalk.Release();
                }
            }
            finally
            {
                objectReader.Release();
            }
        }

        private void LoadObject(ObjectReader objectReader, ObjectId pathObjectId, Stream stream)
        {
            var loader = objectReader.Open(pathObjectId);

            using (var inStream = new ObjectStreamWrapper(loader.OpenStream()))
            {
                inStream.CopyTo(stream);
            }
        }

        private void WriteWorkingRevision(Repository repository, Stream stream, string path)
        {
            using (var inStream = File.OpenRead(path))
            {
                inStream.CopyTo(stream);
            }
        }

        private void WriteBaseRevision(Repository repository, Stream stream, string path)
        {
            var dirCache = repository.ReadDirCache();
            var objectReader = repository.NewObjectReader();

            try
            {
                string relativePath = repository.GetRepositoryPath(path);

                var entryIndex = dirCache.FindEntry(relativePath);

                if (entryIndex >= 0)
                {
                    // When it is in the disk cache, we need to overwrite
                    // the current contents with that of the disk cache.

                    var entry = dirCache.GetEntry(entryIndex);

                    LoadObject(objectReader, entry.GetObjectId(), stream);
                }
            }
            finally
            {
                objectReader.Release();
            }
        }
    }
}
