using System;

namespace Snap.Core
{
    class SnapException : Exception
    {
        public bool Handled { get; }
        public int ExitCode { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="handled">The exception was already handled upstream. Try not throwing if possible.</param>
        public SnapException(string message, bool handled = false, int exitCode = -1)
        {
            Handled = handled;
            ExitCode = exitCode;
        }
    }
}