using System.Xml.Linq;
using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class StartupTaskTests
{
    static readonly XNamespace Task = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    static XDocument Build(string command = @"C:\Tools\SteamRouteTool.exe", string arguments = StartupTask.MinimizedArgument, string userId = @"PC\gamer")
    {
        return XDocument.Parse(StartupTask.BuildTaskXml(command, arguments, userId));
    }

    [Fact]
    public void BuildTaskXml_RunsTheAppElevatedWhenTheUserSignsIn()
    {
        XDocument task = Build();

        Assert.Equal(@"PC\gamer", task.Descendants(Task + "LogonTrigger").Single().Element(Task + "UserId")!.Value);
        XElement principal = task.Descendants(Task + "Principal").Single();
        Assert.Equal(@"PC\gamer", principal.Element(Task + "UserId")!.Value);
        Assert.Equal("InteractiveToken", principal.Element(Task + "LogonType")!.Value);
        Assert.Equal("HighestAvailable", principal.Element(Task + "RunLevel")!.Value);
        Assert.Equal("--minimized", task.Descendants(Task + "Arguments").Single().Value);
    }

    [Fact]
    public void BuildTaskXml_KeepsRunningOnBatteryAndWithoutATimeLimit()
    {
        XElement settings = Build().Descendants(Task + "Settings").Single();

        Assert.Equal("false", settings.Element(Task + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("false", settings.Element(Task + "StopIfGoingOnBatteries")!.Value);
        Assert.Equal("PT0S", settings.Element(Task + "ExecutionTimeLimit")!.Value);
    }

    [Fact]
    public void BuildTaskXml_EscapesPathsAndUserNames()
    {
        XDocument task = Build(command: @"C:\Games & Tools\<SteamRouteTool>.exe", userId: @"PC\Tom & Jerry");

        Assert.Equal(@"C:\Games & Tools\<SteamRouteTool>.exe", task.Descendants(Task + "Command").Single().Value);
        Assert.Equal(@"PC\Tom & Jerry", task.Descendants(Task + "Principal").Single().Element(Task + "UserId")!.Value);
    }

    [Fact]
    public void ParseCommand_ReadsTheCommandOfARegisteredTask()
    {
        string xml = StartupTask.BuildTaskXml(@"D:\SteamRouteTool.exe", StartupTask.MinimizedArgument, @"PC\gamer");

        Assert.Equal(@"D:\SteamRouteTool.exe", StartupTask.ParseCommand(xml));
    }

    [Fact]
    public void ParseCommand_ReturnsNullWithoutAnAction()
    {
        Assert.Null(StartupTask.ParseCommand("""<Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><Actions /></Task>"""));
    }
}
