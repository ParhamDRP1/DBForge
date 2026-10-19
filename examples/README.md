# Example projects

All endpoints, users, schemas and dataset qualifiers here are placeholders.

- `load-text.json`: a synthetic UTF-8 CSV into a pre-existing text table. Create a
  compatible `APP.TEXT_SAMPLE` table with `CODE VARCHAR(16)` and `LABEL VARCHAR(128)`
  in a test environment, then configure the connection, schema and dataset prefix.
- `unload-and-transfer.json`: an archive download followed by a same-server table copy.
  Configure compatible existing source and target tables before execution.

Both examples omit passwords. Supply them through the GUI or configured environment
variables. Do not replace placeholders with production details in committed files.

Enum values in JSON: `Operation` 0=Load, 1=Unload, 2=Transfer;
`LoadInput` 0=NativeFile, 1=CsvToUtf8Fixed, 2=ExistingDataset, 3=GeneratedCards;
`Representation` 0=Binary, 1=Ascii, 2=Ebcdic; `Disposition` 0=Append, 1=Replace.
