# Platform.Queue

A class library implementing `IQueueTool` — `status` and `drain` commands for a hypothetical
queue service. This project has no `Program.cs`; it exists to be composed by
[PlatformCli](../PlatformCli), which mounts it under the `queue` route prefix.
