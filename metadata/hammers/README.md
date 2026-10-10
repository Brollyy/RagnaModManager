# Hammer table metadata registry

RMM downloads one metadata archive for the exact SHA-256 of the installed `Ragnarock-WindowsNoEditor.pak`. Each archive contains the cooked `DT_Hammers` table, one stock hammer data asset used as a safe template, and `metadata.json`, which records the PAK hash, UE/PAK versions, file hashes, and existing `/Game/...` asset package paths. The archive contains only data needed to preserve the table, create custom hammer data assets, and prevent custom asset collisions; it does not contain an AES key or the full game PAK.

## Preparing metadata

For each supported game build, maintainers prepare an archive containing the build's cooked `DT_Hammers` table, a stock hammer data asset template, and the associated metadata. Keep the game PAK and local key candidate files out of this repository. The archive and RMM logs must not contain key material.

Add the reviewed archive under `builds/<lowercase-game-pak-sha256>.zip` and add a matching `builds` entry in `index.json`. RMM fetches the committed archive from the repository's raw HTTPS URL:

```json
{
  "schemaVersion": 1,
  "builds": {
    "FULL_GAME_PAK_SHA256": {
      "url": "https://raw.githubusercontent.com/Brollyy/RagnaModManager/master/metadata/hammers/builds/FULL_GAME_PAK_SHA256.zip",
      "sha256": "METADATA_ARCHIVE_SHA256"
    }
  }
}
```

After a Ragnarock update, prepare and publish a new entry. RMM blocks PAK building when the installed game hash has no matching entry and shows an update warning; it never falls back to stale table data. Player machines only download this exact-build metadata and do not need a local AES key list or PAK decryption support. The archive hash in `index.json` must match the generated ZIP byte-for-byte.
