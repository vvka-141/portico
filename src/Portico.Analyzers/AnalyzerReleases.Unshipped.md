; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; Portico is pre-1.0, so every rule is unshipped: nothing has been released under a
; stability promise yet. Rules move to AnalyzerReleases.Shipped.md when 1.0.0 ships.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------------------------
POR001  | Portico  | Error    | Route placeholder does not match any parameter
POR002  | Portico  | Error    | Duplicate [CliRoute] signature on one type
POR003  | Portico  | Error    | Malformed [CliOption] spec
POR004  | Portico  | Warning  | Missing [CliCommandExample] on [CliRoute] method
POR005  | Portico  | Error    | [CliArgument] has no matching route placeholder
POR006  | Portico  | Error    | CliOptions bundle must have a public parameterless constructor (CliMiddleware is exempt)
POR007  | Portico  | Error    | Parameter carries more than one [CliArgument]
POR008  | Portico  | Error    | [CliRoute] method has an invalid return type
POR009  | Portico  | Error    | Two options on one command declare the same alias
POR010  | Portico  | Error    | [CliOption] type cannot be built from a command-line string
