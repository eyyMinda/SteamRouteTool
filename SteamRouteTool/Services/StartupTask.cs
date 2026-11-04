using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Xml.Linq;

namespace SteamRouteTool.Services;

/// <summary>
/// Starts the app when the user signs in, through a Task Scheduler task. The app needs administrator rights, and Windows
/// skips elevated apps in the Run registry key; a task with "run with highest privileges" starts it elevated without a prompt.
/// Changing the task requires administrator rights. Methods block on schtasks.exe, so call them from a background thread.
/// </summary>
public static class StartupTask
{
    public const string TaskName = "SteamRouteTool";
    /// <summary>Passed when started at sign-in, so the app starts minimized (or in the tray).</summary>
    public const string MinimizedArgument = "--minimized";

    static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>The command that starts this app as it's running now: the exe, or the dotnet host and the dll in development.</summary>
    public static (string Command, string Arguments) GetLaunchCommand()
    {
        string processPath = Environment.ProcessPath!;
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath, "\"" + typeof(StartupTask).Assembly.Location + "\" " + MinimizedArgument);
        }
        return (processPath, MinimizedArgument);
    }

    /// <summary>The command the task runs, or null when the task doesn't exist.</summary>
    public static string? GetRegisteredCommand()
    {
        (int exitCode, string output) = RunSchtasks("/Query", "/TN", TaskName, "/XML");
        return exitCode == 0 ? ParseCommand(output) : null;
    }

    /// <summary>Creates the task, or replaces it so it starts the app from where it is now.</summary>
    public static void Enable()
    {
        (string command, string arguments) = GetLaunchCommand();
        string xmlPath = Path.Combine(Path.GetTempPath(), "SteamRouteTool-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(command, arguments, WindowsIdentity.GetCurrent().Name), System.Text.Encoding.Unicode);
            ThrowIfFailed(RunSchtasks("/Create", "/TN", TaskName, "/XML", xmlPath, "/F"));
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    public static void Disable()
    {
        if (GetRegisteredCommand() == null) { return; }
        ThrowIfFailed(RunSchtasks("/Delete", "/TN", TaskName, "/F"));
    }

    /// <summary>
    /// A task that runs <paramref name="command"/> elevated when <paramref name="userId"/> signs in. It runs only while that
    /// user is signed in, on battery too, and without the default 3-day time limit.
    /// </summary>
    internal static string BuildTaskXml(string command, string arguments, string userId)
    {
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts Steam Route Tool when you sign in. Turn it off in the app's Settings.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(userId)}</UserId>
                  <Delay>PT5S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(userId)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(command)}</Command>
                  <Arguments>{SecurityElement.Escape(arguments)}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    /// <summary>The command of the task's first action, or null if the XML has none.</summary>
    internal static string? ParseCommand(string taskXml)
    {
        return XDocument.Parse(taskXml).Descendants(TaskNamespace + "Command").FirstOrDefault()?.Value;
    }

    static (int ExitCode, string Output) RunSchtasks(params string[] arguments)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) { startInfo.ArgumentList.Add(argument); }

        using Process process = Process.Start(startInfo)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, process.ExitCode == 0 ? output : error.Result.Trim());
    }

    static void ThrowIfFailed((int ExitCode, string Output) result)
    {
        if (result.ExitCode != 0) { throw new InvalidOperationException(result.Output); }
    }
}
