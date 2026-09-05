# Contributing to the official registry

Please use the Mod request Issue template for a new mod. Maintainers will
convert an approved request into a catalog pull request. Authors may also open
a pull request directly when they provide the complete release URL, manifest
details, test matrix, license information, and SHA-256 checksum.

Keep catalog changes small: one mod or release family per pull request. Do not
change a published checksum or repoint an existing version to a different
asset. Publish a new version instead.

Before merging, a maintainer must complete the checklist in `README.md` and
confirm that `index.json` remains valid JSON and that every package URL uses
HTTPS.
