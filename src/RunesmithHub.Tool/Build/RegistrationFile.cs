using System.Text.Json;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Registry;

namespace RunesmithHub.Tool.Build;

/// <summary>Reads a plugin's <c>registration.json</c>.</summary>
internal static class RegistrationFile
{
    /// <exception cref="ToolException">The file is not a valid registration.</exception>
    public static Registration Read(string path)
    {
        try
        {
            return RegistryJson.Deserialize<Registration>(File.ReadAllBytes(path));
        }
        catch (JsonException exception)
        {
            throw new ToolException($"{path} is not a valid registration: {exception.Message}", exception);
        }
    }
}
