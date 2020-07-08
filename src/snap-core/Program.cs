using System;
using Snap.Core;

namespace snap_core
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                SnapRunner.Create().Run();
            }
            catch (SnapException e)
            {
                if (!e.Handled)
                    throw;

                Environment.Exit(-1);
            }
        }
    }
}
