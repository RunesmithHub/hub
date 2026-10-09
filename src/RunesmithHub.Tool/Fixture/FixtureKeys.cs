using RunesmithHub.Protocol.Signing;
using RunesmithHub.Tool.Keys;

namespace RunesmithHub.Tool.Fixture;

/// <summary>Throwaway keys for a fixture: three root keys and one key for each online role.</summary>
internal sealed class FixtureKeys : IDisposable
{
    public const string Folder = "keys";

    private const string Readme = """
        These are throwaway test keys written by rshub dev fixture, unencrypted on purpose.
        They sign only the sample index next to this folder. Never use them for a real index.

        """;

    public EcdsaSigner[] Root { get; } = [EcdsaSigner.Generate(), EcdsaSigner.Generate(), EcdsaSigner.Generate()];

    public EcdsaSigner Publish { get; } = EcdsaSigner.Generate();

    public EcdsaSigner Moderation { get; } = EcdsaSigner.Generate();

    public EcdsaSigner Timestamp { get; } = EcdsaSigner.Generate();

    /// <summary>Writes every key as <c>test-&lt;role&gt;.pem</c> with its public key beside it, and a note saying what they are.</summary>
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "README.txt"), Readme);
        for (var i = 0; i < Root.Length; i++)
            Save(directory, $"test-root-{i + 1}", Root[i]);
        Save(directory, "test-publish", Publish);
        Save(directory, "test-moderation", Moderation);
        Save(directory, "test-timestamp", Timestamp);
    }

    private static void Save(string directory, string name, EcdsaSigner key)
    {
        var path = Path.Combine(directory, name + ".pem");
        KeyFiles.WritePrivateKey(path, key.ExportPem());
        KeyFiles.WritePublicKey(KeyFiles.PublicPathFor(path), key.PublicKey);
    }

    public void Dispose()
    {
        foreach (var key in Root.Append(Publish).Append(Moderation).Append(Timestamp))
            key.Dispose();
    }
}
