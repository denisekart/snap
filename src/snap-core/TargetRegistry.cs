using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Snap.Core
{
    public class TargetRegistry
    {
        public IDictionary<string, ITargetRunner> TargetRunners { get; set; } = new ConcurrentDictionary<string, ITargetRunner>();

        public bool TryGetRunner(string key, out ITargetRunner runner)
        {
            return TargetRunners.TryGetValue(key, out runner);
        }

        public bool TryAddRunner(string key, ITargetRunner runner)
        {
            try
            {
                if (TargetRunners.ContainsKey(key))
                {
                    TargetRunners[key] = runner;
                }
                TargetRunners.Add(key, runner);
            }
            catch
            {
                return false;
            }

            return true;
        }
    }
}
