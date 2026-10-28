using System.Diagnostics;

namespace SteamRouteTool.Services;

public static class Shell
{
    /// <summary>Opens a URL, folder or program the way Explorer would. Failures (e.g. no browser registered) are ignored.</summary>
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { }
    }
}
