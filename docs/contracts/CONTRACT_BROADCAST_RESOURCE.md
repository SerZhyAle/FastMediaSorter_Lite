# Pointer - `BROADCAST-RESOURCE`

| | |
| --- | --- |
| **Id** | `BROADCAST-RESOURCE` |
| **Version** | 1.0, active (2026-10-03). Owner: StreamsPlayer / FastMediaSorter cross-product contract |
| **Home** | `docs/specifications/016_SPECIFICATION_STREAMSPLAYER_BROADCAST_RESOURCE.md` |
| **Role here** | **producer** - `src/FastMediaSorterCompanion/Core/BroadcastConfigBuilder.vb` builds the `.spres`, QR code and clipboard quick link |

## Contract (schemaVersion 1)

```json
{
  "schemaVersion": 1,
  "kind": "broadcast",
  "resourceName": "Dune (2021)",
  "protocol": "http",
  "mediaKind": "video",
  "playlist": false,
  "itemCount": 1,
  "accessPaths": [
    { "kind": "lan",         "host": "192.168.1.100", "port": 55272 },
    { "kind": "ipv6",        "host": "2a02:...",      "port": 55272 },
    { "kind": "portforward", "host": "46.54.0.135",   "port": 55272 }
  ],
  "path": "/b/hT9x..",
  "durationSeconds": 8520,
  "expiresAt": "2026-08-09T21:00:00Z",
  "accessNote": "...",
  "createdAt": "2026-08-08T21:00:00Z"
}
```

### Transport variants

- **`.spres` file**: plain UTF-8 JSON without BOM.
- **QR code**: plain JSON when length <= 900 bytes, else `SPRES1:` + Base64(GZip(JSON)).
- **Clipboard quick link**: `SPCH1 http://<best-host>:<port>/b/<token>`

## Rules

1. **The URL is composed, never transmitted**: `http://{host}:{port}{path}`, with IPv6 bracketed (`http://[{host}]:{port}{path}`).
2. **Ordering is advice; racing is the contract.** Importers must race all access paths in parallel.
3. **Unknown fields are ignored, unknown accessPath kinds are skipped.**
4. **Transport compression**: `SPRES1:` prefix indicates base64-encoded gzip of JSON for QR payloads exceeding 900 bytes.
5. **Clipboard quick link is lossy by design**: `SPCH1 http://<best-host>:<port>/b/<token>` carries only one address and path for immediate playback.
