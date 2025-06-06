#!/bin/bash
set -e

# Get the directory where the script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$SCRIPT_DIR/.."

# Protobuf source and output directories
PROTO_SRC="$PROJECT_ROOT/libpg_query/protobuf"
PROTO_OUT="$PROJECT_ROOT/src/PgQuery.NET/AST/Generated"

echo "🔍 Generating protobuf files..."
echo "Proto source: $PROTO_SRC"
echo "Output dir: $PROTO_OUT"

# Create output directory if it doesn't exist
mkdir -p "$PROTO_OUT"

# Check if libpg_query exists
if [ ! -d "$PROJECT_ROOT/libpg_query" ]; then
    echo "❌ libpg_query directory not found. Run build.sh first."
    exit 1
fi

# Check if protobuf file exists
if [ ! -f "$PROTO_SRC/pg_query.proto" ]; then
    echo "❌ pg_query.proto not found at $PROTO_SRC/pg_query.proto"
    exit 1
fi

# Function to find protoc
find_protoc() {
    # Try system protoc first
    if command -v protoc >/dev/null 2>&1; then
        echo "$(command -v protoc)"
        return 0
    fi
    
    # Try common installation paths
    for path in "/usr/local/bin/protoc" "/opt/homebrew/bin/protoc" "/usr/bin/protoc"; do
        if [ -x "$path" ]; then
            echo "$path"
            return 0
        fi
    done
    
    return 1
}

# Function to find gRPC tools
find_grpc_tools() {
    local base_path="$HOME/.nuget/packages/grpc.tools"
    
    if [ ! -d "$base_path" ]; then
        echo "❌ Grpc.Tools not found. Installing..."
        dotnet add "$PROJECT_ROOT/src/PgQuery.NET" package Grpc.Tools --version 2.72.0
    fi
    
    # Find latest version
    local latest_version=$(ls "$base_path" 2>/dev/null | sort -V | tail -n1)
    if [ -z "$latest_version" ]; then
        return 1
    fi
    
    local tools_path="$base_path/$latest_version/tools"
    
    # Detect platform
    if [[ "$OSTYPE" == "darwin"* ]]; then
        if [[ $(uname -m) == "arm64" ]]; then
            echo "$tools_path/macosx_arm64"
        else
            echo "$tools_path/macosx_x64"
        fi
    elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
        if [[ $(uname -m) == "x86_64" ]]; then
            echo "$tools_path/linux_x64"
        else
            echo "$tools_path/linux_x86"
        fi
    elif [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" ]]; then
        echo "$tools_path/windows_x64"
    else
        echo "$tools_path/linux_x64"  # fallback
    fi
}

# Find protoc compiler
echo "🔍 Looking for protoc compiler..."
PROTOC=$(find_protoc)
if [ $? -ne 0 ] || [ ! -x "$PROTOC" ]; then
    echo "❌ Could not find protoc compiler."
    echo "Please install Protocol Buffers compiler:"
    echo "  macOS: brew install protobuf"
    echo "  Ubuntu: apt-get install protobuf-compiler"
    echo "  Windows: Download from https://github.com/protocolbuffers/protobuf/releases"
    exit 1
fi

echo "✅ Found protoc at: $PROTOC"

# Find gRPC tools
echo "🔍 Looking for gRPC tools..."
PLUGIN_PATH=$(find_grpc_tools)
if [ $? -ne 0 ] || [ ! -d "$PLUGIN_PATH" ]; then
    echo "❌ Could not find gRPC tools."
    echo "Installing Grpc.Tools package..."
    dotnet add "$PROJECT_ROOT/src/PgQuery.NET" package Grpc.Tools --version 2.72.0
    PLUGIN_PATH=$(find_grpc_tools)
fi

echo "✅ Found gRPC tools at: $PLUGIN_PATH"

# Add plugin path to PATH for this session
export PATH="$PATH:$PLUGIN_PATH"

# Clean previous generated files
echo "🧹 Cleaning previous generated files..."
rm -f "$PROTO_OUT"/*.cs

# Generate C# classes from protobuf
echo "🔧 Generating C# protobuf classes..."
"$PROTOC" \
    --proto_path="$PROTO_SRC" \
    --csharp_out="$PROTO_OUT" \
    --csharp_opt=file_extension=.g.cs \
    "$PROTO_SRC/pg_query.proto"

# Verify generated files
if [ ! -f "$PROTO_OUT/PgQuery.g.cs" ]; then
    echo "❌ Failed to generate protobuf classes"
    exit 1
fi

echo "✅ Successfully generated protobuf classes in $PROTO_OUT"

# Optional: Generate JSON descriptors for debugging
if [ "$1" == "--with-descriptors" ]; then
    echo "🔧 Generating descriptor files..."
    "$PROTOC" \
        --proto_path="$PROTO_SRC" \
        --descriptor_set_out="$PROTO_OUT/pg_query.desc" \
        --include_imports \
        "$PROTO_SRC/pg_query.proto"
    echo "✅ Generated descriptor file: $PROTO_OUT/pg_query.desc"
fi

echo "🎉 Protobuf generation completed successfully!"
echo ""
echo "Generated files:"
ls -la "$PROTO_OUT"/*.cs 2>/dev/null || echo "No .cs files found" 