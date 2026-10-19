> **Version 1.2:** See the [wizard and profile guide](WIZARDS.md) for the current GUI, `ProjectRunner`, metadata and ZIP workflows. The lower-level `TransferEngine` examples below remain supported.

# Command-line interface

Extract the CLI ZIP and keep its files together. The Microsoft provider is needed only
for `preview` and `run`, not project creation or local validation.

```powershell
DSNForge.Cli.exe new project.json
DSNForge.Cli.exe validate project.json
DSNForge.Cli.exe preview project.json
DSNForge.Cli.exe run project.json
```

| Command | Behavior |
|---|---|
| `new` | Writes a placeholder project; refuses to overwrite an existing file |
| `validate` | Checks local configuration and referenced inputs |
| `preview` | Runs server-side DSNUTILU PREVIEW |
| `run` | Executes enabled tasks in order and saves reports |

Set `DSNFORGE_DB2_PASSWORD` and `DSNFORGE_FTP_PASSWORD` through your shell's secret
mechanism. Their names are configurable in project JSON. Do not put passwords in
command arguments or commit them to project files. Relative project paths are resolved
from the JSON file's directory.

Ctrl+C requests cancellation. Reconcile uncertain server utility outcomes before
rerunning; cancellation is not a rollback guarantee.

Exit codes:

- **0:** completed, including `CompletedWithWarnings`, or successful validation/creation.
- **1:** runtime error, failed/cancelled project or remote validation error.
- **2:** invalid invocation or local validation failure.

Events are printed to the console and saved to the project report directory. Treat a
zero process code with warning status according to your site's return-code policy.
Set `AcceptReturnCode4=false` per task if RC 4 must fail the job.

See [example projects](../examples) and the [user guide](USER-GUIDE.md) for all settings.
