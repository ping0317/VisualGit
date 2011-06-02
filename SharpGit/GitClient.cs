﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace SharpGit
{
    public class GitClient : IDisposable
    {
        private static readonly Dictionary<string, Version> _clients = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        internal GitUIBindArgs BindArgs { get; set; }

        public bool IsCommandRunning { get; private set; }

        public bool IsDisposed
        {
            get { return _disposed; }
        }

        public bool Status(string path, GitStatusArgs args, EventHandler<GitStatusEventArgs> callback)
        {
            if (path == null)
                throw new ArgumentNullException("path");
            if (args == null)
                throw new ArgumentNullException("args");
            if (callback == null)
                throw new ArgumentNullException("callback");

#if DEBUG
            // We cheat here to aid debugging.

            if (!args.ThrowOnError && !RepositoryUtil.IsBelowManagedPath(path))
            {
                args.SetError(new GitNoRepositoryException());
                return false;
            }
#endif
            return ExecuteCommand<GitStatusCommand>(args, p => p.Execute(path, callback));
        }

        public bool Delete(string path, GitDeleteArgs args)
        {
            if (path == null)
                throw new ArgumentNullException("path");
            if (args == null)
                throw new ArgumentNullException("args");

            return ExecuteCommand<GitDeleteCommand>(args, p => p.Execute(path));
        }
        
        public bool Revert(IEnumerable<string> paths, GitRevertArgs args)
        {
            if (paths == null)
                throw new ArgumentNullException("paths");
            if (args == null)
                throw new ArgumentNullException("args");

            return ExecuteCommand<GitRevertCommand>(args, p => p.Execute(paths));
        }

        public bool Add(string path, GitAddArgs args)
        {
            if (path == null)
                throw new ArgumentNullException("path");
            if (args == null)
                throw new ArgumentNullException("args");

            return ExecuteCommand<GitAddCommand>(args, p => p.Execute(path));
        }

        public bool Commit(IEnumerable<string> paths, GitCommitArgs args, out GitCommitResult result)
        {
            if (paths == null)
                throw new ArgumentNullException("paths");
            if (args == null)
                throw new ArgumentNullException("args");

            return ExecuteCommand<GitCommitCommand, GitCommitResult>(args, p => p.Execute(paths), out result);
        }

        public bool Write(GitTarget path, Stream stream, GitWriteArgs args)
        {
            if (path == null)
                throw new ArgumentNullException("path");
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (args == null)
                throw new ArgumentNullException("args");

            return ExecuteCommand<GitWriteCommand>(args, p => p.Execute(path, stream));
        }

        private bool ExecuteCommand<T>(GitClientArgs args, Action<T> action)
            where T : GitCommand
        {
            try
            {
                IsCommandRunning = true;

                T command = (T)Activator.CreateInstance(typeof(T), new object[] { this, args });

                action(command);

                return true;
            }
            catch (GitException ex)
            {
                args.SetError(ex);

                if (args.ShouldThrow(ex.ErrorCode))
                    throw;

                return false;
            }
            finally
            {
                IsCommandRunning = false;
            }
        }

        private bool ExecuteCommand<TCommand, TResult>(GitClientArgs args, Func<TCommand, TResult> action, out TResult result)
            where TCommand : GitCommand
            where TResult : GitCommandResult
        {
            try
            {
                IsCommandRunning = true;

                TCommand command = (TCommand)Activator.CreateInstance(typeof(TCommand), new object[] { this, args });

                result = action(command);

                return true;
            }
            catch (GitException ex)
            {
                args.SetError(ex);

                if (args.ShouldThrow(ex.ErrorCode))
                    throw;

                result = null;

                return false;
            }
            finally
            {
                IsCommandRunning = false;
            }
        }

        public event EventHandler<GitNotifyEventArgs> Notify;
        public event EventHandler<GitCommittingEventArgs> Committing;

        internal protected virtual void OnNotify(GitNotifyEventArgs e)
        {
            var ev = Notify;

            if (ev != null)
                ev(this, e);
        }

        internal protected virtual void OnCommitting(GitCommittingEventArgs e)
        {
            var ev = Committing;

            if (ev != null)
                ev(this, e);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                _disposed = true;
            }
        }

        public static void AddClientName(string client, Version version)
        {
            lock (_clients)
            {
                _clients.Add(client, version);
            }
        }
    }
}
