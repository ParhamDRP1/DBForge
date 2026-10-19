> **Version 1.2:** See the [wizard and profile guide](WIZARDS.md) for the current GUI, `ProjectRunner`, metadata and ZIP workflows. The lower-level `TransferEngine` examples below remain supported.

# User guide

[Home](../README.md) · [راهنمای فارسی](USER-GUIDE.fa.md) · [CLI](CLI.md) · [Library](LIBRARY.md)

## Requirements and provider discovery

The applications target .NET Framework 4.8, Windows x64. For compilation, install a
.NET SDK and the .NET Framework 4.8 targeting pack; no database provider is required.

Live DB2 calls require the Microsoft Host Integration Server managed DB2 provider and
its native prerequisites, installed and licensed separately. DSNForge checks the usual
`Program Files\Microsoft Host Integration Server\System` installation directories.
Set `DSNFORGE_DB2_PROVIDER` to the installed DLL path for a nonstandard installation.
Copying the DLL alone is not a substitute for installing its native dependencies.

The server must expose `SYSPROC.DSNUTILU`. Configure the location/catalog, port,
package collection, schema, CCSID and authorization appropriate to your installation.
The sample values are placeholders and will not connect to a real server.

## Create a project

Use **New**, edit the project name/report directory, configure **Connections**, and add
tasks in the left-hand list. The arrow buttons change execution order. **Save/Open**
persists JSON settings, not passwords. Relative file paths in a project are resolved
against the project file's directory.

Enter DB2 and FTP passwords in Connections for the current session, or supply the
named environment variables. Each project has one DB2/FTP profile; use separate
projects for distinct endpoints. Tasks run sequentially.

### LOAD

| Input | Use |
|---|---|
| `CsvToUtf8Fixed` | Text CSV packed into fixed native records with two-byte VARCHAR lengths |
| `NativeFile` | A native file plus complete, matching LOAD control statements |
| `ExistingDataset` | An existing host dataset; no upload is performed |
| `GeneratedCards` | A native file with LOAD cards obtained from UNLOAD |

For CSV, select the encoding, delimiter and header policy. Define columns in source
order using **CSV layout**, or read CHAR/VARCHAR metadata with **Read table layout**.
Widths are maximum **UTF-8 bytes**, not character counts. Oversized values are rejected,
not truncated. Quoted delimiters and multiline fields are supported. Empty strings,
leading zeroes and whitespace are preserved; empty strings do not represent SQL NULL.
Blank physical CSV lines are ignored by the CSV parser.

For numeric, date, binary and nullable native data, use the matching native/generated
cards; DSNForge does not infer or convert arbitrary DB2 types from CSV.

`BatchRecords` controls rows per LOAD invocation. Zero uses one file. Batched dataset
names must contain `{batch}`. A Replace job uses REPLACE for its first batch and RESUME
YES for subsequent batches. Complete custom statements require `BatchRecords=0`;
use GeneratedCards for native batching. Native batching needs a fixed payload width.

### UNLOAD

Select the source table, native/delimited format, output path, SHRLEVEL and dataset
policy. DSNForge executes UNLOAD and retrieves the data and generated SYSPUNCH cards.
The latter are saved as `unload-<n>.load.txt` in the run report directory.

UNLOAD runs once per task/table. Use expert utility options for host-specific selection
or output behavior; LOAD batching is not a generic UNLOAD pagination feature.

### TRANSFER

A Transfer task performs UNLOAD then LOAD on the same DB2 server. The target table must
already exist and have a compatible native layout. Data stays in a host dataset; only
the generated LOAD cards are downloaded. The cards are rebound to the selected target
and dataset. A multi-table project is a sequence of these tasks.

## Dataset options

Use a fully qualified MVS name without surrounding quotes, for example
`MYUSER.DSNFORGE.{run}.{batch}`. Qualifiers are at most eight characters; the complete
name is at most 44. `{run}` identifies the run and `{batch}` becomes `B000001`, etc.
UNLOAD uses a distinct companion PUNCH dataset.

- **AutoAllocate:** create input datasets through FTP or output datasets through TEMPLATE.
- **RecordFormat/RecordLength/BlockSize:** RECFM, LRECL and BLKSIZE. CSV packing uses FB
  with a width calculated from the declared fields.
- **SpaceUnit/PrimarySpace/SecondarySpace/Unit:** host allocation controls.
- **TemplateOptions:** additional TEMPLATE keywords understood by your host.
- **DeleteAfterSuccess:** remove datasets only after dependent steps succeed.
- **AllowOverwrite:** permit overwriting an existing upload/output file where supported.

UNLOAD with automatic allocation requires new names. Preallocated UNLOAD requires both
data and PUNCH datasets plus `AllowOverwrite=true`. Failed or uncertain operations retain
their datasets. Cleanup failures are warnings in the event log.

## FTP representations and record boundaries

Binary uses `TYPE I`, ASCII `TYPE A`, and EBCDIC `TYPE E`. The latter two depend on the
server's conversion settings; they are not local file-encoding declarations. Packed
UTF-8 CSV requires binary transfer. Configure passive/active mode, explicit TLS,
timeouts, buffer size and upload retries in Connections. `SiteOptions` carries
server-specific options such as `RDW` or `SBDATACONN`.

For binary VB downloads, use `SITE RDW` when record boundaries must be retained. A
native file with four-byte RDW headers can be imported using `NativeInputHasRdw` and
the correct `NativeRecordBytes` payload width. Differing/spanned record lengths are
rejected by this fixed-record conversion; keep the original dataset for a server-side
transfer instead. See IBM's [binary VB FTP guidance](https://www.ibm.com/support/pages/using-ftp-transfer-variable-blocked-file-binary-data).

Explicit FTPS checks the certificate and uses TLS 1.2. Active FTP, EBCDIC and FTPS are
implemented but not live-tested in the development environment. SFTP is not supported.

## Expert utility cards

`UtilityOptions` adds keywords before INTO TABLE/FROM TABLE. `ControlStatements`
replaces the complete generated statements, including TEMPLATE. Use **Utility cards**
to generate, inspect and edit the text.

Tokens: `{dataset}`, `{punch}`, `{table}`, `{target}`. Supported syntax is determined
by the host utility, not a hardcoded list of GUI choices. DSNUTILU accepts up to 32,704
UTF-8 bytes. Line endings are normalized to spaces; avoid comments that depend on line
boundaries. For generated native cards, preserve their field positions and encodings.

## Validation, progress and reports

**Validate** checks local settings and relevant file existence. **DB2 Preview** invokes
DSNUTILU with `RESTART=PREVIEW`; it checks utility syntax, not every data value, dataset
permission or available disk space. For Transfer, generated destination LOAD cards
become available after UNLOAD; inspect the resulting execution report.

FTP uploads show bytes, percentage, speed and a stage ETA. Downloads show bytes; some
servers do not support SIZE, so a total/ETA may be unavailable. DSNUTILU output arrives
when the result set returns, not row by row. Five-second heartbeats keep the UI active
without pretending to know the server's row progress.

Each run creates:

| File | Contents |
|---|---|
| `events.jsonl` | Timestamped stage messages, warnings and errors |
| `report.json` | Machine-readable batch results and timings |
| `report.csv` | Tabular report |
| `report.html` | Standalone readable report |
| `<utility-id>.control.txt` | Submitted control statements |
| `<utility-id>.checkpoint.json` | Utility identity and latest recorded outcome |

FTP and utility times are separate. Counts belong to individual operations: a Transfer
processes the same data once during UNLOAD and again during LOAD, so summing those
counts is not a distinct-record total. Reports may contain infrastructure identifiers;
redact before sharing.

## Errors, cancellation and reruns

RC 0 is success. RC 4 is a configurable warning. Higher codes fail the task. Prepared
LOAD counts are checked against available SYSPRINT counts. FTP uploads may retry
before utility execution; ambiguous LOAD submissions are never replayed automatically.

**Stop** aborts the file connection and requests DB2 command cancellation. It does not
prove that the host utility stopped or rolled back. A disconnect after submission is
recorded as `OutcomeUnknown`; use the utility ID and host tools to reconcile the
outcome before rerunning. Saved projects restore configuration, not automatic recovery
from partially committed work. Keep successful batch reports to avoid duplicate loads.

## Release packaging

The installer uses per-user installation and provides an uninstaller. Portable ZIPs
contain the required application files; keep each executable beside `DSNForge.Core.dll`
and its `.config` file. The library has a separate ZIP and release-hosted NuGet package.
Application/installer files are unsigned. Verify `SHA256SUMS.txt` and inspect
`build-manifest.json` for source commit and workflow provenance.
