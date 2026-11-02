using System.Diagnostics;

namespace AdvancedLoader
{
    internal static class Diag
    {
        [Conditional("DEBUG")]
        public static void Log(string message)
        {
            AdvancedLoaderPlugin.Logger.LogInfo("[diag] " + message);
        }
    }
}
