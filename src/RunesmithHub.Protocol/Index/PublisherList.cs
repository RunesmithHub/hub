namespace RunesmithHub.Protocol.Index;

/// <summary>Every publisher with its owner, state and verification. Pinned by the snapshot.</summary>
public sealed record PublisherList : IndexDocument
{
    public required IReadOnlyList<PublisherEntry> Publishers { get; init; }

    public PublisherEntry? Find(string name) => Publishers.FirstOrDefault(publisher => string.Equals(publisher.Name, name, StringComparison.Ordinal));
}
