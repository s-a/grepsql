#!/bin/bash
set -e

echo "🏗️  Building GrepSQL with libpg_query..."

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$SCRIPT_DIR/.."

cd "$PROJECT_ROOT"

# Step 1: Initialize and update git submodules
echo "📥 Setting up libpg_query submodule..."
if [ ! -f "libpg_query/Makefile" ]; then
    echo "Initializing and fetching libpg_query submodule..."
    git submodule update --init --recursive
else
    echo "libpg_query submodule already initialized, updating..."
    git submodule update --recursive
fi

cd libpg_query

# Clean previous builds
echo "🧹 Cleaning previous build..."
make clean || true

# Determine platform and build
echo "🔨 Building libpg_query..."
if [[ "$OSTYPE" == "darwin"* ]]; then
    echo "Building for macOS..."
    make
    LIBRARY_EXT="dylib"
    if [[ $(uname -m) == "arm64" ]]; then
        TARGET_RID="osx-arm64"
    else
        TARGET_RID="osx-x64"
    fi
elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
    echo "Building for Linux..."
    make
    LIBRARY_EXT="so"
    TARGET_RID="linux-x64"
elif [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" ]]; then
    echo "Building for Windows..."
    # For Windows, we'd need different build process
    # This is a placeholder - Windows builds are more complex
    echo "⚠️  Windows builds require Visual Studio tools"
    LIBRARY_EXT="dll"
    TARGET_RID="win-x64"
else
    echo "⚠️  Unknown platform: $OSTYPE, assuming Linux"
    make
    LIBRARY_EXT="so"
    TARGET_RID="linux-x64"
fi

# Step 2: Build wrapper library
echo "🔧 Building wrapper library..."
cd "$PROJECT_ROOT"

# Create wrapper if it doesn't exist
if [ ! -f "wrapper.c" ]; then
    echo "❌ wrapper.c not found. Creating basic wrapper..."
    cat > wrapper.c << 'EOF'
#include "libpg_query/pg_query.h"

// Simple wrapper to export pg_query functions
PgQueryParseResult pg_query_parse_wrapper(const char* input) {
    return pg_query_parse(input);
}

void pg_query_free_parse_result_wrapper(PgQueryParseResult result) {
    pg_query_free_parse_result(result);
}

PgQueryNormalizeResult pg_query_normalize_wrapper(const char* input) {
    return pg_query_normalize(input);
}

void pg_query_free_normalize_result_wrapper(PgQueryNormalizeResult result) {
    pg_query_free_normalize_result(result);
}
EOF
fi

# Compile wrapper
if [[ "$OSTYPE" == "darwin"* ]]; then
    echo "Compiling wrapper for macOS..."
    gcc -shared -fPIC -I. -o "libpgquery_wrapper.dylib" wrapper.c libpg_query/libpg_query.a
elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
    echo "Compiling wrapper for Linux..."
    gcc -shared -fPIC -I. -o "libpgquery_wrapper.so" wrapper.c libpg_query/libpg_query.a
fi

# Step 3: Create runtime directories and copy libraries
echo "📁 Setting up runtime directories..."
mkdir -p "runtimes/$TARGET_RID/native"

# Copy wrapper library (the static library is linked into the wrapper)
if [ -f "libpgquery_wrapper.$LIBRARY_EXT" ]; then
    echo "Copying libpgquery_wrapper.$LIBRARY_EXT to runtimes/$TARGET_RID/native/"
    cp "libpgquery_wrapper.$LIBRARY_EXT" "runtimes/$TARGET_RID/native/"
fi

# Also copy to project runtimes directory
mkdir -p "src/GrepSQL/runtimes/$TARGET_RID/native"
if [ -f "libpgquery_wrapper.$LIBRARY_EXT" ]; then
    echo "Copying native library to src/GrepSQL/runtimes/$TARGET_RID/native/"
    cp "libpgquery_wrapper.$LIBRARY_EXT" "src/GrepSQL/runtimes/$TARGET_RID/native/"
fi

# Step 4: Generate protobuf files
echo "🔧 Generating protobuf files..."
chmod +x scripts/generate_protos.sh
if [ ! -f "src/GrepSQL/AST/Generated/PgQuery.g.cs" ]; then
    echo "Protobuf files not found, running generation..."
    ./scripts/generate_protos.sh
    ls -la src/GrepSQL/AST/Generated/ || echo "Directory doesn't exist"
fi

# Step 5: Verify protobuf generation
if [ ! -f "src/GrepSQL/AST/Generated/PgQuery.g.cs" ]; then
    echo "❌ Protobuf generation failed or files not found"
    echo "Attempting to list generated files:"
    ls -la src/GrepSQL/AST/Generated/ || echo "Directory doesn't exist"
    exit 1
fi

# Step 6: Build .NET project
echo "🔨 Building .NET project..."
dotnet restore
dotnet build --configuration Release --verbosity minimal

if [ $? -eq 0 ]; then
    echo "✅ Build completed successfully"
else
    echo "❌ Build failed"
    exit 1
fi

# Step 7: Run tests (if they exist)
if [ -d "tests" ] && [ -f "tests/GrepSQL.Tests.csproj" ]; then
    echo "�� Running tests..."
    dotnet test --configuration Release --no-build --verbosity normal
fi

# Step 8: Create NuGet package
echo "📦 Creating NuGet package..."
dotnet pack src/GrepSQL --configuration Release --output ./artifacts

echo ""
echo "🎉 Build completed successfully!"
echo ""
echo "📊 Build Summary:"
echo "  Target RID: $TARGET_RID"
echo "  Library Extension: $LIBRARY_EXT"
echo "  Generated Libraries:"
ls -la "runtimes/$TARGET_RID/native/" 2>/dev/null || echo "    No runtime libraries found"
echo "  Generated Protobuf Files:"
ls -la "src/GrepSQL/AST/Generated/"*.cs 2>/dev/null || echo "    No protobuf files found"
echo "  NuGet Packages:"
ls -la "./artifacts/"*.nupkg 2>/dev/null || echo "    No packages found"

