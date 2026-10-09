namespace RunesmithHub.Protocol.Resolution;

/// <summary>What the solver knows about packages: their versions, facts and preferences.</summary>
internal interface IPackageSource
{
    string RootPackage { get; }

    /// <summary>Gets how many versions a package has; the root has one.</summary>
    int VersionCount(string package);

    /// <summary>Gets the facts about a package itself, such as versions that can't be chosen; asked once per package.</summary>
    IEnumerable<Incompatibility> Facts(string package);

    /// <summary>Gets the dependencies of one version, including the root's requirements.</summary>
    IEnumerable<Incompatibility> Dependencies(string package, int version);

    /// <summary>Picks the preferred version among those still allowed.</summary>
    int Choose(string package, VersionSet allowed);

    /// <summary>Orders packages for deciding; lower decides first.</summary>
    int Priority(string package);
}
