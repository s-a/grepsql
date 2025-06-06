# GrepSQL Enhancement Plan

## Problem Statement

Currently, the GrepSQL utility has two main limitations:

1. **Recursive Search Issue**: Patterns like `(relname users)` don't work unless prefixed with ellipsis `(... (relname users))`. The tool should search recursively through all AST nodes by default.

2. **Full Statement Output**: The tool currently prints entire SQL statements when a match is found, rather than just the relevant portion that matched the pattern.

## Research Findings

### Current Behavior Analysis

The issue is located in `src/PgQuery.NET/Analysis/SqlPatternMatcher.cs` in the `All` class's `Match` method (around line 551):

```csharp
// Handle the case where we start with ...
if (_expressions[0] is Find find && find.Token == "...")
{
    if (_debug) Log("Found leading ellipsis, searching globally for remaining patterns", true);
    // Skip the leading ellipsis and search for the remaining patterns anywhere in the tree
    var remainingPatterns = _expressions.Skip(1).ToArray();
    return SearchForPatternsInSubtree(node, remainingPatterns);
}
```

**Key Issues Identified:**
- Only patterns starting with `"..."` trigger `SearchForPatternsInSubtree()`
- Patterns without ellipsis try to match sequentially at the current node level only
- Field patterns like `(relname users)` should naturally search through the tree to find that field

### Test Case Verification

Using the sample SQL:
```sql
SELECT id, name, email FROM users WHERE active = true;
INSERT INTO users (name, email, created_at) VALUES ('John Doe', 'john@example.com', NOW());
UPDATE users SET last_login = NOW() WHERE id = 123;
```

- `./grepsql.sh -p "(... (relname users)" -f "sample1.sql"` ✅ Works (finds all 3 statements)
- `./grepsql.sh -p "(relname users)" -f "sample1.sql"` ❌ Fails (finds nothing)

## Enhancement Strategy

### Phase 1: Fix Recursive Search (Priority 1)

**Target**: Make patterns like `(relname users)` work without requiring explicit ellipsis.

**Approach**: Modify the `All` class's pattern matching logic to automatically search recursively when:
1. The pattern contains field names (like `relname`, `whereClause`, etc.)
2. The pattern doesn't start with a node type match at root level
3. The pattern is clearly a field-based search pattern

**Files to Modify:**
- `src/PgQuery.NET/Analysis/SqlPatternMatcher.cs`
  - `All.Match()` method (~line 539)
  - Add logic to detect field patterns and route them to recursive search
  - Consider patterns starting with field names as implicit recursive searches

**Implementation Plan:**
1. Add detection for field-based patterns in `All.Match()`
2. Route field patterns to `SearchForPatternsInSubtree()` automatically
3. Preserve existing ellipsis behavior for backward compatibility
4. Add comprehensive tests to verify the fix

### Phase 2: Partial SQL Output (Priority 2)

**Target**: Print only the SQL fragment that corresponds to the matching AST node, not the entire statement.

**Challenges:**
- PostgreSQL's libpg_query doesn't provide built-in AST-to-SQL conversion for partial nodes
- Need to track exact matching nodes and their SQL representation
- Complex nodes may not have clear SQL boundaries

**Approach Options:**
1. **Source Position Tracking**: Use pg_query's source position info to extract SQL fragments
2. **AST-to-SQL Reconstruction**: Build custom logic to convert specific AST nodes back to SQL
3. **Hybrid Approach**: Combine position tracking with smart SQL fragment extraction

**Files to Modify:**
- `src/GrepSQL/GrepSQL/Program.cs`
  - `SqlMatch` class to include matching node info
  - `PrintMatch()` method to output partial SQL
- `src/PgQuery.NET/Analysis/SqlPatternMatcher.cs`
  - Track matching nodes and their positions
  - Add methods to extract SQL fragments

### Phase 3: Testing & Validation

**Test Cases to Add:**
1. Field patterns without ellipsis: `(relname users)`, `(whereClause ...)`, `(targetList ...)`
2. Complex nested patterns: `(fromClause (JoinExpr (quals ...)))`
3. Mixed patterns with and without ellipsis
4. Edge cases: empty patterns, malformed SQL, etc.

**Files to Modify:**
- `tests/PgQuery.NET.Tests/SqlPatternMatcherTests.cs`
- Add comprehensive test suite for new recursive behavior

## Implementation Roadmap

### Sprint 1: Core Recursive Search Fix
- [ ] Analyze current `All.Match()` logic
- [ ] Implement field pattern detection
- [ ] Route field patterns to recursive search
- [ ] Basic testing and validation
- [ ] Ensure backward compatibility

### Sprint 2: Enhanced Pattern Support
- [ ] Support complex nested field patterns
- [ ] Optimize recursive search performance
- [ ] Add comprehensive test coverage
- [ ] Documentation updates

### Sprint 3: Partial SQL Output
- [ ] Research AST-to-SQL conversion approaches
- [ ] Implement source position tracking
- [ ] Add partial SQL extraction logic
- [ ] Integration with existing output system

### Sprint 4: Polish & Documentation
- [ ] Performance optimization
- [ ] Error handling improvements
- [ ] User documentation updates
- [ ] Examples and tutorials

## Success Criteria

### Phase 1 Success Metrics:
- `./grepsql.sh -p "(relname users)" -f "sample1.sql"` returns matches
- All existing ellipsis-based patterns continue to work
- No performance regression on large SQL files

### Phase 2 Success Metrics:
- Partial SQL output matches only the relevant AST node content
- Output is valid SQL that can be parsed independently
- User can understand which part of the original statement matched

## Risk Assessment

**Low Risk:**
- Phase 1 changes are localized to pattern matching logic
- Existing ellipsis behavior can be preserved

**Medium Risk:**
- Phase 2 requires significant new functionality
- AST-to-SQL conversion may be complex for some node types

**Mitigation Strategies:**
- Implement changes incrementally with extensive testing
- Maintain backward compatibility throughout
- Focus on common use cases first, add edge cases later 