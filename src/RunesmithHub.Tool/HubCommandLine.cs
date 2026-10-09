using System.CommandLine;
using RunesmithHub.Tool.Commands;

namespace RunesmithHub.Tool;

/// <summary>Builds the <c>rshub</c> command line from its command groups.</summary>
internal static class HubCommandLine
{
    public static RootCommand Create(ToolContext context)
    {
        var root = new RootCommand("The Runesmith Plugin Hub tool.");
        root.Add(KeysCommands.Create(context));
        root.Add(DevCommands.Create(context));
        root.Add(CheckCommands.Create(context));
        root.Add(BuildCommands.Create(context));
        root.Add(AnalyseCommands.Create(context));
        root.Add(FindingsCommands.Create(context));
        root.Add(RegistrationCommands.Create(context));
        root.Add(DiscoverCommand.Create(context));
        root.Add(VersionCommands.Create(context));
        root.Add(GateCommands.Create(context));
        root.Add(IndexCommands.Create(context));
        root.Add(PublishCommands.Create(context));
        root.Add(AdminCommandCommand.Create(context));
        root.Add(DeveloperCommand.Create(context));
        root.Add(AuditCommand.Create(context));
        root.Add(DashboardCommand.Create(context));
        return root;
    }
}
