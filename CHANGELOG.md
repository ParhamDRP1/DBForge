# Change history

Dates use ISO 8601. Source version `1.1.0` corresponds to release **1.1** / tag `v1.1`.

## [1.2.0] — 2026-10-20

- Add modal task/run wizards and separate connection-profile management.
- Store independent DB2/FTP secrets via environment, Windows encryption or runtime prompts.
- Support per-task profiles, cloning, runtime overrides and encrypted Remember.
- Add catalog/SELECT layouts, editable dbmeta DDL and preservation of utility cards.
- Add streaming CSV export, field encoding/trim/selection and NULL-aware LOAD packing.
- Add AES ZIP, automatic detection, password retry and traversal-safe extraction.
- Add ODBC/OLE DB/registered/custom provider adapters and a profile-aware library/CLI runner.
- Export confirmed row statistics, rates, elapsed time and errors as text.
- Read project format 1; save new projects as format 2.

## [1.1.0] — 2026-10-20

### Added

- Public DBForge repository documentation, English default README and Persian guide.
- Separate GUI, CLI, library ZIP and NuGet release assets.
- Per-user Inno Setup installer, optional desktop shortcut, and uninstall support.
- DSNForge vector branding, multi-resolution application icon and desktop screenshot.
- GitHub Actions builds from checked-out source, automated tests, release packaging,
  checksums and build provenance manifest.
- Public contribution and security reporting guidance.

### Changed

- Load the Microsoft DB2 provider at runtime. Compilation and offline tests no longer
  require installation or redistribution of proprietary Microsoft binaries.
- Replace environment-specific source defaults and examples with public placeholders.
- Set assembly/package versions to 1.1.0 and separate deployment documentation by component.

### Verification

- Offline tests cover CSV/UTF-8 packing, record bounds, batching, persistence, generated
  cards, reports, return codes, cancellation and ambiguous utility outcomes.
- Earlier isolated host checks verified PREVIEW, UNLOAD, server-side transfer and batched
  CSV LOAD using 10,000 records / 21 fields, with exact source comparison.
- CI build and packaging results are attached to the release workflow; a configured
  runner does not connect to a database or receive production credentials.

## 0.1.0 — 2026-10-20

Initial local implementation, not a previous public GitHub release:

- Shared .NET Framework 4.8 x64 engine with Windows Forms and CLI clients.
- LOAD, UNLOAD, server-side transfer and saved multi-step JSON projects.
- DSNUTILU PREVIEW, FTP controls, dataset policies, runtime events and reports.
- Session/environment credentials and no automatic replay of uncertain LOAD outcomes.

[1.1.0]: https://github.com/ParhamDRP1/DBForge/releases/tag/v1.1
