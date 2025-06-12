# SQL Pattern Capture Functionality Implementation

## 🎯 Overview

This document summarizes the implementation of capture functionality in PgQuery.NET, enabling users to extract specific AST nodes from SQL patterns using `$name` and `$()` syntax.

## ✅ Features Implemented

### 1. **Capture Syntax**
- **Named Captures**: `$name`, `$table`, `$condition` - Store with specific identifiers
- **Unnamed Captures**: `$()` - Store in default capture group
- **Multiple Captures**: Support multiple captures in single pattern
- **Wildcard Captures**: `($name _)` - Capture any matching node

### 2. **CLI Integration**
- **`--captures-only` Flag**: Extract only captured values, not full SQL
- **Debug Support**: `--debug` shows capture parsing process
- **Output Formats**: Named captures show `[name]: Node`, unnamed show just `Node`

### 3. **C# API**
- **`SqlPatternMatcher.GetCaptures()`**: Retrieve captured nodes
- **`SqlPatternMatcher.ClearCaptures()`**: Reset capture state
- **Thread-safe**: Uses ThreadLocal storage for captures
- **Named Access**: `captures["name"][0]` to access specific captures

## 🔧 Technical Implementation

### Core Changes Made

#### 1. **Tokenizer Fix** (`SqlPatternMatcher.cs` lines 45-82)
```csharp
// BEFORE: $name was treated as single token
[\d\w_]+[=\!\?]?     # method names or numbers

// AFTER: $ is separate token, enabling proper capture parsing
\$                    # capture operator (must come before word pattern)
|
[\d\w_]+[=\!\?]?     # method names or numbers
```

#### 2. **Parser Enhancement** (`ParseCapture()` method)
```csharp
// BEFORE: Incorrect capture name extraction
var nextExpression = Parse();
return new Capture(nextExpression);

// AFTER: Proper capture name extraction
var captureName = _tokens.Dequeue();
DebugLog($"[ParseCapture] Extracted capture name: '{captureName}'");
return new Capture(LITERALS["_"], captureName);
```

#### 3. **Expression Matching Fix** (`All.Match()` method)
```csharp
// BEFORE: Short-circuited on base.Match(), never called Capture expressions
if (base.Match(node)) return true;

// AFTER: Check for captures first, skip base matching when captures present
bool hasCaptureExpressions = _expressions.Any(expr => expr is Capture);
if (!hasCaptureExpressions) {
    if (base.Match(node)) return true;
}
// Then call individual expressions including Captures
```

### Architecture

```
Pattern: ($table (relname _))
    ↓
Tokenizer: ['(', '$', 'table', '(', 'relname', '_', ')', ')']
    ↓
Parser: All([Capture("table", Find("_")), All([Find("relname"), Find("_")])])
    ↓
Matcher: Calls Capture.Match() → stores in ThreadLocal<Dictionary<string, List<IMessage>>>
    ↓
API: SqlPatternMatcher.GetCaptures() returns captured nodes
```

## 🧪 Test Coverage

### Test Suite: `SqlPatternMatcherCaptureTests.cs`
- **9 Test Cases** - All passing ✅
- **Coverage Areas**:
  - Basic named captures
  - Unnamed captures with `$()`
  - Multiple captures in one pattern
  - Wildcard captures
  - Capture clearing between searches
  - Tokenizer validation
  - Debug output verification

### Example Test
```csharp
[Fact]
public void TestBasicCapture_SimplePattern()
{
    var sql = "SELECT * FROM test";
    var pattern = "($name (relname $name))";
    
    var results = SqlPatternMatcher.Search(pattern, sql, debug: true);
    var captures = SqlPatternMatcher.GetCaptures();
    
    Assert.True(captures.Count > 0, "Should have captures");
    Assert.True(captures.ContainsKey("name"), "Should have 'name' capture");
}
```

## 📚 Usage Examples

### Command Line
```bash
# Basic table capture
./grepsql.sh -p "(\$table (relname _))" --from-sql "SELECT * FROM users" --captures-only
# Output: [table]: Node

# Multiple captures
./grepsql.sh -p "(\$stmt _) (\$table (relname _))" --from-sql "SELECT * FROM products" --captures-only
# Output: [stmt]: Node

# Debug parsing
./grepsql.sh -p "(\$debug (relname _))" --from-sql "SELECT * FROM debug_table" --debug
```

### C# API
```csharp
// Search with captures
var sql = "SELECT name FROM users WHERE age > 18";
var results = SqlPatternMatcher.Search("($table (relname _))", sql);

// Get captured nodes
var captures = SqlPatternMatcher.GetCaptures();
foreach (var captureGroup in captures)
{
    Console.WriteLine($"Capture '{captureGroup.Key}': {captureGroup.Value.Count} items");
}

// Clear for next search
SqlPatternMatcher.ClearCaptures();
```

## 🚀 Use Cases

### 1. **Security Analysis**
```bash
# Find hardcoded credentials
./grepsql.sh -p "(\$credential (sval _))" -f "**/*.sql" --captures-only
```

### 2. **Performance Analysis**
```bash
# Extract table access patterns
./grepsql.sh -p "(SelectStmt ... (\$table (relname _)))" -f queries.sql --captures-only
```

### 3. **Code Quality**
```bash
# Find magic numbers
./grepsql.sh -p "(\$magic_number (ival _))" -f "**/*.sql" --captures-only
```

### 4. **Migration Planning**
```bash
# Extract schema references
./grepsql.sh -p "(\$schema_ref (schemaname _))" -f migration.sql --captures-only
```

## 🔍 Debug Process

When `--debug` is enabled, you can see the complete capture process:

```
[SqlPatternMatcher] Searching for pattern: ($table (relname _))
[Parser] Parsing token: '('
[Parser] Parsing token: '$'
[ParseCapture] Extracted capture name: 'table'
[Parser] Created expression: Capture
[All] Has capture expressions: True
[All] Skipping base Find logic due to capture expressions
[Capture] Attempting to match capture 'table' against node: Node
[Capture] Expression matched! Capturing node for 'table'
[Capture] Adding named capture: table
```

## 📈 Performance Considerations

- **Thread-Safe**: Uses `ThreadLocal<T>` for capture storage
- **Memory Efficient**: Captures store references to existing AST nodes
- **Cache-Friendly**: Integrates with existing expression compilation cache
- **Minimal Overhead**: Only processes captures when `$` patterns are present

## 🔮 Future Enhancements

Potential areas for expansion:
1. **Value Extraction**: Extract actual string/numeric values from captured nodes
2. **Capture Filtering**: Filter captures by node type or attributes
3. **Capture Transformation**: Transform captured nodes into specific formats
4. **Batch Processing**: Process multiple captures across file sets
5. **Export Formats**: JSON, CSV, XML output for captured data

## 📝 Commit Information

**Commit**: `633b207`
**Files Changed**: 3 files, 544 insertions(+), 27 deletions(-)
- `src/PgQuery.NET/Analysis/SqlPatternMatcher.cs` - Core implementation
- `tests/PgQuery.NET.Tests/SqlPatternMatcherCaptureTests.cs` - Test suite (new)
- `README.md` - Comprehensive documentation

This implementation provides a solid foundation for SQL pattern analysis and extraction workflows in PgQuery.NET. 