# GrepSQL Build Scripts

This directory contains improved build scripts for GrepSQL that handle native library compilation, protobuf generation, and cross-platform builds.

## 🚀 Quick Start

### macOS/Linux
```bash
chmod +x scripts/build.sh
./scripts/build.sh
```

### Windows
```powershell
.\scripts\build.ps1
```

## 📋 Prerequisites

### All Platforms
- **.NET SDK 6.0+**: [Download here](https://dotnet.microsoft.com/download)
- **Git**: For cloning dependencies
- **Protocol Buffers Compiler (protoc)**: For generating C# code from .proto files

### macOS
```bash
# Install protobuf compiler
brew install protobuf

# Install build tools (usually pre-installed)
xcode-select --install
```

### Linux (Ubuntu/Debian)
```bash
# Install protobuf compiler
sudo apt-get update
sudo apt-get install protobuf-compiler build-essential

# For RHEL/CentOS/Fedora
sudo yum install protobuf-compiler gcc make
```

### Windows
- **Visual Studio Build Tools 2019+** or **Visual Studio 2019+**
- **Protocol Buffers Compiler**: Download from [GitHub releases](https://github.com/protocolbuffers/protobuf/releases)

## 🔧 Build Scripts

### `build.sh` (macOS/Linux)

Comprehensive build script that:
1. Clones and builds libpg_query
2. Creates wrapper libraries
3. Generates protobuf C# classes
4. Builds the .NET project
5. Creates NuGet packages

**Usage:**
```bash
./scripts/build.sh
```

### `build.ps1` (Windows)

PowerShell equivalent with additional options:

**Usage:**
```powershell
# Full build
.\scripts\build.ps1

# Skip native library building
.\scripts\build.ps1 -SkipNative

# Skip protobuf generation
.\scripts\build.ps1 -SkipProtobuf

# Skip .NET build
.\scripts\build.ps1 -SkipBuild

# Specify configuration
.\scripts\build.ps1 -Configuration Debug
```

### `generate_protos.sh` (All platforms)

Standalone protobuf generation script with:
- Auto-detection of protoc and gRPC tools
- Cross-platform plugin path detection
- Automatic dependency installation
- Detailed error reporting

**Usage:**
```bash
# Basic generation
./scripts/generate_protos.sh

# Generate with debug descriptors
./scripts/generate_protos.sh --with-descriptors
```

## 🏗️ What Gets Built

### Native Libraries
- **libpg_query**: Core PostgreSQL parser library
- **libpgquery_wrapper**: .NET-compatible wrapper (if needed)

Libraries are placed in:
```
runtimes/
├── linux-x64/native/
├── osx-x64/native/
├── osx-arm64/native/
└── win-x64/native/
```

### Protobuf Classes
Generated C# classes from `libpg_query/protobuf/pg_query.proto`:
```
src/GrepSQL/AST/Generated/
└── GrepSQL.g.cs
```

### NuGet Package
Final package in:
```
artifacts/
└── GrepSQL.{version}.nupkg
```

## 🔍 Troubleshooting

### Common Issues

#### 1. "protoc not found"
**Solution:** Install Protocol Buffers compiler
```bash
# macOS
brew install protobuf

# Ubuntu/Debian  
sudo apt-get install protobuf-compiler

# Windows: Download from GitHub releases
```

#### 2. "libpgquery_wrapper.dylib not found"
**Solution:** The native library loader has been improved to handle multiple fallback locations. Make sure the build completed successfully.

#### 3. gRPC Tools not found
**Solution:** The script will automatically install Grpc.Tools NuGet package:
```bash
dotnet add package Grpc.Tools --version 2.72.0
```

#### 4. Visual Studio Build Tools (Windows)
**Solution:** Install Visual Studio Build Tools 2019 or later from Microsoft.

### Debug Mode

For detailed debugging, check the console output. The scripts provide:
- ✅ Success indicators
- ❌ Error messages with specific solutions
- 🔍 Debug information about paths and versions

### Manual Steps (if scripts fail)

1. **Clone libpg_query manually:**
```bash
git clone -b 17-latest https://github.com/pganalyze/libpg_query.git
cd libpg_query
make
```

2. **Generate protobuf manually:**
```bash
protoc --proto_path=libpg_query/protobuf \
       --csharp_out=src/GrepSQL/AST/Generated \
       libpg_query/protobuf/pg_query.proto
```

3. **Build .NET project:**
```bash
dotnet build --configuration Release
```

## 🔄 Integration with CI/CD

### GitHub Actions Example
```yaml
- name: Setup .NET
  uses: actions/setup-dotnet@v1
  with:
    dotnet-version: 6.0.x

- name: Install protobuf (Ubuntu)
  run: sudo apt-get install protobuf-compiler

- name: Build
  run: ./scripts/build.sh
```

### Docker Example
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:6.0
RUN apt-get update && apt-get install -y protobuf-compiler build-essential
COPY . .
RUN ./scripts/build.sh
```

## 📚 Advanced Usage

### Custom libpg_query Branch
Edit the build scripts to use a different branch:
```bash
LIBPG_QUERY_BRANCH="your-branch-name"
```

### Cross-Compilation
The scripts detect the current platform and build accordingly. For cross-compilation, you'd need to modify the target RID variables.

### Protobuf Customization
Modify `generate_protos.sh` to add custom protoc options:
```bash
"$PROTOC" \
    --proto_path="$PROTO_SRC" \
    --csharp_out="$PROTO_OUT" \
    --csharp_opt=file_extension=.g.cs \
    --csharp_opt=base_namespace=MyNamespace \
    "$PROTO_SRC/pg_query.proto"
```

## 🎯 Next Steps

After successful build:
1. Test the generated NuGet package
2. Run integration tests
3. Publish to NuGet.org (if applicable)
4. Update documentation with any API changes

## 🆘 Getting Help

If you encounter issues:
1. Check the console output for specific error messages
2. Ensure all prerequisites are installed
3. Try running individual build steps manually
4. Open an issue with the complete error output 