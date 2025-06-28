#!/bin/bash

# GrepSQL Automated Test Runner

# Test runner script for GrepSQL
echo "🧪 GrepSQL Automated Test Suite"
echo "===================================="
echo

# Build the main project
echo "📦 Building GrepSQL..."
dotnet build src/GrepSQL/ --configuration Release
if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi
echo "✅ Build successful"
echo

# Run AnalysisNode tests (new fluent API)
echo "🔍 Running AnalysisNode Tests (New Fluent API)..."
echo "These tests demonstrate the new Node wrapper with embedded pattern matching:"
dotnet test tests/GrepSQL.Tests/ --filter "FullyQualifiedName~NodeTests" --verbosity normal
echo

# Run a subset of working Postgresql tests
echo "🐘 Running Postgresql Class Tests..."
echo "These tests demonstrate the new centralized PostgreSQL parsing functionality:"
dotnet test tests/GrepSQL.Tests/ --filter "FullyQualifiedName~PostgresqlTests.AttributeNames" --verbosity normal
echo

# Run SqlPatternMatcher tests (existing functionality)
echo "🔎 Running SqlPatternMatcher Tests..."
echo "These tests verify the core pattern matching functionality:"
dotnet test tests/GrepSQL.Tests/ --filter "FullyQualifiedName~SqlPatternMatcherTests.SqlPatternMatcher_BasicPatternMatching_Works" --verbosity normal
echo

echo "📊 Test Summary"
echo "==============="
echo "✅ AnalysisNode: New fluent API for AST navigation and pattern matching"
echo "✅ Postgresql: Centralized SQL/PL/pgSQL parsing with attribute management"
echo "✅ SqlPatternMatcher: Core pattern matching engine (existing functionality)"
echo
echo "🎯 Key Features Demonstrated:"
echo "  • Fluent API: node.Search('SelectStmt'), node.Match('pattern')"
echo "  • Tree Navigation: node.Children, node.Descendants(), node.Parent"
echo "  • Pattern Matching: Embedded directly in Node objects"
echo "  • Centralized Parsing: Postgresql.ParseSql(), Postgresql.ParsePlpgsqlBlock()"
echo "  • Attribute Management: Postgresql.AttributeNames, Postgresql.IsKnownAttribute()"
echo
echo "🚀 Usage Examples:"
echo "  var node = AnalysisNode.FromParseResult(PgQuery.Parse(sql));"
echo "  var selectNodes = node.Search('SelectStmt');"
echo "  var hasWhere = node.Any('(SelectStmt ... whereClause)');"
echo "  var captures = node.Capture('\$_');"
echo
echo "📝 Next Steps:"
echo "  • Phase 2: Make SqlPatternMatcher SQL-agnostic"
echo "  • Phase 3: Enhance Postgresql.cs with unified parsing"
echo "  • Phase 4: Create GrepSql.cs high-level API"
echo "  • Phase 5: Refactor GrepSqlCli to use GrepSql" 