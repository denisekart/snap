using System;

namespace Snap.Core
{
    [Flags]
    public enum Targets
    {
        Pack = 1,
        Unpack = 2,
        Clean = 4,
        Help = 8
    }
}