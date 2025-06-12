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

### 6. 🔄 **Enhanced PL/pgSQL Support (DO Statements)**
Advanced pattern matching inside PL/pgSQL blocks with dynamic SQL extraction:

```bash
# Find CREATE statements inside DO blocks
./grepsql.sh -p "CreateStmt" -f complex.sql

# Find any SQL pattern within PL/pgSQL
./grepsql.sh -p "IndexStmt" -f "**/*.sql" --highlight

# View AST structure including embedded SQL
./grepsql.sh -p "CreateStmt" -f dostmt.sql --tree
```

**Example PL/pgSQL File:**
```sql
DO $$
BEGIN
    CREATE TABLE historical_data (
        asset varchar(100) NOT NULL,
        timestamp TIMESTAMPTZ NOT NULL,
        wind_speed DOUBLE PRECISION NOT NULL
    );
    
    CREATE UNIQUE INDEX idx_historical_data
        ON historical_data(timestamp, asset);
        
    INSERT INTO historical_data VALUES ('WIND001', NOW(), 15.5);
END
$$;
```

**Enhanced Features:**
- ✅ **Dynamic DoStmt Detection**: Automatically detects DO statements during pattern matching
- ✅ **Intelligent SQL Extraction**: Extracts individual SQL statements from PL/pgSQL blocks
- ✅ **Multi-Statement Support**: Finds CREATE, INSERT, UPDATE, DELETE, SELECT within DO blocks
- ✅ **Complete AST Integration**: Extracted SQL is parsed and searchable like regular statements
- ✅ **Tree Visualization**: Full AST trees including embedded PL/pgSQL content
- ✅ **Smart Filtering**: Skips PL/pgSQL-specific syntax (RAISE, DECLARE, etc.)

```csharp
// C# API supports DO statements automatically
var sql = "DO $$ BEGIN CREATE TABLE users (id INT); END $$;";
var matches = SqlPatternMatcher.Search("CreateStmt", sql);
// Returns: 1 match (the CREATE TABLE inside the DO block)

// Multi-AST search across embedded statements
var results = SqlPatternMatcher.SearchInAsts("IndexStmt", parsedDoBlocks);
```

### 7. 🛡️ **Error Handling**
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

## 📖 **SQL Pattern Matching Syntax Reference**

Our SQL pattern matcher uses a **LISP-inspired s-expression syntax** similar to [jonatas/fast](https://github.com/jonatas/fast) and [rubocop-ast](https://docs.rubocop.org/rubocop-ast/), designed specifically for PostgreSQL AST navigation.

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

#### **Avoid Over-Broad Patterns**
```bash
# ❌ TOO BROAD - matches any node containing "users" text
(SelectStmt ... users)    

# ✅ STRUCTURED - matches table reference to "users"  
(SelectStmt ... (relname "users"))

# ✅ EVEN BETTER - full structure specification
(SelectStmt ... (RangeVar (relname "users")))
```

### **6. Capture Patterns: `$variable`**

Capture matched nodes for later analysis:

```bash
# Basic capture
(SelectStmt $stmt ...)                    # Capture the SELECT statement
(A_Const (ival $number))                  # Capture integer value

# Named captures for analysis
(SelectStmt ... (RangeVar (relname $table))) # Capture table name
($select_stmt ... (whereClause $condition))  # Capture statement and condition
```

### **7. Advanced Patterns**

#### **Parent Navigation: `^pattern`**
```bash
# Match based on parent context
^(SelectStmt ...)                        # Parent is SELECT statement
```

#### **Field Patterns: Attribute Matching**
```bash
# Match specific attributes within nodes
(relname "users")                        # Node with relname = "users"
(ival 42)                               # Node with ival = 42
(sval "password")                       # Node with sval = "password"
```

### **8. Practical Pattern Examples**

#### **Table Access Patterns**
```bash
# Find all table accesses
(RangeVar (relname _))

# Find specific table  
(RangeVar (relname "users"))

# Find tables in SELECT statements
(SelectStmt ... (RangeVar (relname _)))
```

#### **Column Patterns**
```bash
# Any column reference
ColumnRef

# Specific column by name
(ColumnRef (fields "password"))

# Column in WHERE clause
(whereClause ... (ColumnRef ...))
```

#### **Condition Patterns**
```bash
# Any WHERE clause
(whereClause ...)

# Specific comparison operators
(A_Expr (name ">"))                     # Greater than
(A_Expr (name "="))                     # Equals
(A_Expr (name "LIKE"))                  # LIKE operator

# Dangerous patterns
(... (A_Const (sval "password")))       # Hardcoded passwords
```

#### **JOIN Patterns**
```bash
# Any JOIN
JoinExpr

# Specific JOIN type
(JoinExpr (jointype "JOIN_INNER"))

# JOIN with specific table
(JoinExpr ... (RangeVar (relname "orders")))
```

#### **Function Call Patterns**
```bash
# Any function call
FuncCall

# Specific function
(FuncCall (funcname "COUNT"))

# Function with arguments
(FuncCall (funcname "SUBSTRING") (args ...))
```

### **9. Pattern Complexity Levels**

#### **Level 1: Simple Node Matching**
```bash
SelectStmt          # Find all SELECT statements
A_Const             # Find all constants
ColumnRef           # Find all column references
```

#### **Level 2: Structured Matching**
```bash
(SelectStmt ...)                        # SELECT with any content
(A_Const (ival _))                      # Any integer constant
(ColumnRef (fields _))                  # Any column reference
```

#### **Level 3: Deep Navigation**
```bash
(SelectStmt ... (whereClause ...))      # SELECT with WHERE
(... (A_Expr (name "=")))               # Any equality comparison
(SelectStmt ... (relname "users"))      # SELECT involving users table
```

#### **Level 4: Complex Logic**
```bash
# SELECT without WHERE clause
(SelectStmt !(whereClause ...))

# SELECT with specific table and condition
(SelectStmt ... (relname "users") ... (A_Expr (name ">")))

# Capture complex patterns
(SelectStmt $stmt ... (RangeVar (relname $table)) ... (whereClause $where))
```

### **10. Best Practices**

#### **✅ Recommended Patterns**
```bash
# Specific and structured
(SelectStmt ... (RangeVar (relname "users")))
(whereClause ... (A_Expr (name "=") ... (A_Const (sval "admin"))))
(ColumnRef (fields "password"))
```

#### **❌ Avoid These Patterns**
```bash
# Too broad - matches any node with text "users"
(SelectStmt ... users)

# Too vague - what kind of constant?
(SelectStmt ... _)

# Unstructured ellipsis
(... "password")
```

#### **🎯 Performance Tips**
```bash
# Start specific, then broaden
(SelectStmt ...)                        # Good: specific node type first
(... (whereClause ...))                 # Less optimal: ellipsis first

# Use field patterns for precision
(relname "users")                       # Good: field-specific
(... "users")                          # Less precise: text search
```

### **11. Real-World Security Patterns**

```bash
# SQL Injection Detection
(A_Const (sval _))                      # Hardcoded strings
(... (A_Expr (name "=") ... (A_Const (sval _))))  # Direct string comparisons

# Privilege Escalation
(SelectStmt ... (relname "users") ... (ColumnRef (fields "password")))

# Dangerous Functions
(FuncCall (funcname "EXECUTE"))         # Dynamic SQL execution
(FuncCall (funcname "COPY"))            # File system access

# Missing WHERE Clauses
(UpdateStmt !(whereClause ...))         # UPDATE without WHERE
(DeleteStmt !(whereClause ...))         # DELETE without WHERE
```

### **12. Integration Examples**

#### **Command Line Usage**
```bash
# Find all SELECT statements
./grepsql.sh -p "SelectStmt" -f "queries.sql"

# Find password-related queries  
./grepsql.sh -p "(... (ColumnRef (fields \"password\")))" -f "**/*.sql"

# Find hardcoded credentials
./grepsql.sh -p "(A_Const (sval _))" -f "auth.sql" --highlight
```

#### **C# API Usage**
```csharp
// Simple matching
bool hasSelect = SqlPatternMatcher.Match("SelectStmt", sql);

// Complex pattern matching
var pattern = "(SelectStmt ... (relname \"users\") ... (whereClause ...))";
var matches = SqlPatternMatcher.Search(pattern, sql);

// Security analysis
var hardcodedStrings = SqlPatternMatcher.Search("(A_Const (sval _))", sql);
```

This syntax provides **structured, precise AST navigation** while maintaining the flexibility of s-expression patterns. The key insight is that ellipsis (`...`) should be used with **structural patterns** like `(relname "users")` rather than bare text, ensuring patterns match the intended AST structures rather than arbitrary text occurrences.

## 📋 **Comprehensive Pattern Examples**

| SQL Query | Pattern | Description | Matches | Notes |
|-----------|---------|-------------|---------|-------|
| `SELECT id FROM users` | `_` | Any single node (root only) | ✅ 1 | Root SelectStmt node |
| `SELECT id FROM users` | `SelectStmt` | SELECT statements | ✅ 1 | Simple node type matching |
| `SELECT 1, 'hello', true` | `A_Const` | All constants | ✅ 3 | Numbers, strings, booleans |
| `SELECT * FROM users WHERE age > 25` | `A_Expr` | Expressions | ✅ 1 | The `age > 25` comparison |
| `SELECT COUNT(*), AVG(age)` | `FuncCall` | Function calls | ✅ 2 | COUNT and AVG functions |
| `SELECT u.name FROM users u` | `ColumnRef` | Column references | ✅ 1 | The `u.name` reference |
| `SELECT * FROM users WHERE age > 18 AND active = true` | `BoolExpr` | Boolean expressions | ✅ 1 | The AND operator |
| `INSERT INTO users (name) VALUES ('John')` | `InsertStmt` | INSERT statements | ✅ 1 | Simple node type matching |
| `UPDATE users SET active = false` | `UpdateStmt` | UPDATE statements | ✅ 1 | Simple node type matching |
| `DELETE FROM users WHERE id = 1` | `DeleteStmt` | DELETE statements | ✅ 1 | Simple node type matching |
| `SELECT * FROM users u JOIN orders o ON u.id = o.user_id` | `JoinExpr` | JOIN operations | ✅ 1 | The JOIN expression |
| `SELECT name, CASE WHEN age > 18 THEN 'adult' ELSE 'minor' END` | `CaseExpr` | CASE expressions | ✅ 1 | The CASE...WHEN construct |
| `SELECT name FROM (SELECT * FROM users) subq` | `SubLink` | Subqueries | ✅ 1 | The subquery in FROM |
| `WITH cte AS (SELECT * FROM users) SELECT * FROM cte` | `WithClause` | CTE definitions | ✅ 1 | The WITH clause |
| `SELECT * FROM users UNION SELECT * FROM customers` | `SelectStmt` | Multiple SELECTs | ✅ 2 | Both SELECT statements |
| `SELECT * FROM users WHERE name IS NULL` | `NullTest` | NULL tests | ✅ 1 | The `IS NULL` test |
| `SELECT * FROM users WHERE age BETWEEN 18 AND 65` | `A_Expr` | BETWEEN expressions | ✅ 1 | The BETWEEN construct |
| `SELECT DISTINCT name FROM users` | `SelectStmt` | DISTINCT queries | ✅ 1 | SELECT with DISTINCT |
| `SELECT * FROM users ORDER BY name LIMIT 10` | `LimitOffset` | LIMIT clauses | ✅ 1 | The LIMIT construct |

### **Structured Pattern Examples**

| SQL Query | Pattern | Description | Matches | Notes |
|-----------|---------|-------------|---------|-------|
| `SELECT * FROM users JOIN products ON users.id = products.user_id` | `(relname "users")` | Specific table by name | ✅ 1 | Only the "users" table |
| `SELECT id, name, email FROM customers` | `(ColumnRef (fields "name"))` | Specific column by name | ✅ 1 | Only the "name" column |
| `SELECT * FROM orders WHERE status = 'shipped'` | `(A_Const (sval "shipped"))` | Specific string constant | ✅ 1 | The "shipped" string |
| `SELECT * FROM users WHERE age = 25` | `(A_Const (ival 25))` | Specific integer constant | ✅ 1 | The number 25 |
| `SELECT * FROM users WHERE active = true` | `(A_Const (boolval true))` | Specific boolean constant | ✅ 1 | The boolean true |

### **Advanced Structural Patterns**

| SQL Query | Pattern | Description | Matches | Notes |
|-----------|---------|-------------|---------|-------|
| `SELECT * FROM users WHERE age > 18` | `(SelectStmt ... (whereClause ...))` | SELECT with WHERE | ✅ 1 | Any SELECT with WHERE clause |
| `SELECT * FROM users` | `(SelectStmt !(whereClause ...))` | SELECT without WHERE | ✅ 1 | SELECT missing WHERE clause |
| `SELECT * FROM users WHERE age > 18` | `(... (A_Expr (name ">")))` | Any comparison | ✅ 1 | The > operator anywhere |
| `SELECT u.id, u.name FROM users u` | `(SelectStmt ... (RangeVar (relname "users")))` | SELECT from specific table | ✅ 1 | SELECT specifically from users |
| `SELECT * FROM users WHERE role = 'admin'` | `(... (A_Expr (name "=") ... (A_Const (sval "admin"))))` | Equality with string | ✅ 1 | Comparison to "admin" |

### **Complex Security-Focused Patterns**

| SQL Query | Pattern | Description | Matches | Notes |
|-----------|---------|-------------|---------|-------|
| `SELECT * FROM users WHERE password = 'secret'` | `(... (ColumnRef (fields "password")) ... (A_Const (sval _)))` | Password comparison | ✅ 1 | Hardcoded password pattern |
| `UPDATE users SET role = 'admin'` | `(UpdateStmt !(whereClause ...))` | UPDATE without WHERE | ✅ 1 | Dangerous mass update |
| `DELETE FROM users` | `(DeleteStmt !(whereClause ...))` | DELETE without WHERE | ✅ 1 | Dangerous mass delete |
| `SELECT * FROM users; DROP TABLE users;` | `{SelectStmt InsertStmt UpdateStmt DeleteStmt}` | Any DML statement | ✅ 2 | Multiple statements detected |
| `COPY users TO '/tmp/data.csv'` | `(FuncCall (funcname "COPY"))` | File system access | ✅ 1 | Potential data exfiltration |

### **Enhanced DoStmt Pattern Examples**

| SQL Query | Pattern | Description | Matches | Notes |
|-----------|---------|-------------|---------|-------|
| `DO $$ BEGIN CREATE TABLE users (id INT); END $$;` | `CreateStmt` | CREATE inside DO | ✅ 1 | Finds embedded CREATE TABLE |
| `DO $$ BEGIN INSERT INTO users VALUES (1); END $$;` | `InsertStmt` | INSERT inside DO | ✅ 1 | Finds embedded INSERT |
| `DO $$ BEGIN CREATE INDEX idx ON users(id); END $$;` | `IndexStmt` | INDEX inside DO | ✅ 1 | Finds embedded INDEX creation |
| `DO $$ BEGIN UPDATE users SET active = true; END $$;` | `UpdateStmt` | UPDATE inside DO | ✅ 1 | Finds embedded UPDATE |
| `DO $$ BEGIN SELECT COUNT(*) FROM users; END $$;` | `SelectStmt` | SELECT inside DO | ✅ 1 | Finds embedded SELECT |
| `DO $$ BEGIN CREATE TABLE users (id INT); CREATE INDEX idx ON users(id); END $$;` | `CreateStmt` | Multiple CREATEs | ✅ 2 | Finds both embedded statements |
| `DO $$ BEGIN UPDATE users SET role = 'admin'; END $$;` | `(UpdateStmt !(whereClause ...))` | Dangerous UPDATE in DO | ✅ 1 | Finds dangerous mass update in DO block |
| Complex DO with mixed statements | `{CreateStmt InsertStmt UpdateStmt}` | Any DDL/DML in DO | ✅ Multiple | Finds all matching statement types |

### **Real-World Command Line Examples**

```bash
# Find all constants in a query
./grepsql.sh -p "A_Const" --from-sql "SELECT 1, 'hello', true"
# Result: Found 3 matches

# Find all function calls  
./grepsql.sh -p "FuncCall" --from-sql "SELECT COUNT(*), AVG(age) FROM users"
# Result: Found 2 matches

# Find SELECT statements with WHERE clauses
./grepsql.sh -p "(SelectStmt ... (whereClause ...))" --from-sql "SELECT * FROM users WHERE age > 18"
# Result: Found 1 matches

# Find hardcoded password comparisons
./grepsql.sh -p "(... (ColumnRef (fields \"password\")) ... (A_Const (sval _)))" -f "auth.sql" --highlight
# Result: Highlights dangerous password patterns

# Find dangerous UPDATE statements without WHERE
./grepsql.sh -p "(UpdateStmt !(whereClause ...))" -f "**/*.sql" --highlight
# Result: Finds potentially dangerous mass updates

# Test with wildcard (any single node)
./grepsql.sh -p "_" --from-sql "SELECT id FROM users"  
# Result: Found 1 matches (root node only)

# Enhanced DoStmt Examples
./grepsql.sh -p "CreateStmt" --from-sql "DO \$\$ BEGIN CREATE TABLE users (id INT); END \$\$;"
# Result: Found 1 matches (CREATE TABLE inside DO block)

./grepsql.sh -p "IndexStmt" -f complex_migration.sql --highlight
# Result: Finds CREATE INDEX statements both inside and outside DO blocks

./grepsql.sh -p "(relname \"historical_data\")" -f timestampdb.sql
# Result: Finds table references inside DO statements

./grepsql.sh -p "CreateStmt" -f migration.sql --tree
# Result: Shows complete AST including embedded SQL from DO blocks
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
6. **Enhanced DoStmt Processing** - Dynamic PL/pgSQL content extraction and parsing

### **DoStmt Processing Architecture**
Our enhanced DoStmt support provides real-time SQL extraction and parsing:

- **Dynamic Detection**: DoStmt nodes detected during search operations (no hardcoded patterns)
- **Intelligent Extraction**: `ExtractSqlStatementsFromPlPgSqlBlock()` parses PL/pgSQL blocks
- **Multi-Parser Strategy**: Uses both manual extraction and structured PL/pgSQL parsing
- **AST Integration**: Extracted SQL statements become searchable AST nodes
- **Wrapper Classes**: `DoStmtWrapper`, `PlPgSqlWrapper` for tracking embedded content
- **Smart Filtering**: Automatically skips PL/pgSQL-specific constructs (RAISE, DECLARE, etc.)
- **Multi-AST Support**: `SearchInAsts()` methods handle lists of parse trees
- **Tree Building**: Enhanced `--tree` support includes embedded PL/pgSQL structures

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
