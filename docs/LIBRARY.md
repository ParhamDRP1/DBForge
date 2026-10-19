> **Version 1.2:** See the [wizard and profile guide](WIZARDS.md) for the current GUI, `ProjectRunner`, metadata and ZIP workflows. The lower-level `TransferEngine` examples below remain supported.

# DSNForge.Core

A .NET Framework 4.8 x64 library shared by the GUI and CLI. Install the release-hosted
`DSNForge.Core` NuGet package from a local feed, or reference the DLL from the library
ZIP. The package is not assumed to be published to nuget.org.

## Execute a saved project

```csharp
using System;
using System.Threading;
using DSNForge.Core;

var project = ProjectStore.Load("project.json");
var issues = Validation.Check(project);
if (issues.Count > 0)
    throw new InvalidOperationException(string.Join(Environment.NewLine, issues));

var engine = new TransferEngine();
engine.Progress += e => Console.WriteLine($"{e.Time:O} | {e.Level} | {e.Stage} | {e.Message}");

using (var cancellation = new CancellationTokenSource())
{
    var secrets = Credentials.FromEnvironment(project);
    await engine.ValidateRemoteAsync(project, secrets, cancellation.Token);
    var report = await engine.RunAsync(project, secrets, cancellation.Token);
    Reports.Save("reports/export", report);
    // Inspect Status, ReturnCode and UtilityId. Never blindly replay OutcomeUnknown.
}
```

Supply a `Credentials` object directly when the calling application owns a secret store.
The object is not part of serialized project settings. Progress callbacks can arrive
from worker threads; marshal them to your UI thread or queue them as the desktop app does.

## Build a workflow

```csharp
var project = new TransferProject { Name = "Archive copy", ReportDirectory = "reports" };
project.Db2.Host = "db2.example.invalid";
project.Db2.Catalog = "DB2LOC";
project.Db2.User = "DB2USER";
project.Ftp.Host = "ftp.example.invalid";
project.Ftp.User = "FTPUSER";

project.Steps.Add(new TransferStep {
    Name = "Copy table",
    Operation = Operation.Transfer,
    Table = "APP.SOURCE_TABLE",
    TargetTable = "APP.TARGET_TABLE",
    Dataset = new DatasetSettings {
        Name = "MYUSER.DSNFORGE.{run}.{batch}",
        AutoAllocate = true,
        DeleteAfterSuccess = true
    }
});
ProjectStore.Save("archive.json", project);
```

The target must exist and have a compatible layout. One project has one connection
profile; tasks execute sequentially. Public model enums serialize as numeric values;
the GUI displays their names. Prefer `ProjectStore` over hand-editing enum numbers.

## Key APIs

| API | Purpose |
|---|---|
| `ProjectStore.Load/Save/Clone` | Versioned JSON project persistence |
| `Validation.Check` | Local configuration validation |
| `TransferEngine.RunAsync` | Execute an ordered project |
| `TransferEngine.ValidateRemoteAsync` | DSNUTILU PREVIEW |
| `TransferEngine.Progress` | Timestamped telemetry |
| `Catalog.ReadTextLayout` | Read CHAR/VARCHAR source layout from the DB2 catalog |
| `ControlBuilder.Build/Rebind` | Generate or rebind utility cards |
| `Reports.Save` | JSON, CSV and HTML report export |
| `IUtilityExecutor` | Replaceable live utility adapter for testing or another provider |

The built-in adapter discovers the separately installed Microsoft DB2 provider at
runtime. It uses `DbConnection`/`DbCommand` abstractions; no proprietary assemblies are
bundled in the package or required for compilation. See [provider setup](USER-GUIDE.md#requirements-and-provider-discovery).

The library currently targets `net48`, not modern .NET/Core or cross-platform systems.
This is deliberate compatibility with the host provider. No automatic utility replay
or transactional rollback across multiple tasks is promised.
