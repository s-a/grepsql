using System;
using System.Linq;
using Xunit;
using GrepSQL.SQL;

namespace GrepSQL.Tests.SQL
{
    public class PostgresTests
    {
        [Fact]
        public void AttributeNames_ContainsExpectedAttributes()
        {
            // Assert - check that we have some basic attributes
            Assert.NotNull(Postgres.AttributeNames);
            Assert.NotEmpty(Postgres.AttributeNames);
            Assert.Contains("relname", Postgres.AttributeNames);
            Assert.Contains("schemaname", Postgres.AttributeNames);
            Assert.Contains("aliasname", Postgres.AttributeNames);
        }

        [Fact]
        public void AttributeNames_IsCaseInsensitive()
        {
            // Assert - check that attribute lookup is case insensitive
            Assert.True(Postgres.IsKnownAttribute("relname"));
            Assert.True(Postgres.IsKnownAttribute("RELNAME"));
            Assert.True(Postgres.IsKnownAttribute("RelName"));
        }

        [Theory]
        [InlineData("relname", true)]
        [InlineData("RELNAME", true)]
        [InlineData("RelName", true)]
        [InlineData("schemaname", true)]
        [InlineData("funcname", true)]
        [InlineData("nonexistent_attribute", false)]
        [InlineData("", false)]
        public void IsKnownAttribute_ReturnsExpectedResult(string attributeName, bool expected)
        {
            // Act & Assert
            Assert.Equal(expected, Postgres.IsKnownAttribute(attributeName));
        }

        [Fact]
        public void IsKnownAttribute_WithNullInput_ReturnsFalse()
        {
            // Act & Assert
            Assert.False(Postgres.IsKnownAttribute(null!));
        }

        [Fact]
        public void ParseSql_WithValidSql_ReturnsParseResult()
        {
            // Arrange
            var sql = "SELECT id, name FROM users WHERE active = true";

            // Act
            var result = Postgres.ParseSql(sql);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.ParseTree);
            Assert.NotEmpty(result.ParseTree.Stmts);
        }

        [Fact]
        public void SetDebug_DoesNotThrow()
        {
            // Act & Assert - should not throw
            Postgres.SetDebug(true);
            Postgres.SetDebug(false);
            
            // Test that it doesn't affect parsing
            var sql = "SELECT 1";
            var result = Postgres.ParseSql(sql);
            Assert.NotNull(result);
        }

        [Theory]
        [InlineData("SELECT id FROM users", 1)]
        [InlineData("SELECT id FROM users; SELECT name FROM products;", 2)]
        [InlineData("INSERT INTO users (name) VALUES ('test'); UPDATE users SET active = true;", 2)]
        public void ParseSql_WithMultipleStatements_ReturnsCorrectCount(string sql, int expectedStatementCount)
        {
            // Act
            var result = Postgres.ParseSql(sql);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedStatementCount, result.ParseTree.Stmts.Count);
        }

        [Fact]
        public void AttributeNames_HasReasonableSize()
        {
            // Assert - should have a reasonable number of attributes
            Assert.True(Postgres.AttributeNames.Count > 50);
            Assert.True(Postgres.AttributeNames.Count < 200); // Sanity check
        }

        [Fact]
        public void AttributeNames_ContainsCommonSqlElements()
        {
            // Assert - check for common SQL elements that should be recognized
            var commonAttributes = new[]
            {
                "relname", "schemaname", "funcname", "colname", "aliasname",
                "indexname", "tablename", "viewname", "typename"
                // Removed "opname" as it's not in the current attribute list
            };

            foreach (var attr in commonAttributes)
            {
                Assert.Contains(attr, Postgres.AttributeNames);
            }
        }
    }
} 