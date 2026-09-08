# RagnaModManager 1.2.0 roadmap

This is the local tracking list for the user-facing improvements identified in
the mod-manager review. Multiple game installations are intentionally excluded.

## Planned improvements

- [x] Make dependency problems actionable: install, update, or enable the
  required dependency from the Mods screen.
- [x] Add bulk mod operations: select all/none, enable or disable selected,
  remove selected, and update selected/all official mods.
- [x] Add search, filtering, and useful sorting to the Library and Mods views.
- [x] Make update state obvious with a dashboard/library badge, last-checked
  timestamp, stale/offline state, and an Update All action.
- [x] Show richer mod details: dependencies, conflicts, compatibility,
  package size, source/license metadata, and release notes where available.
- [x] Support drag-and-drop `.rmod` import.
- [x] Add profile export/import for sharing and backup.
- [x] Add a deployment preview showing additions, removals, conflicts, and a
  visible rollback/reset recovery path.
- [x] Add launch options: saved arguments and launching directly with the
  active profile.
- [x] Make missing profile mods explicit and provide repair/removal actions.
- [x] Use semantic-version ordering, including prerelease versions.
- [x] Restore official-library actions after failed downloads without requiring
  a full page refresh.

## Explicitly out of scope for 1.2.0

- Supporting multiple Ragnarock game installations or game versions.
