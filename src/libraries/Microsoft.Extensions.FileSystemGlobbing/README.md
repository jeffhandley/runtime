# Microsoft.Extensions.FileSystemGlobbing

This assembly provides support for matching file system names/paths using [glob patterns](https://en.wikipedia.org/wiki/Glob_(programming)).

The primary type is [`Matcher`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.filesystemglobbing.matcher), you can `AddInclude(string)` and/or `AddExclude(string)` glob patterns and `Execute(DirectoryInfoBase)` the matcher in order to get a `PatternMatchingResult` of the specified directory.

## Contribution Bar
- [x] [We only consider lower-risk or high-impact fixes to maintain or improve quality](../README.md#primary-bar)
- [x] [We consider PRs that target this library for new source code analyzers](../README.md#secondary-bars)

## Deployment
The Microsoft.Extensions.FileSystemGlobbing assembly is shipped as a [NuGet package](https://www.nuget.org/packages/Microsoft.Extensions.FileSystemGlobbing).
