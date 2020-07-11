using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.SqlServer.Management.Facets;

namespace Snap.Core.Runners
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal class CoreRunnerAttribute  : Attribute
    {
    }
}
