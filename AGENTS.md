# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 MAUI single-project app, currently containing the starter counter screen. 
`MauiProgram.cs` configures startup and dependency injection; `App`, `AppShell`, and `MainPage` contain XAML and code-behind. 
Use the currently empty `Config/` and `Services/` directories for configuration and shared behavior. 
Keep OS integrations in `Platforms/Windows/` and `Platforms/Android/`; assets and styles belong in `Resources/`. 
iOS/MacCatalyst template files exist but are outside product scope. 
The sibling DNS prototype is a separate project, not this app's implementation.

## Build, Test, and Development Commands

Use .NET 10 and the required MAUI workloads. From this directory:

```powershell
dotnet workload restore Regex-Site-Blocker-App.csproj
dotnet build Regex-Site-Blocker-App.csproj -f net10.0-windows10.0.19041.0 -c Debug
dotnet build Regex-Site-Blocker-App.csproj -f net10.0-windows10.0.19041.0 -t:Run
dotnet build Regex-Site-Blocker-App.csproj -f net10.0-android -c Debug
```

These restore workloads, build Windows, launch Windows, and build Android. Specify a target framework to avoid unrelated Apple builds. Android requires its SDK/JDK toolchain. Windows is currently unpackaged (`WindowsPackageType=None`).

## Coding Style & Naming Conventions

Follow existing C# conventions: four-space indentation, block-scoped namespaces, PascalCase types/methods, camelCase locals/parameters, and `Async` suffixes for asynchronous methods. Preserve the `Regex_Site_Blocker_App` namespace and matching XAML `x:Class` declarations. Nullable references and implicit usings are enabled. No dedicated formatter or linter configuration exists. Keep regex/configuration logic independent of UI and OS APIs; register dependencies in `MauiProgram.cs`.

## Agreed Product Requirements

Windows is first priority. The MAUI process handles background blocking and closes to tray; do not add a separate Windows service. Login autostart opens only the tray icon. Tray actions reopen settings or exit and stop blocking. Preserve existing DNS/proxy settings; WFP integration remains unimplemented. Android accessibility integration is a later target.

Support rule listing, addition, editing, deletion, individual enablement, regex syntax validation, Import/Export, startup loading, and live setting changes with result/error feedback. YAML contains only `start-program` and `blacklist`, whose entries have `pattern` and `enable`; omit schema version metadata. Exclude global blocking toggles, rule-testing UI, service recovery UI, and Android-specific settings UI. Import merging and save interaction remain undecided.

## Testing & Pull Requests

Configuration tests are in `tests/Config.Tests/`; run `dotnet test tests/Config.Tests/Config.Tests.csproj`. They link platform-independent source and use temporary files, never the live configuration. No coverage threshold or Git history is present. Manually verify UI interactions, tray lifecycle, and login startup. Use concise imperative commit subjects. PRs should describe behavior, validation, relevant issues, and configuration changes; include UI screenshots. Exclude `bin/`, `obj/`, `.vs/`, user settings, and `*.csproj.user` from commits.

`ConfigService` owns the configuration path: Windows uses `CommonApplicationData/RegexSiteBlocker/config.yaml`; other platforms use MAUI `AppDataDirectory/config.yaml`. Missing files are initialized, invalid files are preserved, and UI edits save automatically. Loading the autostart preference does not register Windows startup execution.
