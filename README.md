<p align="center"><img src="assets/logo.svg" alt="DSNForge" width="800"></p>

<p align="center">
  <a href="https://github.com/ParhamDRP1/DBForge/actions/workflows/build.yml"><img src="https://github.com/ParhamDRP1/DBForge/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <a href="https://github.com/ParhamDRP1/DBForge/releases"><img src="https://img.shields.io/github/v/release/ParhamDRP1/DBForge" alt="Release"></a>
  <img src="https://img.shields.io/badge/.NET_Framework-4.8-512BD4" alt=".NET Framework 4.8">
  <img src="https://img.shields.io/badge/platform-Windows_x64-0078D4" alt="Windows x64">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-3ed6bd" alt="MIT license"></a>
</p>

**English** · [فارسی](README.fa.md) · [User guide](docs/USER-GUIDE.md) · [Library API](docs/LIBRARY.md) · [CLI](docs/CLI.md) · [Change history](CHANGELOG.md)

# DSNForge

**A Windows workbench and reusable .NET library for IBM Db2 for z/OS data movement.**

DSNForge turns `DSNUTILU` control statements, MVS datasets and FTP transfers into saved, repeatable projects. Load local files, unload tables, or copy data between compatible tables on the same server. Inspect the generated utility cards, validate their syntax, follow transfer progress, and retain a report for every run.

The application is named **DSNForge**; its GitHub repository is **DBForge**.

## Three interfaces, one execution engine

| Component | For | Release asset |
|---|---|---|
| **Desktop GUI** | Interactive project editing, validation and execution | `DSNForge-1.2.1-GUI-win-x64.zip` |
| **Core library** | Integrating data movement into existing .NET Framework applications | `DSNForge-1.2.1-Library-net48.zip`, `DSNForge.Core.1.2.1.nupkg` |
| **CLI** | Scripts, scheduled jobs and terminal workflows | `DSNForge-1.2.1-CLI-win-x64.zip` |
| **Windows installer** | Per-user installation with shortcuts and uninstall support | `DSNForge-1.2.1-Setup-win-x64.exe` |

Get the packages from [Releases](https://github.com/ParhamDRP1/DBForge/releases). The NuGet package is distributed as a release asset; publication to nuget.org is not implied.

## What it does

New in 1.2: [task/run wizards, saved connection profiles, encrypted credentials, metadata and protected ZIP](docs/WIZARDS.md).

- **LOAD:** packed UTF-8 CSV, native files, existing datasets, and generated LOAD cards.
- **UNLOAD:** native or delimited output, data download, and generated `SYSPUNCH` LOAD cards.
- **Server-side transfer:** UNLOAD followed by LOAD into a compatible target table. The data dataset stays on the mainframe, avoiding a client-side data round trip.
- **Saved projects:** ordered, multi-table workflows stored as JSON. Credentials use environment variables, Windows-encrypted Manual storage, or runtime prompts.
- **Dataset management:** allocation, record format, record length, block size, space settings, unique run/batch names, and optional deletion after success.
- **FTP controls:** binary/ASCII/EBCDIC representations, passive/active connections, explicit TLS, buffer size, timeout, retries, and server `SITE` options.
- **Validation:** local configuration checks and server-side `DSNUTILU PREVIEW`.
- **Observability:** timestamped messages, byte progress, upload speed and ETA, five-second heartbeats, return codes, utility IDs, and JSON/CSV/HTML reports.
- **Expert controls:** editable complete utility statements and additional TEMPLATE/SITE options. Available syntax depends on the host's Db2 and FTP versions.

![DSNForge desktop](assets/desktop.png)

## Quick start

1. Install the setup package or extract the GUI ZIP.
2. Start `DSNForge.Desktop.exe`, open **Connections → Manage connection profiles**, then use **New task wizard**. See the [wizard guide](docs/WIZARDS.md).
3. Add a LOAD, UNLOAD or TRANSFER task; choose its table, files and dataset policy.
4. For text CSV input, define the column layout or use **Read table layout**.
5. Open **Validate / preview**, then **Run wizard**.
6. Inspect the modal progress/error page and save the final text report.

All defaults and examples use placeholders. The wizard can create a missing target when explicitly enabled with reviewed DDL. Start with a non-production table and a small representative file.

## Requirements

- Windows x64 with **.NET Framework 4.8**.
- For live DB2 operations: an installed x64 DB2 provider (Microsoft Host Integration Server, OLE DB, ODBC or compatible ADO.NET driver). See [provider profiles](docs/WIZARDS.md#connection-profiles).
- A Db2 for z/OS host with `SYSPROC.DSNUTILU` configured and appropriate utility privileges.
- FTP access to the required MVS datasets for upload/download tasks.

The proprietary provider is **not redistributed**. Project editing, local validation, builds and offline tests work without it. Live calls load the installed provider at runtime; `DSNFORGE_DB2_PROVIDER` can point to its installed DLL. See [deployment details](docs/USER-GUIDE.md#requirements-and-provider-discovery).

## Automation

```powershell
DSNForge.Cli.exe new project.json
DSNForge.Cli.exe validate project.json
# Set DSNFORGE_DB2_PASSWORD and DSNFORGE_FTP_PASSWORD through your secret mechanism.
DSNForge.Cli.exe preview project.json
DSNForge.Cli.exe run project.json
```

```csharp
var project = ProjectStore.Load("project.json");
var engine = new ProjectRunner();
engine.Progress += e => Console.WriteLine($"{e.Time:O} {e.Level} {e.Message}");
var report = await engine.RunAsync(
    project, new RunOptions(), preview: false, cancellationToken);
```

See the complete [library integration guide](docs/LIBRARY.md) and [CLI reference](docs/CLI.md).

## Build and release

```powershell
git clone https://github.com/ParhamDRP1/DBForge.git
cd DBForge
.\build.ps1
.\scripts\package.ps1 -InstallCompiler
```

Builds use the .NET SDK and the .NET Framework 4.8 targeting pack. No database credentials or proprietary assemblies are needed for compilation or offline tests. Packaging creates separate component archives, a NuGet package, an Inno Setup installer, checksums, and a source/build manifest.

The [GitHub Actions workflow](.github/workflows/build.yml) checks out the repository source, builds, tests, packages and attaches artifacts to version-tagged releases. [Release 1.2.1 notes](docs/releases/1.2.1.md) and [CHANGELOG.md](CHANGELOG.md) record the version history. Installers and application binaries are currently **unsigned**.

## Tested behavior and limits

Development integration testing on a Db2 10 for z/OS host covered PREVIEW, a 10,000-row UNLOAD, a server-side transfer, dataset cleanup, and CSV LOAD in two 5,000-row batches. All 21 fields of the test records were compared with the source, including Persian text. These are functional checks, not universal throughput benchmarks.

- Jobs execute sequentially; each task selects a DB2/FTP connection profile.
- CSV packing supports text, external numeric/date/time fields and explicit NULL markers. Advanced native types use generated cards; see [tested limits](docs/WIZARDS.md).
- `BatchRecords` applies to LOAD. UNLOAD runs once per table/task.
- DSNUTILU does not stream row-by-row progress through this interface. A heartbeat is not a completed-row estimate.
- Cancelling a client request does **not** prove that the server utility stopped or rolled back.
- An uncertain utility outcome is recorded with its utility ID and is **never automatically replayed**. Saved projects are configuration files, not automatic crash-recovery checkpoints.
- Active FTP, explicit TLS and EBCDIC representation are configurable but have not been live-tested in the development environment. SFTP is not implemented.

See [operational details](docs/USER-GUIDE.md) before using native formats or resuming work after a failure.

## References

- IBM: [DSNUTILU stored procedure](https://www.ibm.com/docs/en/db2-for-zos/12.0.0?topic=db2-dsnutilu)
- IBM: [LOAD utility](https://www.ibm.com/docs/en/db2-for-zos/13.0.0?topic=utilities-load)
- IBM: [UNLOAD utility](https://www.ibm.com/docs/en/db2-for-zos/12.0.0?topic=utilities-unload)
- IBM: [Generated LOAD statements](https://www.ibm.com/docs/en/db2-for-zos/12.0.0?topic=unload-load-statements-that-are-generated-by)
- IBM: [Variable-blocked binary FTP transfers](https://www.ibm.com/support/pages/using-ftp-transfer-variable-blocked-file-binary-data)
- Microsoft: [Host Integration Server](https://learn.microsoft.com/en-us/host-integration-server/)
- [Inno Setup](https://jrsoftware.org/isinfo.php)

Use the documentation matching your host version; the references above span multiple Db2 releases. DSNForge is an independent project by [ParhamDRP1](https://github.com/ParhamDRP1), not affiliated with or endorsed by IBM or Microsoft. Their product names remain their respective trademarks.

## License and contributions

[MIT License](LICENSE). Bug reports and pull requests are welcome; read [CONTRIBUTING.md](CONTRIBUTING.md). Do not attach customer data, credentials or unredacted production logs to public issues. See [SECURITY.md](SECURITY.md) for private vulnerability reporting.
