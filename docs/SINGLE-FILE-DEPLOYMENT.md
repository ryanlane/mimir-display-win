# Single-File Deployment Guide

## Why So Many DLLs?

Your release build folder has **hundreds of DLLs** because:

1. **.NET Runtime** - Self-contained deployment includes the entire .NET 8 runtime (~150+ DLLs)
2. **WPF Framework** - Windows Presentation Foundation is large (PresentationFramework, PresentationCore, etc.)
3. **NuGet Dependencies** - MQTTnet, Serilog, ImageMagick, etc.
4. **Satellite Assemblies** - Localization resources for multiple languages (de/, es/, fr/, it/, etc.)
5. **Regular Build vs Publish** - `dotnet build` doesn't create a single file; you need `dotnet publish`

## Current State

Your `.csproj` already has:
```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

But these settings **only apply during `dotnet publish`**, not `dotnet build`.

---

## Solution: Create a Single-File Executable

### Option 1: Quick Publish Command

Run this command to create a single-file executable:

```powershell
dotnet publish MimirDisplay\MimirDisplay.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish\
```

**Output:**
- Single file: `publish\MimirDisplay.exe` (~80-120 MB)
- No DLL clutter!
- Everything bundled inside the EXE

### Option 2: Optimize Further (Recommended)

Add these to your `.csproj` to reduce size and improve performance:

```xml
<PropertyGroup>
  <!-- Existing settings -->
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>

  <!-- NEW: Single-file optimizations -->
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>

  <!-- NEW: Trim unused code (reduces size by ~30-50%) -->
  <PublishTrimmed>true</PublishTrimmed>
  <TrimMode>partial</TrimMode>

  <!-- NEW: ReadyToRun compilation (faster startup) -->
  <PublishReadyToRun>true</PublishReadyToRun>

  <!-- NEW: Remove debug symbols from release -->
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
</PropertyGroup>
```

Then publish with:
```powershell
dotnet publish MimirDisplay\MimirDisplay.csproj -c Release -o publish\
```

---

## Size Comparison

| Method | File Count | Total Size | Startup |
|--------|-----------|------------|---------|
| **Current Build** | ~400 files | ~150 MB | Normal |
| **Single File (Basic)** | 1 EXE | ~100 MB | Normal |
| **Single File + Trimmed** | 1 EXE | ~60-80 MB | Fast |
| **Single File + R2R** | 1 EXE | ~110 MB | Faster |

---

## Trade-offs

### ✅ Pros of Single-File
- **Easy Distribution** - Just one .exe file
- **No DLL Hell** - Everything bundled
- **Professional** - Looks cleaner to users
- **Portable** - Copy & run anywhere

### ⚠️ Cons to Consider
- **Larger File Size** - Single EXE is bigger than just your app code
- **First-Run Extraction** - Some files extract to temp folder on first run (with `IncludeNativeLibrariesForSelfExtract`)
- **Trimming Risks** - Can break reflection-heavy code (WPF is usually safe)

---

## How It Works

### Single-File Bundle
1. All DLLs are embedded inside the .exe
2. On startup, .NET extracts necessary files to a temp folder (e.g., `%TEMP%\.net\MimirDisplay\`)
3. App runs normally from those extracted files
4. Temp files are reused on subsequent runs (fast)

### With Native Libraries Included
- ImageMagick native DLLs (e.g., `Magick.Native-Q8-x64.dll`) also bundled
- No need to ship separate native dependencies

---

## Recommended Workflow

### For Development (Current)
Use `dotnet build` or Visual Studio build:
- Fast compilation
- Easy debugging
- Separate DLLs visible

### For Distribution
Use `dotnet publish`:
- Single-file executable
- Optimized and trimmed
- Ready to ship

---

## Advanced: Even Smaller File

If you want to go **even smaller** (trade portability for size):

```xml
<SelfContained>false</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
```

**Result:**
- Single file: ~5-10 MB
- **Requires .NET 8 Runtime installed** on target machine
- Not recommended for display kiosks

---

## Next Steps

1. **Test the optimized publish** to ensure nothing breaks
2. **Update your installer script** to use the published EXE
3. **Keep both workflows:**
   - `dotnet build` for dev
   - `dotnet publish` for releases

See `PUBLISH-SCRIPT.ps1` for an automated publish script.
