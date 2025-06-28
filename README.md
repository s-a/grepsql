# GrepSQL

A comprehensive .NET wrapper for libpg_query, providing PostgreSQL query parsing and advanced SQL pattern matching capabilities with a **clean, object-oriented architecture**.

## 📦 Quick Install

### Pre-built Binaries (Recommended)
Download ready-to-use binaries from [GitHub Releases](https://github.com/jonatas/grepsql/releases):

- **Linux (x64)**: `grepsql-linux-x64.tar.gz`
- **macOS (Intel)**: `grepsql-osx-x64.tar.gz` 
- **macOS (Apple Silicon)**: `grepsql-osx-arm64.tar.gz`
- **Windows (x64)**: `grepsql-win-x64.zip`

```bash
# Download and extract (example for Linux)
wget https://github.com/jonatas/grepsql/releases/latest/download/grepsql-linux-x64.tar.gz
tar -xzf grepsql-linux-x64.tar.gz
chmod +x GrepSQL

# Use immediately
./GrepSQL "SelectStmt" *.sql --highlight
```

> **🚀 Automated Releases**: Cross-platform binaries are automatically built and released when changes are merged to the main branch or when version tags are pushed.

### NuGet Package (.NET Library)
```bash
dotnet add package GrepSQL
```
## Features

### 1. 🔍 **GrepSQL - Command Line Tool**
Search through SQL files with powerful pattern matching:
```bash
# Search for all SELECT statements
./grepsql.sh "SelectStmt" --from-sql "SELECT id FROM users"

# Find specific table names with highlighting
./grepsql.sh "(relname \"users\")" --from-sql "SELECT * FROM users JOIN products ON users.id = products.user_id" --highlight

# Show AST structure
./grepsql.sh "SelectStmt" --from-sql "SELECT id FROM users" --tree

# Show expression tree for pattern debugging
./grepsql.sh "SelectStmt" --only-exp
```

### 2. 🧠 **Advanced SQL Pattern Matching**
Match complex SQL patterns with s-expression syntax:

#### **Basic Pattern Types**
```csharp
// Node type matching
PatternMatcher.Search("SelectStmt", sql);        // Find SELECT statements
PatternMatcher.Search("InsertStmt", sql);        // Find INSERT statements
PatternMatcher.Search("A_Const", sql);           // Find constants

// Wildcard matching
PatternMatcher.Search("_", sql);                 // Match any single node
PatternMatcher.Search("...", sql);               // Match any node with children
```

#### **Field-Based Pattern Matching**
```csharp
// Match specific table names
PatternMatcher.Search("(relname \"users\")", sql);

// Match any table name with wildcard
PatternMatcher.Search("(relname _)", sql);

// Match string constants
PatternMatcher.Search("(sval \"admin\")", sql);

// Match integer constants
PatternMatcher.Search("(ival 42)", sql);

// Match schema names
PatternMatcher.Search("(schemaname \"public\")", sql);
```

#### **Set Pattern Matching**
```csharp
// Match any of several table names
PatternMatcher.Search("(relname {users orders products})", sql);

// Match specific string values
PatternMatcher.Search("(sval {admin user guest})", sql);
```

#### **Nested Pattern Matching**
```csharp
// Match table references with specific names
PatternMatcher.Search("(RangeVar (relname \"users\"))", sql);

// Match any table reference
PatternMatcher.Search("(RangeVar (relname _))", sql);
```



### 3. ⚙️ **Core Query Parsing**
Parse PostgreSQL queries into an AST (Abstract Syntax Tree):
```csharp
var query = "SELECT id, name FROM users WHERE age > 25";
var result = Postgres.ParseSql(query);
// Access the AST through result.ParseTree

// Print formatted AST
Console.WriteLine(TreePrinter.Print(result.ParseTree));
```

### 4. 🛡️ **Error Handling**
Robust error handling with detailed information:
```csharp
try
{
    var result = Postgres.ParseSql("SELECT * FROM");
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
git clone https://github.com/jonatas/grepsql.git
cd grepsql
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
./grepsql.sh "SelectStmt" --from-sql "SELECT id FROM users"
```

4. Use in your .NET code:
```csharp
using GrepSQL.SQL;

var sql = "SELECT name FROM users WHERE age > 18";
var matches = PatternMatcher.Search("(relname _)", sql);
Console.WriteLine($"Found {matches.Count} table references");
```

## 📖 **SQL Pattern Matching Syntax Reference**

Our SQL pattern matcher uses a **LISP-inspired s-expression syntax** designed specifically for PostgreSQL AST navigation.

### **Core Philosophy: Structured Navigation**

Unlike simple string matching, our patterns navigate the **Abstract Syntax Tree (AST)** with precise structure:

```sql
-- SQL: SELECT name FROM users WHERE age > 18
-- AST Structure (simplified):
(SelectStmt 
  (targetList 
    (ResTarget (val (ColumnRef (fields (String "name"))))))
  (fromClause 
    (RangeVar (relname "users")))
  (whereClause 
    (A_Expr (name (String ">")) (lexpr (ColumnRef ...)) (rexpr (A_Const (ival 18))))))
```

### **1. Basic Patterns**

| Pattern | Description | Example |
|---------|-------------|---------|
| `NodeType` | Match any node of this type | `SelectStmt` |
| `_` | Match any single node | `_` |
| `nil` | Match exactly null/empty | `nil` |

### **2. Wildcard Patterns** 

| Pattern | Description | SQL Example | Matches |
|---------|-------------|-------------|---------|
| `_` | Any single node (root only) | `SELECT id` | ✅ 1 (root SelectStmt) |
| `...` | Any node with children | `SELECT id` | ✅ All non-leaf nodes |

### **3. Structural Patterns**

#### **S-Expression Structure: `(head children...)`**
```bash
# Basic structure matching
(SelectStmt ...)              # Any SELECT statement
(SelectStmt (targetList ...)) # SELECT with target list
(A_Const (ival _))           # Any integer constant
```

#### **Field-Specific Matching**
```bash
# Target specific fields within nodes
(RangeVar (relname "users"))     # Table named "users"
(ColumnRef (fields "name"))      # Column named "name"  
(A_Const (sval "admin"))         # String constant "admin"
(A_Const (ival 42))              # Integer constant 42
```

### **4. Logical Operators**

#### **Any Pattern: `{a b c}` (OR logic)**
```bash
# Match any of the specified patterns
{SelectStmt InsertStmt UpdateStmt}   # Any DML statement
(A_Const {ival sval boolval})        # Any constant type
```

#### **All Pattern: `[a b c]` (AND logic)**  
```bash
# All conditions must be true
[SelectStmt (whereClause ...)]       # SELECT with WHERE clause
[ColumnRef (fields "id")]            # Column reference to "id"
```

#### **Negation: `!pattern`**
```bash
# Pattern must NOT match
!(whereClause ...)                   # No WHERE clause
(SelectStmt !(joinClause ...))       # SELECT without JOINs
```

#### **Maybe: `?pattern`**
```bash
# Optional pattern (may be null)
(SelectStmt ?(whereClause ...))      # SELECT optionally with WHERE
```

### **5. Ellipsis Navigation: `(...)`**

**Critical**: Ellipsis provides **structured traversal**, not arbitrary text matching.

#### **Correct Ellipsis Usage**
```bash
# Find pattern anywhere in subtree structure
(SelectStmt ... (relname "users"))           # SELECT containing table "users"
(... (whereClause (A_Expr ...)))             # Any query with WHERE expression
(SelectStmt ... (A_Const (ival 42)))         # SELECT containing integer 42
```

#### **Ellipsis with Structure: `(... pattern)`**
```bash
# More precise: ellipsis + structured pattern
(SelectStmt ... (RangeVar (relname "users"))) # SELECT with table users
(... (ColumnRef (fields "password")))         # Any password column reference
(... (A_Expr (name ">")))                     # Any > comparison
```

## 📚 **API Reference**

### **PatternMatcher Class**

#### **Basic Matching**
```csharp
// Check if pattern matches
bool matches = PatternMatcher.Match(pattern, sql);

// Search for all matching nodes
List<IMessage> results = PatternMatcher.Search(pattern, sql);


```

#### **Advanced Features**
```csharp
// Enable debug output
PatternMatcher.SetDebug(true);

// Get detailed analysis
PatternAnalysisResult analysis = PatternMatcher.Analyze(pattern, sql);

// Get expression tree for pattern debugging
string expressionTree = PatternMatcher.GetExpressionTree(pattern);
```

#### **Node-Based Matching**
```csharp
// Work directly with AST nodes
var parseResult = Postgres.ParseSql(sql);
var node = parseResult.ParseTree.Stmts[0].Stmt;

bool matches = PatternMatcher.Match(node, pattern);
List<IMessage> results = PatternMatcher.Search(node, pattern);

```

### **Postgres Class**

#### **SQL Parsing**
```csharp
// Parse SQL into AST
ParseResult result = Postgres.ParseSql(sql);

// Search for patterns in SQL
List<IMessage> matches = Postgres.SearchInSql(pattern, sql);

// Search across multiple ASTs
List<IMessage> matches = Postgres.SearchInAsts(pattern, parseResults);
```

#### **Attribute Information**
```csharp
// Get all known PostgreSQL attributes
HashSet<string> attributes = Postgres.AttributeNames;

// Check if attribute is known
bool isKnown = Postgres.IsKnownAttribute("relname");

// Enable debug output
Postgres.SetDebug(true);
```

## 🔧 **Command Line Options**

The `grepsql.sh` script supports the following options:

```bash
# Basic usage
./grepsql.sh "pattern" [files...] [options]

# Pattern specification
-p, --pattern          Pattern to match
--from-sql             Inline SQL instead of files

# Output control
--ast                  Print AST instead of SQL
--tree                 Print AST as formatted tree
--tree-mode            Tree display mode: clean (default) or full
-c, --count            Only print count of matches

-X, --only-exp         Show only expression tree

# Highlighting and formatting
--highlight            Highlight matching SQL parts
--highlight-style      Style: ansi, html, markdown
--context              Show context lines around matches
-n, --line-numbers     Show line numbers
--no-filename          Don't show filename

# Debug and verbose
--debug                Print matching details
--verbose              Enable verbose debug output
--no-color             Disable colored output
```

## 🧪 **Examples**

### **Finding Security Issues**
```bash
# Find hardcoded passwords
./grepsql.sh "(sval \"password\")" *.sql

# Find admin access patterns
./grepsql.sh "(sval \"admin\")" *.sql --highlight
```

### **Performance Analysis**
```bash
# Find all table references
./grepsql.sh "(relname _)" *.sql

# Find all SELECT statements
./grepsql.sh "SelectStmt" *.sql --tree
```

### **Code Quality**
```bash
# Find magic numbers
./grepsql.sh "(ival _)" *.sql

# Find SELECT * patterns
./grepsql.sh "A_Star" *.sql --highlight
```

### **Migration Planning**
```bash
# Extract all table references
./grepsql.sh "(relname _)" migration.sql

# Find schema references
./grepsql.sh "(schemaname _)" *.sql
```


## 🤝 **Contributing**

We welcome contributions! Our codebase follows modern .NET development practices.

### **Development Setup**
1. Fork the repository

## 📄 **License**

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 **Acknowledgments**

- [libpg_query](https://github.com/pganalyze/libpg_query) - PostgreSQL query parsing
- [jonatas/fast](https://github.com/jonatas/fast) - Ruby AST pattern matching inspiration
- [rubocop-ast](https://docs.rubocop.org/rubocop-ast/) - AST navigation patterns
