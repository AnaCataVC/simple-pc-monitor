> **Created:** 2026-09-20
> **Last Updated:** 2026-09-20
> **Author:** ami-tech-lead & ami-research-context
> **Target:** System Core Monitor → System Core Monitor
> **Status:** Research / Architectural Blueprint

# Technical Architecture: WPF .NET 9 & MVVM Modernization Migration Guide

---

## 1. Executive Summary & Migration Context

System Core Monitor is currently architected as a high-performance, single-executable Windows desktop telemetry HUD built on **C# (.NET Framework 4.8 / WPF)** with an old-style MSBuild project (`ToolsVersion="4.0"`), zero NuGet dependencies, and monolithic code-behind UI logic (`MainWindow.xaml.cs` ~2600 lines).

Migrating to **.NET 9 (`net9.0-windows`)** unlocks:
- Modern SDK-style project format with concise XML and automatic file globbing.
- Modern C# 13 features (records, pattern matching, primary constructors, partial properties).
- Native WPF Fluent Theme styling and dynamic dark/light mode (`ThemeMode`).
- 30–40% faster startup time and reduced memory footprint.
- High-performance JSON serialization via `System.Text.Json` (BCL-included, zero-NuGet).
- Single-file publishing with framework-dependent or self-contained deployment.

---

## 2. Project File Modernization: .NET Framework 4.8 to SDK-Style .NET 9

### 2.1 The Legacy vs. SDK-Style Difference

The legacy `src/SimplePCMonitor.csproj` relies on `ToolsVersion="4.0"`, explicit `<Compile Include="..." />` for every C# file, explicit `<Page Include="..." />` for every XAML view, and GAC assembly references (`PresentationFramework`, `PresentationCore`, `WindowsBase`, `System.Xaml`).

### 2.2 Target `SystemCoreMonitor.csproj` (SDK-Style)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <ApplicationIcon>..\icon.ico</ApplicationIcon>
    <RootNamespace>SystemCoreMonitor</RootNamespace>
    <AssemblyName>SystemCoreMonitor</AssemblyName>

    <!-- Language and Compilation -->
    <LangVersion>13.0</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Deterministic>true</Deterministic>

    <!-- Assembly Attribute Handling -->
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>

    <!-- Single-File Packaging -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>false</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>

    <!-- Intel CET Guard: disable if unmanaged hooks trigger STATUS_SECURITY_CHECK_FAILURE -->
    <CETCompat>false</CETCompat>

    <!-- Suppress experimental Fluent ThemeMode runtime-switch warning -->
    <NoWarn>$(NoWarn);WPF0001</NoWarn>
  </PropertyGroup>

  <!--
    Zero-NuGet is the default philosophy.
    If System.ServiceProcess cannot be replaced with advapi32 P/Invoke, uncomment:
  <ItemGroup>
    <PackageReference Include="System.ServiceProcess.ServiceController" Version="9.0.2" />
  </ItemGroup>
  -->

</Project>
```

### 2.3 Step-by-Step Migration Actions

1. **Remove GAC References:** Delete all `<Reference Include="System..." />`, `PresentationFramework`, `PresentationCore`, `WindowsBase`. `<UseWPF>true</UseWPF>` automatically pulls `Microsoft.WindowsDesktop.App.WPF`.
2. **Remove File Globs:** Remove all explicit `<Compile Include="..." />` and `<Page Include="..." />` nodes. SDK-style auto-includes `.cs` and `.xaml` files.
3. **Handle `AssemblyInfo.cs`:** Set `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` or delete the legacy `Properties/AssemblyInfo.cs` to prevent `CS0579` duplicate attribute errors.
4. **Build Script Update:** Replace hardcoded MSBuild 4.0 path in `scripts/Build-Package.ps1` with `dotnet build` and `dotnet publish`.

---

## 3. WPF .NET 9 Fluent Theme & `ThemeMode`

### 3.1 Overview

.NET 9 integrates modern Windows 11 Fluent Design styles directly into WPF, providing rounded corners, refreshed controls, and native dark/light awareness without third-party theming libraries.

The `ThemeMode` enum provides four options:
- `None`: Classic Aero2 styling (backwards-compatible default).
- `Light`: Forces Fluent Light theme.
- `Dark`: Forces Fluent Dark theme.
- `System`: Synchronizes with the OS theme setting (`AppsUseLightTheme` registry).

### 3.2 XAML Configuration (`App.xaml`)

```xml
<Application x:Class="SystemCoreMonitor.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="UI/MainWindow.xaml"
             ThemeMode="System">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/CommonStyles.xaml" />
                <ResourceDictionary Source="Themes/PastelDark.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

### 3.3 Runtime Switching in C#

```csharp
#pragma warning disable WPF0001
public static void SetAppThemeMode(string themeName)
{
    Application.Current.ThemeMode = themeName.ToLowerInvariant() switch
    {
        "light" => ThemeMode.Light,
        "dark"  => ThemeMode.Dark,
        _       => ThemeMode.System
    };
}
#pragma warning restore WPF0001
```

### 3.4 Coexistence with 4 Custom Palettes

Strategy: retain dynamic `ResourceDictionary` swapping for custom HUD colors and gradients, while using `ThemeMode="System"` as the foundational base for system controls (scrollbars, context menus, dialogs).

---

## 4. Win32 P/Invoke & Marshalling Modernization in .NET 9

### 4.1 `DllImport` vs. `LibraryImport` (SYSLIB1054)

In .NET 9, Roslyn analyzers recommend replacing `[DllImport]` with `[LibraryImport]` (source-generated P/Invoke) for compile-time marshalling and Native AOT safety.

#### When to Keep `[DllImport]` (Complex Shell/Toolhelp Structs):

For structs with fixed-length inline string arrays (`ByValTStr`), `LibraryImport` does not support automatic marshalling without verbose custom marshallers:
- `PROCESSENTRY32` (`szExeFile` with `[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]`)
- `NOTIFYICONDATA` (`szTip`, `szInfo`)

**Requirement:** Ensure `CharSet = CharSet.Unicode` is explicit and exact Unicode entry points are used (`Process32FirstW`, `Process32NextW`).

#### When to Adopt `[LibraryImport]` (Blittable Primitives):

```csharp
public static partial class NativeMethods
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("ntdll.dll", SetLastError = true)]
    public static partial int NtSuspendProcess(IntPtr processHandle);

    [LibraryImport("ntdll.dll", SetLastError = true)]
    public static partial int NtResumeProcess(IntPtr processHandle);
}
```

### 4.2 Intel CET (Control-flow Enforcement Technology)

.NET 9 activates Intel CET hardware shadow stacks by default on x64.

- **Problem:** Native DLLs hooked by system utilities or legacy drivers may crash with `STATUS_SECURITY_CHECK_FAILURE` (`0xC0000409`).
- **Mitigation:** Include `<CETCompat>false</CETCompat>` in the `.csproj`.

### 4.3 `MEMORYSTATUSEX` Refactor to `struct`

```csharp
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;

    public static MEMORYSTATUSEX Create() =>
        new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
}
```

---

## 5. Configuration Serialization: Modernizing with `System.Text.Json`

### 5.1 The Legacy Problem

`ConfigManager.cs` uses manual substring parsing (`json.Contains("\"Theme\": \"Light\"")`) and string concatenation to write JSON. This is fragile and lacks schema flexibility.

### 5.2 Native `System.Text.Json` + Source Generator (Zero NuGet)

```csharp
using System.Text.Json.Serialization;

namespace SystemCoreMonitor.Core
{
    public class AppConfig
    {
        public int RefreshIntervalSeconds { get; set; } = 3;
        public string Theme { get; set; } = "Dark";
        public string ViewMode { get; set; } = "Full";
        public string Language { get; set; } = "es";
        public bool AlwaysOnTop { get; set; } = false;
        public bool AutoPowerScheme { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool StartMinimizedToTray { get; set; } = false;
        public bool RunAtStartup { get; set; } = false;
        public int TranscriptRetentionDays { get; set; } = 7;   // New: AI transcript pruning
    }

    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(AppConfig))]
    internal partial class AppConfigJsonContext : JsonSerializerContext { }
}
```

Modern `Load()` / `Save()` reduce to:

```csharp
var cfg = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
string json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
```

---

## 6. Architectural Evaluation: `CommunityToolkit.Mvvm` vs. Zero-NuGet Custom MVVM

### 6.1 CommunityToolkit.Mvvm 8.4.x Key Facts

- **Current Version:** `8.4.2` (Dec 2024 / early 2025 release cycle).
- **Packaging:** NuGet only (`CommunityToolkit.Mvvm`).
- **Key APIs:** `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`, `WeakReferenceMessenger`.
- **Important:** `CommunityToolkit.Mvvm` does **NOT** include `INavigationService`. Navigation must always be implemented by the developer regardless of which approach is chosen.

### 6.2 Comparison Matrix

| Criterion | CommunityToolkit.Mvvm 8.4.2 | Zero-NuGet Custom MVVM Base |
| :--- | :--- | :--- |
| **External NuGet Dependencies** | **1 package** | **0** |
| **Boilerplate Reduction** | Extreme (Roslyn source generators) | Moderate (standard C# properties) |
| **Partial Properties (C# 13)** | Yes (`[ObservableProperty] public partial ...`) | Manual |
| **Executable Size Impact** | Negligible (<50 KB) | **0 KB overhead** |
| **Async Command Handling** | Built-in `IAsyncRelayCommand` | ~40 lines `AsyncRelayCommand.cs` |
| **Event Aggregation** | Built-in `WeakReferenceMessenger` | ~30 lines or standard C# events |
| **Philosophy Alignment** | Pragmatic (Microsoft toolkit) | **Strict (Zero-NuGet invariant)** |

### 6.3 Zero-NuGet Lightweight MVVM Base (~180 lines in `src/Core/Mvvm/`)

Four files — `ObservableObject.cs`, `RelayCommand.cs`, `AsyncRelayCommand.cs`, `NavigationService.cs` — implement all the MVVM infrastructure needed, preserving 100% zero-dependency purity and the <600 KB standalone executable size.

**Final Recommendation:** Implement the **Zero-NuGet Lightweight MVVM Base**. If source generators prove indispensable after the refactor is underway, `CommunityToolkit.Mvvm` 8.4.2 is the only authorized exception.

---

## 7. Critical Migration Risks & Gotchas

### 7.1 `System.ServiceProcess` Breaking Change

- **Issue:** Under .NET 9, `System.ServiceProcess.ServiceController` is **not** part of `Microsoft.WindowsDesktop.App.WPF`.
- **Impact:** `ServiceCollector.cs` fails to compile.
- **Resolution Options:**
  - *Option A (Pragmatic):* Add `<PackageReference Include="System.ServiceProcess.ServiceController" Version="9.0.2" />`.
  - *Option B (Pure Zero-NuGet):* P/Invoke `advapi32.dll` directly (`OpenSCManagerW`, `EnumServicesStatusExW`, `ControlService`). Feasible given the project's existing P/Invoke expertise.

### 7.2 Intel CET Runtime Crashes (`0xC0000409`)

Keep `<CETCompat>false</CETCompat>` in `.csproj` to prevent crashes when native API hooks interact with the CET shadow stack.

### 7.3 Duplicate Assembly Attributes (`CS0579`)

Set `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` or delete `Properties/AssemblyInfo.cs` to avoid conflicts with SDK-generated attributes.

### 7.4 WPF Thread Dispatching

In .NET 9, unobserved task exceptions from background collectors are strictly caught. Telemetry polling loops must use `Application.Current.Dispatcher.InvokeAsync` for all UI updates.

### 7.5 Single-File Deployment Profiles

| Profile | `SelfContained` | Approx. Size | Requires .NET 9 Runtime? |
| :--- | :--- | :--- | :--- |
| Framework-Dependent | `false` | ~500 KB–1 MB | Yes |
| Self-Contained | `true` | ~30–75 MB | No |

Recommendation: provide both profiles in `scripts/Build-Package.ps1`.

---

## 8. Final Architecture Recommendations

1. **Project Format:** Migrate to SDK-style `net9.0-windows` with `<UseWPF>true</UseWPF>` and `<CETCompat>false</CETCompat>`.
2. **Theming:** Adopt `ThemeMode="System"` in `App.xaml` as the Fluent base; preserve 4 custom palettes via `ResourceDictionary` swapping.
3. **P/Invoke:** Keep `[DllImport]` for `PROCESSENTRY32` / `NOTIFYICONDATA`; adopt `[LibraryImport]` for blittable primitives; refactor `MEMORYSTATUSEX` to a zero-allocation `struct`.
4. **Config:** Replace `ConfigManager.cs` string parsing with `System.Text.Json` source generation.
5. **MVVM:** Implement the Zero-NuGet Lightweight MVVM Base (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`, `NavigationService`) — ~180 lines, preserves zero-dependency purity.
6. **`System.ServiceProcess`:** P/Invoke `advapi32.dll` natively to maintain zero NuGet dependencies.
