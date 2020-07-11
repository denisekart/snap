using System;
using System.Collections.Generic;
using System.Text;

namespace Snap.Core.Runners
{
    [CoreRunner]
    public class SnapTargetRunner : ITargetRunner
    {
        public string Type => "snap";
        public void Pack(SnapConfiguration.Target target)
        {
            throw new NotImplementedException();
        }

        public void Restore(SnapConfiguration.Target target)
        {
            throw new NotImplementedException();
        }

        public void Clean(SnapConfiguration.Target target)
        {
            throw new NotImplementedException();
        }
    }
}
