namespace RunesmithHub.Tool.Analysis.Detection;

/// <summary>Where a capability is used: the member that does it, and the API it uses.</summary>
/// <param name="Assembly">The assembly's file under <c>lib/</c>.</param>
/// <param name="Member">The member, such as <c>Lumen.Todo.Sync.Client.FetchAsync</c>.</param>
/// <param name="Api">The API or construct, such as <c>System.Net.Http.HttpClient.GetAsync</c>.</param>
internal sealed record CallSite(string Assembly, string Member, string Api)
{
    public override string ToString() => $"{Member} ({Assembly}) uses {Api}";
}
