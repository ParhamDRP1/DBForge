# Contributing

1. Open an issue describing the problem and expected behavior. Redact all identifiers,
   addresses, credentials and customer records from examples and logs.
2. Fork the repository and work in a feature branch.
3. Run `./build.ps1` on Windows with the .NET SDK and .NET Framework 4.8 targeting pack.
4. Add meaningful tests for conversion, batching or execution changes. Offline tests
   must not need a database, FTP server, credentials or the Microsoft DB2 provider.
5. Include relevant documentation and a CHANGELOG entry in the pull request.

For optional live testing, use a dedicated test schema and small synthetic datasets.
Do not run integration tests against production by default. A cancelled or disconnected
utility must not be automatically replayed without reconciling its server state.

The public API is in `src/DSNForge.Core`. The GUI and CLI share this engine. Keep new
execution behavior in the library rather than duplicating it in the clients.
