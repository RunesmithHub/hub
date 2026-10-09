# RunesmithHub.Protocol

The formats and rules of the Runesmith Plugin Hub, shared by Runesmith, the hub tool and tests:

- Plugin IDs, semantic versions and version ranges.
- The developer manifest, the packaged manifest, registry records and the signed index payloads, with source-generated JSON.
- DSSE envelopes signed with ECDSA P-256.
- The index updater that verifies the signed index and keeps a trusted copy.
- Effective states, catalog queries and dependency resolution.
- Reading and writing `.rsplugin` packages safely.
