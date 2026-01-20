# Build Issue: .NET 10 and WindowsAppSDK Compatibility - RESOLVED ?

## Status
**`dotnet clean`**: ? **Working**  
**`dotnet build`**: ? **Working**

## Resolution Summary

The project has been successfully downgraded from .NET 10 to .NET 8, which resolved the build issues:

### Changes Made
1. **Target Framework**: Changed from `net10.0-windows10.0.17763` to `net8.0-windows10.0.17763`
2. **H.NotifyIcon.WinUI**: Downgraded from `2.4.1` to `2.2.0`
3. **WindowsAppSDK**: Updated to `1.6.241114003` (compatible version)
4. **XAML Fixes**:
   - Removed `Width` and `Height` properties from `<Window>` elements (not supported in WinUI 3)
   - Added programmatic window sizing using `AppWindow.Resize()` in code-behind
   - Converted `x:Bind` to regular `{Binding}` to avoid FrameworkElement conversion issues
5. **Code Fixes**:
   - Removed `TrayMouseDoubleClick` event (not available in H.NotifyIcon.WinUI 2.2.0)
   - Set proper DataContext on Grid element

### Build Results
- ? Build succeeds with 0 errors
- ?? 5 warnings (non-breaking, related to AOT compatibility)

---

## Original Problem Description

The project was targeting `.NET 10` which is very new (released January 2025). The current versions of Microsoft WindowsAppSDK had XAML compiler compatibility issues with .NET 10:

### Stable Version (1.8.260101001)
- Uses a .NET Framework 4.7.2-based XAML compiler (`XamlCompiler.exe`)
- Cannot load or process .NET 10 assemblies
- Error: `XamlCompiler.exe exited with code 1`
- Root cause: .NET Framework applications cannot load .NET 10 assemblies

### Experimental Version (2.0.0-experimental4)
- Includes a .NET 6-based XAML compiler
- Missing required dependencies (e.g., `System.Security.Permissions` v6.0.0.0)
- Error: `Could not load file or assembly 'System.Security.Permissions'`

## Why We Can't Downgrade to .NET 9 or .NET 8

The project uses `H.NotifyIcon.WinUI` version `2.4.1`, which **requires .NET 10**. Attempting to downgrade results in:
```
error NU1202: Package H.NotifyIcon.WinUI 2.4.1 is not compatible with net9.0-windows
```

## Current Workarounds Attempted

1. ? **DisableXbfGeneration**: Tried but doesn't prevent XAML compiler from running
2. ? **XamlCompiler.exe.config**: Created runtime config file, but still fails
3. ? **UseXamlCompilerExecutable=false**: Tried to force MSBuild task, but experimental version missing dependencies
4. ? **System.Security.Permissions package**: Added but MSBuild tasks need DLLs in tools folder

## Resolution Timeline

This is a **known compatibility gap** that will likely be resolved by Microsoft in upcoming releases:
- WindowsAppSDK team needs to release a version with full .NET 10 support
- OR H.NotifyIcon.WinUI needs to support .NET 9/8

## Temporary Solutions

### Option 1: Wait for Updates (Recommended)
Monitor these repositories for updates:
- [microsoft/WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK)
- [HavenDV/H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)

### Option 2: Use Visual Studio 2022 (Preview)
Visual Studio 2022 Preview may have better .NET 10 + WinUI 3 integration:
- Install [VS 2022 Preview](https://visualstudio.microsoft.com/vs/preview/)
- Open solution in Visual Studio instead of using `dotnet build`

### Option 3: Build in Docker/CI
If you have access to a build environment with proper WindowsAppSDK tooling:
```bash
# This may work in some CI environments with newer build tools
dotnet build -c Release
```

## Working Commands

```bash
# Clean build artifacts - WORKS ?
dotnet clean

# Restore NuGet packages - WORKS ?
dotnet restore

# Build - BLOCKED ?
dotnet build
```

## Technical Details

### Error Message
```
error MSB3073: The command "XamlCompiler.exe" "input.json" "output.json" exited with code 1.
```

### Root Cause
The XAML compiler needs to reflect over .NET assemblies to generate code-behind files. The .NET Framework 4.7.2 compiler cannot load .NET 10 assemblies due to fundamental runtime differences.

### Affected Files
- `App.xaml` ? `App.xaml.g.cs` (generated)
- `MainWindow.xaml` ? `MainWindow.xaml.g.cs` (generated)
- `SettingsWindow.xaml` ? `SettingsWindow.xaml.g.cs` (generated)

## Monitoring

Check for updates regularly:
```bash
# Check for newer WindowsAppSDK versions
dotnet list package --outdated

# Check for newer H.NotifyIcon.WinUI versions that might support .NET 9
dotnet add package H.NotifyIcon.WinUI --version *-*
```

---

**Last Updated**: January 2026  
**SDK Version**: .NET 10.0.102  
**WindowsAppSDK**: 1.8.260101001  
**Issue Tracker**: https://github.com/microsoft/WindowsAppSDK/issues
