using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using PgQuery.NET;
using PgQuery.NET.AST.Pattern;
using PgQuery.NET.SQL;

namespace PgQuery.NET.Tests.AST.Pattern
{
    public class CorePatternTests
    {
        private readonly ITestOutputHelper _output;

        public CorePatternTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Basic Pattern Matching Tests

        [Fact]
        public void BasicPattern_SelectStmt_FindsSelectStatements()
        {
            // Arrange
            var sql = "SELECT * FROM users";
            var parseResult = PgQuery.Parse(sql);

            // Act
            var results = Match.Search(parseResult.ParseTree, "SelectStmt", debug: true);

            // Assert
            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Equal("SelectStmt", r.GetType().Name));
            
            _output.WriteLine($"Found {results.Count} SelectStmt nodes");
        }

        [Fact]
        public void BasicPattern_Wildcard_FindsMultipleNodes()
        {
            // Arrange
            var sql = "SELECT id FROM users";
            var parseResult = PgQuery.Parse(sql);

            // Act
            var results = Match.Search(parseResult.ParseTree, "_", debug: true);

            // Assert
            Assert.NotEmpty(results);
            Assert.True(results.Count > 5, $"Expected more than 5 nodes, got {results.Count}");
            
            _output.WriteLine($"Wildcard pattern found {results.Count} nodes");
        }

        [Fact]
        public void BasicPattern_HasChildren_FindsNodesWithChildren()
        {
            // Arrange
            var sql = "SELECT * FROM users";
            var parseResult = PgQuery.Parse(sql);

            // Act
            var results = Match.Search(parseResult.ParseTree, "...", debug: true);

            // Assert
            Assert.NotEmpty(results);
            
            _output.WriteLine($"HasChildren pattern found {results.Count} nodes");
        }

        [Fact]
        public void ComplexPattern_SelectWithRelname_FindsCorrectNodes()
        {
            // Arrange
            var sql = "SELECT * FROM users";
            var parseResult = PgQuery.Parse(sql);

            // Act
            var results = Match.Search(parseResult.ParseTree, "(SelectStmt ... (relname _))", debug: true);

            // Assert
            Assert.NotEmpty(results);
            
            _output.WriteLine($"Complex pattern found {results.Count} nodes");
        }

        [Fact]
        public void AttributePattern_Relname_FindsTableReferences()
        {
            // Arrange
            var sql = "SELECT * FROM users";
            var parseResult = PgQuery.Parse(sql);

            // Act
            var results = Match.Search(parseResult.ParseTree, "(relname _)", debug: true);

            // Assert
            Assert.NotEmpty(results);
            
            _output.WriteLine($"Relname pattern found {results.Count} nodes");
        }

        [Fact]
        public void CapturePattern_BasicCapture_WorksCorrectly()
        {
            // Arrange
            var sql = "SELECT id FROM users";
            var parseResult = PgQuery.Parse(sql);

            // Act
            var captures = Match.SearchWithCaptures(parseResult.ParseTree, "(relname $_)", debug: true);

            // Assert
            Assert.NotEmpty(captures);
            Assert.Contains("default", captures.Keys);
            Assert.Equal("users", captures["default"][0].ToString());
            
            _output.WriteLine($"Captured {captures.Count} groups");
            _output.WriteLine($"  default: {captures["default"].Count} items");
        }

        [Fact]
        public void CapturePattern_ComplexAnyCapture_WorksCorrectly()
        {
            // Arrange
            var sql = "SELECT users.name, posts.title FROM users JOIN posts ON users.id = posts.user_id";
            var parseResult = PgQuery.Parse(sql);

            // Act - Capture nodes that have either sval OR relname fields
            var captures = Match.SearchWithCaptures(parseResult.ParseTree, "$({sval relname} _)", debug: true);

            // Assert
            Assert.NotEmpty(captures);
            Assert.Contains("default", captures.Keys);
            
            // Should capture both column names and table names
            var capturedValues = captures["default"].Select(c => c.ToString()).ToList();
            
            // Should include table names like "users", "posts"
            Assert.Contains("users", capturedValues);
            Assert.Contains("posts", capturedValues);
            
            // Should include column names like "name", "title", "id", "user_id"
            Assert.Contains("name", capturedValues);
            Assert.Contains("title", capturedValues);
            Assert.Contains("id", capturedValues);
            Assert.Contains("user_id", capturedValues);
            
            _output.WriteLine($"Complex Any Capture found {captures["default"].Count} items:");
            for (int i = 0; i < captures["default"].Count; i++)
            {
                _output.WriteLine($"  [{i}]: {captures["default"][i]}");
            }
        }

        [Theory]
        [InlineData("SelectStmt")]
        [InlineData("_")]
        [InlineData("...")]
        [InlineData("(relname _)")]
        [InlineData("(SelectStmt ... (relname _))")]
        [InlineData("$({colname relname} _)")]
        public void ExpressionTree_ValidPatterns_ReturnsTree(string pattern)
        {
            // Act
            var tree = Match.GetExpressionTree(pattern);

            // Assert
            Assert.NotNull(tree);
            Assert.NotEmpty(tree);
            
            _output.WriteLine($"Pattern: {pattern}");
            _output.WriteLine($"Tree: {tree}");
        }

        [Fact]
        public void ExpressionTree_InvalidPattern_ReturnsError()
        {
            // Act
            var tree = Match.GetExpressionTree("(invalid");

            // Assert
            Assert.Contains("Error", tree);
            
            _output.WriteLine($"Error tree: {tree}");
        }

        #endregion

        #region Integration with PatternMatcher

        [Fact]
        public void PatternMatcher_BasicUsage_WorksCorrectly()
        {
            // Arrange
            var sql = "SELECT * FROM users";

            // Act
            var results = PatternMatcher.Search("SelectStmt", sql);

            // Assert
            Assert.NotEmpty(results);
            
            _output.WriteLine($"PatternMatcher found {results.Count} results");
        }

        [Fact]
        public void PatternMatcher_WithCaptures_WorksCorrectly()
        {
            // Arrange
            var sql = "SELECT * FROM a, b, c";

            // Act
            var captures = PatternMatcher.SearchWithCaptures("(relname $_)", sql);

            // Assert
            Assert.NotEmpty(captures);
            Assert.Contains("default", captures.Keys);
            Assert.Equal("a", captures["default"][0].ToString());
            Assert.Equal("b", captures["default"][1].ToString());
            Assert.Equal("c", captures["default"][2].ToString());
            
            _output.WriteLine($"PatternMatcher captured {captures.Count} groups");
        }

        [Theory]
        [InlineData("SELECT * FROM users", "SelectStmt", 1)]
        [InlineData("INSERT INTO users VALUES (1)", "InsertStmt", 1)]
        [InlineData("UPDATE users SET name = 'test'", "UpdateStmt", 1)]
        [InlineData("DELETE FROM users", "DeleteStmt", 1)]
        public void RealSQL_StatementTypes_FoundCorrectly(string sql, string pattern, int expectedMinCount)
        {
            // Arrange
            var parseResult = PgQuery.Parse(sql);

            // Act
            var results = Match.Search(parseResult.ParseTree, pattern, debug: true);

            // Assert
            Assert.True(results.Count >= expectedMinCount, 
                $"Expected at least {expectedMinCount} matches for '{pattern}' in '{sql}', got {results.Count}");
            
            _output.WriteLine($"SQL: {sql}");
            _output.WriteLine($"Pattern: {pattern}");
            _output.WriteLine($"Matches: {results.Count}");
        }

        [Fact]
        public void RealSQL_ComplexQuery_HandledCorrectly()
        {
            // Arrange
            var sql = @"
                SELECT u.id, u.name, p.title 
                FROM users u 
                JOIN posts p ON u.id = p.user_id 
                WHERE u.active = true 
                ORDER BY u.name";
            var parseResult = PgQuery.Parse(sql);

            // Act
            var selectResults = Match.Search(parseResult.ParseTree, "SelectStmt", debug: true);
            var joinResults = Match.Search(parseResult.ParseTree, "JoinExpr", debug: true);
            var relnameResults = Match.Search(parseResult.ParseTree, "(relname _)", debug: true);

            // Assert
            Assert.NotEmpty(selectResults);
            Assert.NotEmpty(joinResults);
            Assert.NotEmpty(relnameResults);
            
            _output.WriteLine($"Complex query analysis:");
            _output.WriteLine($"  SELECT statements: {selectResults.Count}");
            _output.WriteLine($"  JOIN expressions: {joinResults.Count}");
            _output.WriteLine($"  Table references: {relnameResults.Count}");
        }

        #endregion

        #region Performance Tests

        [Fact]
        public void Performance_LargeQuery_ProcessesQuickly()
        {
            // Arrange
            var sql = @"
                WITH user_stats AS (
                    SELECT u.id, u.name, COUNT(p.id) as post_count
                    FROM users u
                    LEFT JOIN posts p ON u.id = p.user_id
                    WHERE u.active = true
                    GROUP BY u.id, u.name
                )
                SELECT us.name, us.post_count,
                       CASE WHEN us.post_count > 10 THEN 'active'
                            WHEN us.post_count > 5 THEN 'moderate'
                            ELSE 'inactive' END as activity_level
                FROM user_stats us
                ORDER BY us.post_count DESC
                LIMIT 100";
            
            var parseResult = PgQuery.Parse(sql);

            // Act
            var startTime = DateTime.UtcNow;
            var results = Match.Search(parseResult.ParseTree, "_", debug: false);
            var duration = DateTime.UtcNow - startTime;

            // Assert
            Assert.NotEmpty(results);
            Assert.True(duration.TotalMilliseconds < 1000, 
                $"Search took {duration.TotalMilliseconds}ms, expected < 1000ms");
            
            _output.WriteLine($"Performance test: {results.Count} nodes found in {duration.TotalMilliseconds}ms");
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void ErrorHandling_InvalidPattern_ThrowsException()
        {
            // Arrange
            var sql = "SELECT * FROM users";
            var parseResult = PgQuery.Parse(sql);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => Match.Search(parseResult.ParseTree, "(invalid", debug: false));
        }

        [Fact]
        public void ErrorHandling_NullInput_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => Match.Search(null!, "SelectStmt", debug: false));
        }

        [Fact]
        public void ErrorHandling_EmptyPattern_ThrowsException()
        {
            // Arrange
            var sql = "SELECT * FROM users";
            var parseResult = PgQuery.Parse(sql);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => Match.Search(parseResult.ParseTree, "", debug: false));
        }

        #endregion
    }
} 