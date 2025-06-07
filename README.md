# PgQuery.NET

A comprehensive .NET wrapper for libpg_query, providing PostgreSQL query parsing and advanced SQL pattern matching capabilities.

## Features

### 1. 🔍 **GrepSQL - Command Line Tool**
Search through SQL files with powerful pattern matching:
```bash
# Search for all SELECT statements with WHERE clauses
./grepsql.sh -p "(SelectStmt ... (whereClause ...))" -f "**/*.sql"

# Find specific table names with highlighting
./grepsql.sh -p "(relname \"users\")" --from-sql "SELECT * FROM users JOIN products ON users.id = products.user_id" --highlight

# Highlight matches in HTML format for documentation
./grepsql.sh -p "(relname \"products\")" -f queries.sql --highlight --highlight-style html

# Show highlighted matches in markdown format
./grepsql.sh -p "(colname \"name\")" --from-sql "SELECT name FROM users" --highlight --highlight-style markdown

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

#### **S-Expression Attribute Matching**
```csharp
// Match specific table names precisely
SqlPatternMatcher.Matches("(relname \"users\")", "SELECT * FROM users JOIN products ON users.id = products.user_id");
// Returns 1 match (only the users table, not products)

// Match column names by attribute
SqlPatternMatcher.Matches("(colname \"id\")", "SELECT id, name FROM users WHERE id > 10");
// Returns matches for id column references

// Match string constants by value
SqlPatternMatcher.Matches("(sval \"admin\")", "SELECT * FROM users WHERE role = 'admin'");
// Returns 1 match for the 'admin' string constant
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

### 5. 🎨 **SQL Highlighting & Source Extraction**
Highlight matching SQL parts with multiple output formats:
```csharp
// Extract source text from any AST node
var sql = "SELECT id, name FROM users WHERE age > 18";
var result = PgQuery.Parse(sql);
var selectStmt = result.ParseTree.Stmts[0].Stmt;
var sourceText = selectStmt.GetSource(); // "SELECT id, name FROM users WHERE age > 18"

// Get location information from nodes
var matches = SqlPatternMatcher.Search("(relname \"users\")", sql);
var tableNode = matches[0];
var location = LocationExtractor.GetLocation(tableNode, sql);
// location.Line = 1, location.Column = 26, location.Text = "users"
```

**Command Line Highlighting:**
```bash
# ANSI colored output (default)
./grepsql.sh -p "(relname \"users\")" --from-sql "SELECT * FROM users" --highlight

# HTML output for web documentation
./grepsql.sh -p "(relname \"users\")" --from-sql "SELECT * FROM users" --highlight --highlight-style html
# Output: SELECT * FROM <mark>users</mark>

# Markdown output for documentation
./grepsql.sh -p "(relname \"users\")" --from-sql "SELECT * FROM users" --highlight --highlight-style markdown
# Output: SELECT * FROM **users**

# Show context lines around matches
./grepsql.sh -p "(relname \"products\")" -f complex.sql --highlight --context 2
```

### 6. 🛡️ **Error Handling**
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

Our pattern matching uses both simple node type patterns and advanced Lisp-like syntax:

### **Simple Patterns (Recommended)**
- `_` - Match any single node (root only)
- `...` - Match any node with children (root only)  
- `NodeType` - Find all nodes of this type (recursive search)
- `A_Const` - Find all constants (numbers, strings, booleans)
- `SelectStmt` - Find all SELECT statements

### **Advanced Patterns**
- `(NodeType ...)` - Match any node of this type
- `(NodeType (field ...))` - Match field within node  
- `(NodeType (field value))` - Match exact field value
- `(... pattern)` - **Ellipsis**: Find pattern anywhere in subtree
- `?(pattern)` - **Maybe**: Optional pattern (field may be null)
- `!(pattern)` - **Not**: Negation pattern (must not match)
- `{value1 value2}` - **Any**: Match any of the listed values
- `$name(pattern)` - **Capture**: Save matched node for later analysis
- `(attribute "value")` - **S-Expression**: Match nodes by specific attribute values

## 📋 **Pattern Examples Table**

| SQL Query | Pattern | Description | Matches |
|-----------|---------|-------------|---------|
| `SELECT id FROM users` | `_` | Any single node | ✅ 1 (root node) |
| `SELECT id FROM users` | `SelectStmt` | SELECT statements | ✅ 1 |
| `SELECT 1, 'hello', true` | `A_Const` | All constants | ✅ 3 (1, 'hello', true) |
| `SELECT * FROM users WHERE age > 25` | `A_Expr` | Expressions | ✅ 1 (age > 25) |
| `SELECT COUNT(*), AVG(age)` | `FuncCall` | Function calls | ✅ 2 (COUNT, AVG) |
| `SELECT u.name FROM users u` | `ColumnRef` | Column references | ✅ 1 (u.name) |
| `SELECT * FROM users WHERE active = true` | `BoolExpr` | Boolean expressions | ✅ 0 (no AND/OR) |
| `SELECT * FROM users WHERE age > 18 AND active = true` | `BoolExpr` | Boolean expressions | ✅ 1 (AND) |
| `INSERT INTO users (name) VALUES ('John')` | `InsertStmt` | INSERT statements | ✅ 1 |
| `UPDATE users SET active = false` | `UpdateStmt` | UPDATE statements | ✅ 1 |
| `DELETE FROM users WHERE id = 1` | `DeleteStmt` | DELETE statements | ✅ 1 |
| `SELECT * FROM users u JOIN orders o ON u.id = o.user_id` | `JoinExpr` | JOIN operations | ✅ 1 |
| `SELECT name, CASE WHEN age > 18 THEN 'adult' ELSE 'minor' END` | `CaseExpr` | CASE expressions | ✅ 1 |
| `SELECT name FROM (SELECT * FROM users) subq` | `SubLink` | Subqueries | ✅ 1 |
| `WITH cte AS (SELECT * FROM users) SELECT * FROM cte` | `WithClause` | CTE definitions | ✅ 1 |
| `SELECT * FROM users UNION SELECT * FROM customers` | `SelectStmt` | UNION operations | ✅ 2 (both SELECTs) |
| `SELECT * FROM users WHERE name IS NULL` | `NullTest` | NULL tests | ✅ 1 |
| `SELECT * FROM users WHERE age BETWEEN 18 AND 65` | `A_Expr` | BETWEEN expressions | ✅ 1 |
| `SELECT DISTINCT name FROM users` | `SelectStmt` | DISTINCT queries | ✅ 1 |
| `SELECT * FROM users ORDER BY name LIMIT 10` | `LimitOffset` | LIMIT clauses | ✅ 1 |
| `SELECT * FROM users JOIN products ON users.id = products.user_id` | `(relname "users")` | Specific table by name | ✅ 1 (users only) |
| `SELECT id, name, email FROM customers` | `(colname "name")` | Specific column by name | ✅ 1 |
| `SELECT * FROM orders WHERE status = 'shipped'` | `(sval "shipped")` | Specific string constant | ✅ 1 |

### **Usage Examples**

```bash
# Find all constants in a query
./grepsql.sh -p "A_Const" --from-sql "SELECT 1, 'hello', true"
# Result: Found 3 matches

# Find all function calls
./grepsql.sh -p "FuncCall" --from-sql "SELECT COUNT(*), AVG(age) FROM users"
# Result: Found 2 matches

# Check if query has WHERE clause
./grepsql.sh -p "A_Expr" --from-sql "SELECT * FROM users WHERE age > 18"
# Result: Found 1 matches (indicates WHERE clause exists)

# Find JOIN operations
./grepsql.sh -p "JoinExpr" --from-sql "SELECT * FROM users u JOIN orders o ON u.id = o.user_id"
# Result: Found 1 matches

# Test with wildcard (any single node)
./grepsql.sh -p "_" --from-sql "SELECT id FROM users"
# Result: Found 1 matches (root node only)
```

### **Advanced Pattern Examples**
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

## Development Setup

This repository uses git submodules for the libpg_query dependency. After cloning:

```bash
# Clone the repository
git clone https://github.com/jonatas/pgquery-dotnet.git
cd pgquery-dotnet

# Initialize and fetch submodules
git submodule update --init --recursive

# Build the project
./scripts/build.sh
```

### Working with Submodules

The `libpg_query` directory is a git submodule pointing to the official [libpg_query repository](https://github.com/pganalyze/libpg_query). This keeps our repository lightweight while maintaining access to the PostgreSQL parser.

To update the submodule to a newer version:
```bash
cd libpg_query
git fetch origin
git checkout 17-latest  # or desired version
cd ..
git add libpg_query
git commit -m "Update libpg_query submodule"
```

### Advanced Pattern Matching

GrepSQL supports complex pattern combinations and precise node searches:

```bash
# Find queries with both column references AND constants
./grepsql.sh -p "{ColumnRef A_Const}" sample1.sql --highlight

# Output shows all SQL statements containing both patterns:
# sample1.sql:SELECT id, name, email
# FROM users
# WHERE active = true;
# 
# sample1.sql:INSERT INTO users (name, email, created_at)  
# VALUES ('John Doe', 'john@example.com', NOW());
# 
# sample1.sql:UPDATE users 
# SET last_login = NOW()
# WHERE id = 123;
```

**Pattern Types:**
- `{ColumnRef A_Const}` - Finds queries with column references AND constants
- `(relname "users")` - S-expression: finds specific table references  
- `ColumnRef` - Simple node type matching
- `SelectStmt` - Finds all SELECT statements

**Highlighting Options:**
- `--highlight` - ANSI colors for terminal
- `--highlight-style html` - HTML `<mark>` tags
- `--highlight-style markdown` - Markdown **bold** syntax
- `--context 2` - Show surrounding lines
