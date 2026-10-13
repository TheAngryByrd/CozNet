# Verify the deterministic contract

From the repository root:

```sh
dotnet test tests/CozNet.Tests/CozNet.Tests.fsproj -c Release
```

These tests verify parsing, accounting, scheduling, and session lifecycle.
They do not establish physical prediction accuracy. Use the [sample](../Tutorials/Getting_Started.md) for that investigation.
