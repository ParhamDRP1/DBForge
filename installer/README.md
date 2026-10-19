# Installer project

`DSNForge.iss` is the Inno Setup source. It creates a per-user Windows x64 installer
with GUI/CLI/library components, documentation, optional desktop shortcut and an
uninstaller. It checks for .NET Framework 4.8; it does not install or redistribute the
proprietary Microsoft DB2 provider.

Build through `scripts/package.ps1 -InstallCompiler`, or point `ISCC_PATH` to an
existing Inno Setup compiler. The package script downloads a pinned official compiler
installer, verifies its SHA-256 and installs it under ignored build tools when needed.

The produced installer is unsigned. Uninstall removes installed program files, not
external projects, reports or datasets.
