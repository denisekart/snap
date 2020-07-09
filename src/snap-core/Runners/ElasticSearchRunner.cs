using Nest;
using System;

namespace Snap.Core.Runners
{
    public class ElasticSearchRunner : ITargetRunner
    {
        public string Type => "elasticsearch";

        public void Pack(SnapConfiguration.Target target)
        {
            var elastic = new ElasticClient(new Uri(target.GetHostProperty()));
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
