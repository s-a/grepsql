#!/bin/bash

# GrepSQL - Easy to use wrapper script
# Usage: ./grepsql.sh "pattern" [files...] [options]

cd "$(dirname "$0")"
exec ./bin/GrepSQL "$@" 