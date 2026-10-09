# Runesmith Hub

The .NET code of the Runesmith Plugin Hub.

| Package | What it is |
| --- | --- |
| `RunesmithHub.Protocol` | A library with the hub's formats and rules: plugin IDs, semantic versions and ranges, manifests, registry records, the signed index, index verification, dependency resolution and `.rsplugin` packages. Runesmith uses it to install and update plugins |
| `RunesmithHub.Tool` | The `rshub` dotnet tool: checks plugins before release, runs the hub's build, analysis and registry automation, manages keys and writes development fixtures |

## Build and test

You need the .NET SDK version in `global.json`.

```sh
dotnet build RunesmithHub.slnx -warnaserror
dotnet format RunesmithHub.slnx --verify-no-changes
dotnet test --solution RunesmithHub.slnx
```

## Check a plugin before you release it

`rshub check` builds, packages and analyses a plugin the way the hub does when it publishes a release, and prints the report the
hub would post. Run it in the plugin's folder (the one with `plugin.json`), or in your own CI on every push:

```sh
dotnet tool install --global RunesmithHub.Tool --add-source https://nuget.runesmith.dev/index.json
rshub check
```

It restores both projects in locked mode from nuget.org and the hub feed, builds them in Release with deterministic settings, assembles
the `.rsplugin` package, and checks the manifest, images, capabilities, the boundary between contracts and implementation, the
contracts' semantic version, bundled packages, known vulnerabilities, licenses and build logic. The exit code is 1 when the release would
fail; a report that passes may still say a person will review the version, for example because it is the first one.

| Option | Does |
| --- | --- |
| `[path]` | The plugin's folder; the current folder by default |
| `--no-build` | Analyses the existing Release build instead of restoring and building again |
| `--output <dir>` | Where the package and the report go; `obj/hub-check` in the plugin by default |
| `--package-cache <dir>` | Where to look for bundled NuGet packages before downloading them; NuGet's global packages folder by default |
| `--feed <url>` | The hub feed's service index; `HUB_FEED_URL` or `https://nuget.runesmith.dev/index.json` by default |

The report is in `obj/hub-check/analysis/summary.md` and the package in `obj/hub-check/package/`.

## The `rshub` tool

```sh
dotnet run --project src/RunesmithHub.Tool -- --help
```

| Command | Does |
| --- | --- |
| `rshub check [path]` | Builds, packages and analyses a plugin as the hub does, and prints the report |
| `rshub build prepare` / `build package` / `build compare` | The hub's build steps: check the source and plan the build, assemble the package with its contracts package, images, SBOM and source archive, compare two builds |
| `rshub analyse` | The hub's analysis of a built package against the previous version |
| `rshub findings decrypt --key <file> <file>` | Decrypts review findings for administrators |
| `rshub keys generate --out <file>` | Generates a P-256 key encrypted with a passphrase, and writes its public key beside it |
| `rshub keys root init` | Creates an unsigned root from public key files |
| `rshub keys root sign <root> --key <file>` | Adds a signature to a root |
| `rshub keys root verify <directory>` | Verifies a chain of roots named `1.json`, `2.json` and so on |
| `rshub dev fixture <directory>` | Writes a complete signed sample index for development and tests, with throwaway keys beside it |
| `rshub dev moderate <directory> --block\|--freeze <target>` | Blocks or freezes a plugin, versions or a publisher in a fixture, signed again with its test keys, to test how clients react |
| `rshub registration validate --base <ref>` | Checks the publisher and registration files a registry pull request changes |
| `rshub discover` | Finds new releases of registered plugins, opens their tracking issues and writes the build matrix |
| `rshub version previous` / `version propose` | Downloads the version a new one is analysed against; writes a version record from an analysis, checks it against the published index and opens its pull request |
| `rshub gate verify\|review\|hold --pr <n>` | The required checks of registry pull requests |
| `rshub index publish\|moderate\|timestamp\|refresh --index <dir>` | Builds and signs the index in an index directory, such as a checkout of the registry's `index` branch |
| `rshub publish artifacts\|announce\|withdraw` | Releases versions to the packages repository and the hub feed, announces them, and withdraws the files of blocked versions |
| `rshub command` | Runs an administrator's command from an issue comment |
| `rshub developer` | Runs a maintainer's request from an issue form |
| `rshub audit` | The nightly audit of registered plugins |
| `rshub dashboard --index <dir>` | Writes the moderation dashboard into an index directory |

The registry commands read `GITHUB_TOKEN` for GitHub, and the signing commands read their keys from the environment.

Commands that read a passphrase ask for it at the terminal, or read it from the environment variable named by `--passphrase-env`.

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
