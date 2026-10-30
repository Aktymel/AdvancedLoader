using System.Diagnostics;

namespace AdvancedLoader
{
    // Diagnostic log for testing. Calls to it are left out of Release builds by the compiler, so it can stay in the code.
    // Info: some installs leave Warning out of LogOutput.log
    internal static class Diag
    {
        [Conditional("DEBUG")]
        public static void Log(string message)
        {
            AdvancedLoaderPlugin.Logger.LogInfo("[diag] " + message);
        }
    }
}
