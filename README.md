# PgQuery.NET

A comprehensive .NET wrapper for libpg_query, providing PostgreSQL query parsing and advanced SQL pattern matching capabilities.

## Features

### 1. 🔍 **GrepSQL - Command Line Tool**
Search through SQL files with powerful pattern matching:
```bash
# Search for all SELECT statements with WHERE clauses
./grepsql.sh -p "(SelectStmt ... (whereClause ...))" -f "**/*.sql"

# Find specific patterns in SQL code
./grepsql.sh -p "(... (A_Expr (name [(String \">\")]) ...))" --from-sql "SELECT * FROM users WHERE age > 18"

# Show AST structure
./grepsql.sh -p "SelectStmt" --from-sql "SELECT id FROM users" --tree
```

### 2. 🧠 **Advanced SQL Pattern Matching**
Match complex SQL patterns with ellipsis navigation:

#### **Ellipsis Patterns - Deep AST Navigation**
```csharp
// Find WHERE clauses anywhere in the query
SqlPatternMatcher.Matches("(... (whereClause ...))", sql);

// Find specific values deep in the AST
SqlPatternMatcher.Matches("(... (whereClause ... 18))", "SELECT * FROM users WHERE age > 18");

// Navigate to any level with ellipsis
SqlPatternMatcher.Matches("(SelectStmt ... (targetList ...))", sql);
```

#### **Flexible Pattern Types**
```csharp
// Maybe patterns - optional matching
SqlPatternMatcher.Matches("(SelectStmt (whereClause ?(A_Expr ...)))", sql);

// Not patterns - negation
SqlPatternMatcher.Matches("(SelectStmt (targetList [(ResTarget (val !(A_Const (ival ...))))]))", sql);

// Any patterns - match any of several options
SqlPatternMatcher.Matches("(SelectStmt (fromClause [(RangeVar (relname {users orders}))]))", sql);

// Literal values
SqlPatternMatcher.Matches("(... (A_Const (ival 42)))", "SELECT 42");
SqlPatternMatcher.Matches("(... (A_Const (sval child)))", "SELECT 'child'");
```

#### **Enum Value Matching**
```csharp
// Match PostgreSQL operator enums
SqlPatternMatcher.Matches("(BoolExpr (boolop \"AND_EXPR\"))", "SELECT * FROM users WHERE age = 18 AND name = 'John'");
SqlPatternMatcher.Matches("(BoolExpr (boolop \"OR_EXPR\"))", "SELECT * FROM users WHERE age = 18 OR name = 'John'");
```

#### **Capture and Search**
```csharp
// Capture nodes for later analysis
var sql = "SELECT name FROM users WHERE age > 18";
var pattern = "(SelectStmt ... (whereClause $cond(A_Expr ...)))";
SqlPatternMatcher.Matches(pattern, sql);
var captures = SqlPatternMatcher.GetCaptures();
var condition = captures["cond"][0]; // The A_Expr node

// Search for all matches
var matches = SqlPatternMatcher.Search("(A_Const (sval ...))", sql);
// Returns all string constants in the query
```

### 3. ⚙️ **Core Query Parsing**
Parse PostgreSQL queries into an AST (Abstract Syntax Tree):
```csharp
var query = "SELECT id, name FROM users WHERE age > 25";
var result = PgQuery.Parse(query);
// Access the AST through result.ParseTree

// Print formatted AST
Console.WriteLine(TreePrinter.Print(result.ParseTree));
```

### 4. 📊 **Query Analysis**
Extract meaningful information from queries:
```csharp
var query = "SELECT * FROM users JOIN orders ON users.id = orders.user_id";
var result = PgQuery.Parse(query);

// Extract table names
var tables = result.GetTableNames(); // ["users", "orders"]

// Detect query types
var isSelect = result.IsSelectQuery(); // true
```

### 5. 🛡️ **Error Handling**
Robust error handling with detailed information:
```csharp
try
{
    var result = PgQuery.Parse("SELECT * FROM");
}
catch (PgQueryException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Position: {ex.CursorPosition}");
}
```

## 🚀 **Getting Started**

### Installation

1. Clone the repository:
```bash
git clone https://github.com/your-username/pgquery-dotnet.git
cd pgquery-dotnet
```

2. Build the project:
```bash
dotnet build
```

3. Try GrepSQL (command-line tool):
```bash
# Make script executable
chmod +x grepsql.sh

# Test with a simple pattern
./grepsql.sh -p "SelectStmt" --from-sql "SELECT id FROM users"
```

4. Use in your .NET code:
```csharp
using PgQuery.NET;
using PgQuery.NET.Analysis;

var sql = "SELECT name FROM users WHERE age > 18";
bool matches = SqlPatternMatcher.Matches("(... (whereClause ...))", sql);
```

## 📖 **Pattern Syntax Guide**

Our pattern matching uses a Lisp-like syntax that mirrors the PostgreSQL AST structure:

### **Basic Patterns**
- `(NodeType ...)` - Match any node of this type
- `(NodeType (field ...))` - Match field within node  
- `(NodeType (field value))` - Match exact field value

### **Advanced Patterns**
- `(... pattern)` - **Ellipsis**: Find pattern anywhere in subtree
- `?(pattern)` - **Maybe**: Optional pattern (field may be null)
- `!(pattern)` - **Not**: Negation pattern (must not match)
- `{value1 value2}` - **Any**: Match any of the listed values
- `$name(pattern)` - **Capture**: Save matched node for later analysis

### **Real Examples**
```bash
# Find all WHERE clauses (anywhere in the query)
./grepsql.sh -p "(... (whereClause ...))" --from-sql "SELECT * FROM users WHERE age > 18"

# Find queries WITHOUT WHERE clauses  
./grepsql.sh -p "(SelectStmt !(whereClause ...))" --from-sql "SELECT * FROM users"

# Find specific table names
./grepsql.sh -p "(... (RangeVar (relname {users orders})))" --from-sql "SELECT * FROM users"

# Capture conditions for analysis
./grepsql.sh -p "(... $condition(A_Expr ...))" --from-sql "SELECT * FROM users WHERE age > 18"
```

## 🧪 **Testing**

We have comprehensive tests with **100% pass rate**:

```bash
# Run all tests
dotnet test

# Run just pattern matching tests
dotnet test tests/PgQuery.NET.Tests/ --filter "SqlPatternMatcherTests"
```

**Test Coverage:**
- ✅ 28/28 SqlPatternMatcher tests passing (100%)
- ✅ All major pattern types (ellipsis, maybe, not, any, capture)
- ✅ Complex nested queries and CTEs
- ✅ Enum value matching and literals
- ✅ Deep AST navigation with ellipsis patterns

## 🔧 **Architecture**

### **Core Components**
- **PgQuery.Parse()** - Main parsing entry point using libpg_query
- **SqlPatternMatcher** - Advanced pattern matching engine  
- **GrepSQL** - Command-line tool for searching SQL files
- **TreePrinter** - AST visualization for debugging

### **Pattern Matching Engine**
Our SqlPatternMatcher implements a sophisticated pattern matching system:

1. **Recursive AST Traversal** - Navigate any depth with ellipsis patterns
2. **Smart Field Detection** - PascalCase vs camelCase routing
3. **Flexible Value Matching** - Literals, enums, and primitives
4. **Capture System** - Extract matched nodes for analysis
5. **Negation Support** - Complex logical combinations

## 📚 **Learning Path**

1. **Start with GrepSQL**: Use the command-line tool to understand AST structure
2. **Explore Patterns**: Try different pattern types on sample SQL
3. **View AST Structure**: Use `--tree` flag to see how PostgreSQL parses queries
4. **Build Complex Patterns**: Combine ellipsis, maybe, and capture patterns
5. **Integrate in Code**: Use SqlPatternMatcher in your .NET applications

## 🤝 **Contributing**

We welcome contributions! Check out our test suite to see what patterns are supported and add new ones.

## 📄 **License**

This project is licensed under the MIT License.

## 🙏 **Acknowledgments**

- [libpg_query](https://github.com/pganalyze/libpg_query) - The core PostgreSQL parser library
- PostgreSQL community for the robust SQL grammar
