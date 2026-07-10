# Single-File Deployment - Results

## ✅ Success! Single File Created

### Before (dotnet build)
- **File Count:** ~400+ files (DLLs, PDBs, resources, satellites)
- **Total Size:** ~150 MB across all files
- **Distribution:** Requires zipping entire folder

### After (dotnet publish - Single File)
- **File Count:** **3 files** (MimirDisplay.exe + .env + appsettings.json)
- **Executable Size:** **80.71 MB**
- **Distribution:** Just copy the `publish\` folder

---

## What's Inside the EXE?

The **MimirDisplay.exe** now contains:
- ✅ Your application code
- ✅ .NET 8 Runtime (all framework DLLs)
- ✅ WPF framework
- ✅ All NuGet packages (MQTTnet, Serilog, ImageMagick, etc.)
- ✅ Native libraries (ImageMagick native DLLs)
- ✅ Application icon

---

## How to Use

### Development (Fast Iteration)
```powershell
# Use regular build for fast compilation
dotnet build MimirDisplay\MimirDisplay.csproj -c Release
```
- Output: `MimirDisplay\bin\Release\net8.0-windows\win-x64\`
- Multiple DLLs (easier to debug)

### Distribution (Production)
```powershell
# Use the publish script for single-file
.\publish-single-file.ps1
```
- Output: `publish\MimirDisplay.exe`
- Single file (ready to ship)

---

## Distribution Options

### Option 1: Zip the Publish Folder
```powershell
Compress-Archive -Path publish\* -DestinationPath MimirDisplay-v1.0.0.zip
```
**Result:** 3 files in a zip (exe + config files)

### Option 2: Installer (Inno Setup)
Update `MimirDisplaySetup.iss`:
```inno
[Files]
Source: "publish\MimirDisplay.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\.env"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion
```

### Option 3: Just the EXE
For minimal distribution, you can ship **just MimirDisplay.exe**:
- Config files will be created on first run with defaults
- User can create `.env` manually if needed

---

## First-Run Behavior

When the user runs `MimirDisplay.exe`:

1. **Extraction (First Time Only)**
   - .NET extracts bundled files to: `%TEMP%\.net\MimirDisplay\<hash>\`
   - Takes ~2-3 seconds
   - Subsequent runs are instant (uses cached files)

2. **Normal Operation**
   - App runs as if all DLLs were separate
   - No performance penalty
   - Logging, MQTT, everything works normally

---

## Size Optimization Notes

### Current Settings (Conservative)
```xml
<PublishTrimmed>false</PublishTrimmed>
<PublishReadyToRun>false</PublishReadyToRun>
```
- **Safe:** Works with WPF reflection/XAML
- **Size:** 80 MB

### If You Want Smaller (Experimental)
Enable trimming in `.csproj`:
```xml
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>partial</TrimMode>
```

Then publish:
```powershell
.\publish-single-file.ps1
```

**Result:** ~50-60 MB
**Risk:** May break XAML bindings or reflection code
**Recommendation:** Test thoroughly before deploying

---

## Comparison to Other Tools

| Tool | File Count | Size | Startup |
|------|-----------|------|---------|
| **Node.js Electron App** | 1000+ | 200+ MB | Slow |
| **.NET WPF (Multi-File)** | 400+ | 150 MB | Fast |
| **.NET WPF (Single-File)** | **1** | **80 MB** | **Fast** |
| **Native C++ App** | 1-50 | 5-20 MB | Very Fast |

Your single-file .NET app is competitive with native apps while being much easier to develop!

---

## Recommended Workflow

### Daily Development
```powershell
# Quick build for testing
dotnet build MimirDisplay\MimirDisplay.csproj -c Release
```

### Release Builds
```powershell
# Create single-file executable
.\publish-single-file.ps1

# Test it
.\publish\MimirDisplay.exe

# Create installer or zip for distribution
```

---

## Next Steps

1. ✅ **Single-file created** - `publish\MimirDisplay.exe`
2. 📦 **Update installer** - Modify `MimirDisplaySetup.iss` to use `publish\` folder
3. 🧪 **Test deployment** - Run on a clean Windows machine
4. 🚀 **Ship it!** - Distribute via installer or zip

See also:
- `SINGLE-FILE-DEPLOYMENT.md` - Detailed explanation
- `publish-single-file.ps1` - Automated publish script
- `ICON-AND-INSTALLER-GUIDE.md` - Installer creation guide
