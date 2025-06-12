#!/bin/bash

# GrepSQL - Easy to use wrapper script
# Usage: ./grepsql.sh -p "pattern" [options]

cd "$(dirname "$0")"
exec ./bin/GrepSQL "$@" 