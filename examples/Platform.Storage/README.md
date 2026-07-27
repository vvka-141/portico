# Platform.Storage

A class library implementing `IStorageTool` — `status` and `purge` commands for a hypothetical
storage service. This project has no `Program.cs`; it exists to be composed by
[PlatformCli](../PlatformCli), which mounts it under the `storage` route prefix.
