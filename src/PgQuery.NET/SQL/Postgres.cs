using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using PatternMatch = PgQuery.NET.Patterns.Match;

namespace PgQuery.NET.SQL
{
    /// <summary>
    /// PostgreSQL-specific utilities for parsing and analyzing PL/pgSQL code and PostgreSQL AST attributes.
    /// </summary>
    public static class Postgres
    {
        /// <summary>
        /// Comprehensive set of PostgreSQL AST attribute names used for pattern matching.
        /// </summary>
        public static readonly HashSet<string> AttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Table and relation names
            "relname", "schemaname", "aliasname", "tablename", "catalogname",
            
            // Column and field names  
            "colname", "fieldname", "attname", "resname",
            
            // Function and procedure names
            "funcname", "proname", "oprname", "aggname",
            
            // Type names
            "typename", "typname", "typnamespace",
            
            // Index and constraint names
            "indexname", "idxname", "constraintname", "conname",
            
            // General names and identifiers
            "name", "defname", "label", "alias", "objname",
            
            // String values
            "str", "sval", "val", "value", "strval",
            
            // Numeric values
            "ival", "fval", "dval", "location", "typemod",
            
            // Boolean values
            "boolval", "isnull", "islocal", "isnotnull", "unique", "primary",
            "deferrable", "initdeferred", "replace", "ifnotexists", "missingok",
            "concurrent", "temporary", "unlogged", "setof", "pcttype",
            
            // Access methods and storage
            "accessmethod", "tablespacename", "indexspace", "storage",
            
            // Constraint types and actions
            "contype", "fkmatchtype", "fkupdaction", "fkdelaction",
            
            // Expression and operator types
            "kind", "opno", "opfuncid", "opresulttype", "opcollid",
            
            // Language and format specifiers
            "language", "funcformat", "defaction",
            
            // Ordering and sorting
            "ordering", "nullsfirst", "nullslast",
            
            // Inheritance and OID references
            "inhcount", "typeoid", "colloid", "oldpktableoid",
            
            // Subquery and CTE names
            "ctename", "subquery", "withname",
            
            // Window function attributes
            "winname", "framestart", "frameend",
            
            // Trigger attributes
            "tgname", "tgfoid", "tgtype", "tgenabled",
            
            // Role and permission attributes
            "rolname", "grantor", "grantee", "privilege",
            
            // Database and schema attributes
            "datname", "nspname", "encoding", "collate", "ctype",
            
            // Sequence attributes
            "seqname", "increment", "minvalue", "maxvalue", "start", "cache",
            
            // View attributes
            "viewname", "viewquery", "materialized",
            
            // Extension and foreign data wrapper attributes
            "extname", "fdwname", "srvname", "usename",
            
            // Partition attributes
            "partitionkey", "partitionbound", "partitionstrategy",
            
            // Publication and subscription attributes
            "pubname", "subname", "publication", "subscription"
        };

        /// <summary>
        /// Comprehensive set of PostgreSQL AST node type names used for pattern matching.
        /// </summary>
        public static readonly HashSet<string> NodeTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Statement-level nodes
            "SelectStmt", "InsertStmt", "UpdateStmt", "DeleteStmt", "CreateStmt", "DropStmt",
            "AlterStmt", "TruncateStmt", "CopyStmt", "TransactionStmt", "ViewStmt", "IndexStmt",
            "RuleStmt", "CreateTableAsStmt", "RefreshMatViewStmt", "VariableSetStmt", "VariableShowStmt",
            "DoStmt", "CreateRoleStmt", "AlterRoleStmt", "DropRoleStmt", "CreatedbStmt", "AlterDatabaseStmt",
            "DropdbStmt", "VacuumStmt", "ExplainStmt", "CreateSeqStmt", "AlterSeqStmt", "VariableSetStmt",
            "NotifyStmt", "ListenStmt", "UnlistenStmt", "CommentStmt", "FetchStmt", "CheckPointStmt",
            "DiscardStmt", "LockStmt", "ConstraintsSetStmt", "ReindexStmt", "CreateConversionStmt",
            "CreateCastStmt", "CreateOpClassStmt", "CreateOpFamilyStmt", "AlterOpFamilyStmt", "PrepareStmt",
            "ExecuteStmt", "DeallocateStmt", "DeclareCursorStmt", "CreateTableSpaceStmt", "DropTableSpaceStmt",
            "AlterObjectDependsStmt", "AlterObjectSchemaStmt", "AlterOwnerStmt", "AlterOperatorStmt",
            "RenameStmt", "RuleStmt", "CreateForeignTableStmt", "ImportForeignSchemaStmt", "CreateExtensionStmt",
            "AlterExtensionStmt", "AlterExtensionContentsStmt", "CreateFdwStmt", "AlterFdwStmt", "CreateForeignServerStmt",
            "AlterForeignServerStmt", "CreateUserMappingStmt", "AlterUserMappingStmt", "DropUserMappingStmt",
            "AlterTableStmt", "AlterTableCmd", "AlterDomainStmt", "SetOperationStmt", "GrantStmt", "GrantRoleStmt",
            "AlterDefaultPrivilegesStmt", "ClosePortalStmt", "ClusterStmt", "CopyStmt", "CreateStmt", "DefineStmt",
            "CreateDomainStmt", "CreateFunctionStmt", "AlterFunctionStmt", "CreatePLangStmt", "CreateSchemaStmt",
            "CreateTrigStmt", "CreateEventTrigStmt", "AlterEventTrigStmt", "RefreshMatViewStmt", "ReplicaIdentityStmt",
            "AlterSystemStmt", "CreatePolicyStmt", "AlterPolicyStmt", "CreateTransformStmt", "CreateAmStmt",
            "CreatePublicationStmt", "AlterPublicationStmt", "CreateSubscriptionStmt", "AlterSubscriptionStmt",
            "DropSubscriptionStmt", "CreateStatsStmt", "AlterCollationStmt", "CallStmt", "AlterStatsStmt",
            
            // Expression-level nodes
            "A_Const", "A_Expr", "A_Indirection", "A_ArrayExpr", "A_Star", "Alias", "RangeVar", "ColumnRef",
            "FuncCall", "BoolExpr", "CaseExpr", "CaseWhen", "CaseTestExpr", "SubLink", "SubPlan", "AlternativeSubPlan",
            "FieldSelect", "FieldStore", "RelabelType", "CoerceViaIO", "ArrayCoerceExpr", "ConvertRowtypeExpr",
            "CollateExpr", "MinMaxExpr", "SQLValueFunction", "XmlExpr", "NullTest", "BooleanTest", "CoerceToDomain",
            "CoerceToDomainValue", "SetToDefault", "CurrentOfExpr", "NextValueExpr", "InferenceElem", "TargetEntry",
            "RangeTblRef", "FromExpr", "OnConflictExpr", "Query", "PlannedStmt", "Result", "ProjectSet", "ModifyTable",
            "Append", "MergeAppend", "RecursiveUnion", "BitmapAnd", "BitmapOr", "SeqScan", "SampleScan", "IndexScan",
            "IndexOnlyScan", "BitmapIndexScan", "BitmapHeapScan", "TidScan", "SubqueryScan", "FunctionScan",
            "ValuesScan", "TableFuncScan", "CteScan", "NamedTuplestoreScan", "WorkTableScan", "ForeignScan",
            "CustomScan", "NestLoop", "MergeJoin", "HashJoin", "Material", "Sort", "IncrementalSort", "Group",
            "Agg", "WindowAgg", "Unique", "Gather", "GatherMerge", "Hash", "SetOp", "LockRows", "Limit",
            
            "JoinExpr", "TypeName", "TypeCast", "ResTarget", "SortBy", "WindowDef", "RangeSubselect", "RangeFunction",
            "RangeTableSample", "RangeTableFunc", "CommonTableExpr", "WithClause", "InferClause", "OnConflictClause",
            "GroupingSet", "WindowClause", "PartitionElem", "PartitionSpec", "PartitionBoundSpec", "PartitionRangeDatum",
            "CreateTableAsStmt", "IntoClause", "Constraint", "DefElem", "RangeTblEntry", "RangeTblFunction", "TableSampleClause",
            
            // General AST nodes
            "Node", "List", "Value", "Integer", "Float", "String", "BitString", "Null", "ParseResult", "RawStmt",
            
            // Planner and executor nodes 
            "PlannedStmt", "Plan", "Result", "ProjectSet", "ModifyTable", "Append", "MergeAppend", "RecursiveUnion",
            "BitmapAnd", "BitmapOr", "Scan", "SeqScan", "SampleScan", "IndexScan", "IndexOnlyScan", "BitmapIndexScan",
            "BitmapHeapScan", "TidScan", "SubqueryScan", "FunctionScan", "ValuesScan", "TableFuncScan", "CteScan",
            "NamedTuplestoreScan", "WorkTableScan", "ForeignScan", "CustomScan", "Join", "NestLoop", "MergeJoin",
            "HashJoin", "Material", "Sort", "IncrementalSort", "Group", "Agg", "WindowAgg", "Unique", "Gather",
            "GatherMerge", "Hash", "SetOp", "LockRows", "Limit"
        };

        private static bool _debugEnabled = false;

        /// <summary>
        /// Enable or disable debug output for PostgreSQL operations.
        /// </summary>
        /// <param name="enable">True to enable debug output</param>
        public static void SetDebug(bool enable)
        {
            _debugEnabled = enable;
        }

        private static void DebugLog(string message)
        {
            if (_debugEnabled) Console.WriteLine(message);
        }

        /// <summary>
        /// Parse PL/pgSQL content and return both the PL/pgSQL AST and embedded SQL ASTs.
        /// This is a utility method that can be used independently of pattern matching.
        /// </summary>
        /// <param name="plpgsqlContent">PL/pgSQL code content</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>Tuple containing PL/pgSQL parse result and list of embedded SQL ASTs</returns>
        public static (PlpgsqlParseResult? PlpgsqlAst, List<ParseResult> EmbeddedSqlAsts) ParsePlpgsqlBlock(string plpgsqlContent, bool debug = false)
        {
            var originalDebugState = _debugEnabled;
            _debugEnabled = debug;
            
            try
            {
                if (string.IsNullOrEmpty(plpgsqlContent))
                {
                    return (null, new List<ParseResult>());
                }

                DebugLog($"[ParsePlpgsqlBlock] Processing PL/pgSQL content: {plpgsqlContent.Substring(0, Math.Min(100, plpgsqlContent.Length))}...");
                
                var embeddedSqlAsts = new List<ParseResult>();
                PlpgsqlParseResult? plpgsqlParseResult = null;

                try
                {
                    // Try protobuf-based parsing first
                    plpgsqlParseResult = PgQuery.ParsePlpgsqlProtobuf(plpgsqlContent);
                    
                    if (plpgsqlParseResult != null && plpgsqlParseResult.PlpgsqlFunctions != null)
                    {
                        DebugLog($"[ParsePlpgsqlBlock] Successfully parsed {plpgsqlParseResult.PlpgsqlFunctions.Count} PL/pgSQL functions via protobuf");
                        
                        // Extract embedded SQL statements from the protobuf result
                        var embeddedSqlStatements = ExtractSqlStatementsFromPlpgsqlProtobuf(plpgsqlParseResult);
                        
                        if (embeddedSqlStatements.Count > 0)
                        {
                            DebugLog($"[ParsePlpgsqlBlock] Found {embeddedSqlStatements.Count} embedded SQL statements");
                            
                            foreach (var sqlStatement in embeddedSqlStatements)
                            {
                                if (!string.IsNullOrEmpty(sqlStatement))
                                {
                                    try
                                    {
                                        DebugLog($"[ParsePlpgsqlBlock] Parsing embedded SQL: {sqlStatement.Substring(0, Math.Min(50, sqlStatement.Length))}...");
                                        var embeddedAst = ParseSql(sqlStatement);
                                        if (embeddedAst != null)
                                        {
                                            embeddedSqlAsts.Add(embeddedAst);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLog($"[ParsePlpgsqlBlock] Failed to parse embedded SQL: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog($"[ParsePlpgsqlBlock] Protobuf parsing failed: {ex.Message}");
                    
                    // Fallback to manual SQL extraction if protobuf parsing fails
                    var extractedSqlStatements = ExtractSqlStatementsFromPlPgSqlBlock(plpgsqlContent);
                    
                    if (extractedSqlStatements.Count > 0)
                    {
                        DebugLog($"[ParsePlpgsqlBlock] Fallback: Found {extractedSqlStatements.Count} SQL statements via manual extraction");
                        
                        foreach (var sqlStatement in extractedSqlStatements)
                        {
                            if (!string.IsNullOrEmpty(sqlStatement))
                            {
                                try
                                {
                                    var embeddedAst = ParseSql(sqlStatement);
                                    if (embeddedAst != null)
                                    {
                                        embeddedSqlAsts.Add(embeddedAst);
                                    }
                                }
                                catch (Exception ex2)
                                {
                                    DebugLog($"[ParsePlpgsqlBlock] Fallback failed to parse SQL: {ex2.Message}");
                                }
                            }
                        }
                    }
                }

                return (plpgsqlParseResult, embeddedSqlAsts);
            }
            finally
            {
                _debugEnabled = originalDebugState;
            }
        }

        /// <summary>
        /// Parse SQL and return parse result.
        /// </summary>
        /// <param name="sql">SQL string to parse</param>
        /// <returns>Parse result or null if parsing fails</returns>
        public static ParseResult? ParseSql(string sql)
        {
            try
            {
                return PgQuery.Parse(sql);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Check if a given attribute name is a known PostgreSQL AST attribute.
        /// </summary>
        /// <param name="attributeName">The attribute name to check</param>
        /// <returns>True if the attribute is a known PostgreSQL AST attribute</returns>
        public static bool IsKnownAttribute(string attributeName)
        {
            return AttributeNames.Contains(attributeName);
        }

        /// <summary>
        /// Check if a given name is a known PostgreSQL AST node type.
        /// </summary>
        /// <param name="nodeTypeName">The node type name to check</param>
        /// <returns>True if the name is a known PostgreSQL AST node type</returns>
        public static bool IsKnownNodeType(string nodeTypeName)
        {
            return NodeTypeNames.Contains(nodeTypeName);
        }

        // ==================== SQL-SPECIFIC PATTERN MATCHING ====================

        /// <summary>
        /// Search for patterns in SQL code, handling both regular SQL and PL/pgSQL blocks.
        /// </summary>
        /// <param name="pattern">The pattern to search for</param>
        /// <param name="sql">The SQL code to search in</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes</returns>
        public static List<IMessage> SearchInSql(string pattern, string sql, bool debug = false)
        {
            if (string.IsNullOrEmpty(sql) || string.IsNullOrEmpty(pattern))
                return new List<IMessage>();

            var oldDebug = _debugEnabled;
            if (debug) _debugEnabled = true;

            try
            {
                var ast = ParseSql(sql);
                if (ast?.ParseTree?.Stmts == null)
                    return new List<IMessage>();

                var results = new List<IMessage>();
                
                foreach (var stmt in ast.ParseTree.Stmts)
                {
                    if (stmt?.Stmt != null)
                    {
                        // Use our new pattern matching system directly
                        var stmtResults = PatternMatch.Search(stmt.Stmt, pattern, debug);
                        results.AddRange(stmtResults);
                    }
                }

                return results;
            }
            finally
            {
                _debugEnabled = oldDebug;
            }
        }

        /// <summary>
        /// Search for patterns across multiple SQL parse results.
        /// </summary>
        /// <param name="pattern">The pattern to search for</param>
        /// <param name="asts">The parse results to search in</param>
        /// <param name="debug">Enable debug output</param>
        /// <returns>List of matching nodes from all ASTs</returns>
        public static List<IMessage> SearchInAsts(string pattern, IEnumerable<ParseResult> asts, bool debug = false)
        {
            if (asts == null || string.IsNullOrEmpty(pattern))
                return new List<IMessage>();

            var oldDebug = _debugEnabled;
            if (debug) _debugEnabled = true;

            try
            {
                var results = new List<IMessage>();
                var rootNodes = new List<IMessage>();

                foreach (var ast in asts)
                {
                    if (ast?.ParseTree?.Stmts != null)
                    {
                        foreach (var stmt in ast.ParseTree.Stmts)
                        {
                            if (stmt?.Stmt != null)
                            {
                                rootNodes.Add(stmt.Stmt);
                            }
                        }
                    }
                }

                foreach (var rootNode in rootNodes)
                {
                    results.AddRange(PatternMatch.Search(rootNode, pattern, debug));
                }
                return results;
            }
            finally
            {
                _debugEnabled = oldDebug;
            }
        }

        /// <summary>
        /// Search in a single SQL node, with enhanced DoStmt handling for PL/pgSQL.
        /// </summary>
        private static void SearchInSqlNode(string pattern, IMessage node, List<IMessage> results)
        {
            if (node == null) return;

            // Enhanced DoStmt handling using the new utility method
            if (node.Descriptor?.Name == "DoStmt")
            {
                DebugLog($"[DoStmt] Found DoStmt node, processing PL/pgSQL content");
                var plpgsqlContent = ExtractSqlFromDoStmt(node);

                if (!string.IsNullOrEmpty(plpgsqlContent))
                {
                    DebugLog($"[DoStmt] Extracted PL/pgSQL content: {plpgsqlContent.Substring(0, Math.Min(100, plpgsqlContent.Length))}...");
                    var (plpgsqlAst, embeddedSqlAsts) = ParsePlpgsqlBlock(plpgsqlContent, _debugEnabled);

                    // Search in PL/pgSQL AST
                    if (plpgsqlAst?.PlpgsqlFunctions != null)
                    {
                        foreach (var plpgsqlFunction in plpgsqlAst.PlpgsqlFunctions)
                        {
                            var plpgsqlResults = PatternMatch.Search(plpgsqlFunction, pattern, _debugEnabled);
                            foreach (var result in plpgsqlResults)
                            {
                                results.Add(new DoStmtWrapper(result, plpgsqlContent));
                            }
                        }
                    }

                    // Search in embedded SQL ASTs
                    foreach (var embeddedAst in embeddedSqlAsts)
                    {
                        if (embeddedAst?.ParseTree?.Stmts != null)
                        {
                            foreach (var embeddedStmt in embeddedAst.ParseTree.Stmts)
                            {
                                if (embeddedStmt?.Stmt != null)
                                {
                                    var embeddedResults = PatternMatch.Search(embeddedStmt.Stmt, pattern, _debugEnabled);
                                    foreach (var result in embeddedResults)
                                    {
                                        results.Add(new DoStmtWrapper(result, plpgsqlContent));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Regular pattern matching for non-DoStmt nodes
            var nodeResults = PatternMatch.Search(node, pattern, _debugEnabled);
            results.AddRange(nodeResults);
        }

        /// <summary>
        /// Extract SQL content from a DoStmt node's dollar-quoted string.
        /// </summary>
        private static string? ExtractSqlFromDoStmt(IMessage doStmtNode)
        {
            try
            {
                DebugLog($"[ExtractSqlFromDoStmt] Starting extraction from DoStmt");

                var argsField = FindField(doStmtNode.Descriptor, "args");
                if (argsField != null)
                {
                    DebugLog($"[ExtractSqlFromDoStmt] Found args field");
                    var args = (System.Collections.IList?)argsField.Accessor.GetValue(doStmtNode);
                    if (args != null)
                    {
                        DebugLog($"[ExtractSqlFromDoStmt] Found {args.Count} args");
                        
                        foreach (var arg in args)
                        {
                            if (arg is IMessage argMessage)
                            {
                                DebugLog($"[ExtractSqlFromDoStmt] Processing arg: {argMessage.Descriptor?.Name}");
                                
                                var defElemField = FindField(argMessage.Descriptor, "def_elem");
                                if (defElemField != null)
                                {
                                    DebugLog($"[ExtractSqlFromDoStmt] Found def_elem field");
                                    var defElem = defElemField.Accessor.GetValue(argMessage) as IMessage;
                                    if (defElem != null && GetStringFieldValue(defElem, "defname") == "as")
                                    {
                                        DebugLog($"[ExtractSqlFromDoStmt] Found 'as' DefElem");
                                        var sval = GetStringFieldValue(defElem, "arg", "sval");
                                        if (!string.IsNullOrEmpty(sval))
                                        {
                                            DebugLog($"[ExtractSqlFromDoStmt] Extracted sval: {sval}");
                                            return sval;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        DebugLog($"[ExtractSqlFromDoStmt] args is null");
                    }
                }
                else
                {
                    DebugLog($"[ExtractSqlFromDoStmt] args field not found");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[ExtractSqlFromDoStmt] Exception: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Helper method to find a field by name in a message descriptor.
        /// </summary>
        private static FieldDescriptor? FindField(MessageDescriptor? descriptor, string fieldName)
        {
            if (descriptor == null) return null;
            return descriptor.Fields.InFieldNumberOrder().FirstOrDefault(f => f.Name == fieldName);
        }

        /// <summary>
        /// Helper method to get a string field value from a message, with support for nested paths.
        /// </summary>
        private static string? GetStringFieldValue(IMessage message, params string[] fieldPath)
        {
            if (message == null || fieldPath.Length == 0) return null;

            IMessage current = message;
            for (int i = 0; i < fieldPath.Length - 1; i++)
            {
                var field = FindField(current.Descriptor, fieldPath[i]);
                if (field == null) return null;
                
                var value = field.Accessor.GetValue(current);
                if (value is IMessage nextMessage)
                {
                    current = nextMessage;
                }
                else
                {
                    return null;
                }
            }

            var finalField = FindField(current.Descriptor, fieldPath[fieldPath.Length - 1]);
            if (finalField == null) return null;

            var finalValue = finalField.Accessor.GetValue(current);
            return finalValue?.ToString();
        }

        /// <summary>
        /// Wrapper class to track nodes that came from inside DoStmt parsing.
        /// </summary>
        public class DoStmtWrapper : IMessage
        {
            public IMessage InnerNode { get; }
            public string ExtractedSql { get; }

            public DoStmtWrapper(IMessage innerNode, string extractedSql)
            {
                InnerNode = innerNode ?? throw new ArgumentNullException(nameof(innerNode));
                ExtractedSql = extractedSql ?? string.Empty;
            }

            public MessageDescriptor Descriptor => InnerNode.Descriptor;
            public int CalculateSize() => InnerNode.CalculateSize();
            public void MergeFrom(CodedInputStream input) => InnerNode.MergeFrom(input);
            public void WriteTo(CodedOutputStream output) => InnerNode.WriteTo(output);
            public IMessage Clone() => new DoStmtWrapper(InnerNode, ExtractedSql);
        }

        /// <summary>
        /// Extract embedded SQL statements from PL/pgSQL protobuf parse result.
        /// </summary>
        private static List<string> ExtractSqlStatementsFromPlpgsqlProtobuf(PlpgsqlParseResult plpgsqlParseResult)
        {
            var sqlStatements = new List<string>();
            
            if (plpgsqlParseResult?.PlpgsqlFunctions == null) return sqlStatements;
            
            foreach (var function in plpgsqlParseResult.PlpgsqlFunctions)
            {
                ExtractSqlFromPlpgsqlFunction(function, sqlStatements);
            }
            
            return sqlStatements;
        }

        /// <summary>
        /// Extract SQL statements from a PL/pgSQL function node.
        /// </summary>
        private static void ExtractSqlFromPlpgsqlFunction(IMessage functionNode, List<string> sqlStatements)
        {
            if (functionNode?.Descriptor == null) return;
            
            DebugLog($"[ExtractSqlFromPlpgsqlFunction] Processing function: {functionNode.Descriptor.Name}");
            
            // Look for action field which contains the function body
            var actionField = functionNode.Descriptor.Fields.InFieldNumberOrder()
                .FirstOrDefault(f => f.Name.Equals("action", StringComparison.OrdinalIgnoreCase));
            
            if (actionField != null)
            {
                var actionValue = actionField.Accessor.GetValue(functionNode);
                if (actionValue is IMessage actionMessage)
                {
                    ExtractSqlFromPlpgsqlNode(actionMessage, sqlStatements);
                }
            }
        }

        /// <summary>
        /// Recursively extract SQL statements from PL/pgSQL AST nodes.
        /// </summary>
        private static void ExtractSqlFromPlpgsqlNode(IMessage plpgsqlNode, List<string> sqlStatements)
        {
            if (plpgsqlNode?.Descriptor == null) return;
            
            var nodeType = plpgsqlNode.Descriptor.Name;
            DebugLog($"[ExtractSqlFromPlpgsqlNode] Processing node: {nodeType}");
            
            // Look for SQL statement nodes
            if (nodeType.Contains("Sql") || nodeType.Contains("Query"))
            {
                // Try to extract SQL text from various possible fields
                var sqlFields = new[] { "query", "sqlstmt", "sql", "text", "stmt" };
                
                foreach (var fieldName in sqlFields)
                {
                    var field = plpgsqlNode.Descriptor.Fields.InFieldNumberOrder()
                        .FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                    
                    if (field != null)
                    {
                        var value = field.Accessor.GetValue(plpgsqlNode);
                        if (value is string sqlText && !string.IsNullOrWhiteSpace(sqlText))
                        {
                            DebugLog($"[ExtractSqlFromPlpgsqlNode] Found SQL in {fieldName}: {sqlText.Substring(0, Math.Min(50, sqlText.Length))}...");
                            sqlStatements.Add(sqlText.Trim());
                            return; // Found SQL, no need to continue
                        }
                    }
                }
            }
            
            // Recursively process child nodes
            foreach (var field in plpgsqlNode.Descriptor.Fields.InFieldNumberOrder())
            {
                if (field.IsRepeated)
                {
                    var list = (System.Collections.IList)field.Accessor.GetValue(plpgsqlNode);
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            if (item is IMessage child)
                            {
                                ExtractSqlFromPlpgsqlNode(child, sqlStatements);
                            }
                        }
                    }
                }
                else
                {
                    var value = field.Accessor.GetValue(plpgsqlNode);
                    if (value is IMessage child)
                    {
                        ExtractSqlFromPlpgsqlNode(child, sqlStatements);
                    }
                }
            }
        }

        /// <summary>
        /// Extract SQL statements from PL/pgSQL block using manual parsing.
        /// This is a fallback method when protobuf parsing fails.
        /// </summary>
        private static List<string> ExtractSqlStatementsFromPlPgSqlBlock(string plpgsqlContent)
        {
            var sqlStatements = new List<string>();
            
            if (string.IsNullOrEmpty(plpgsqlContent))
                return sqlStatements;
            
            DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Processing content");
            
            var lines = plpgsqlContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var currentStatement = new List<string>();
            var inSqlStatement = false;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("--"))
                    continue;
                
                // Check if this line starts a SQL statement
                if (IsStartOfSqlStatement(trimmedLine))
                {
                    // If we were already building a statement, finish it
                    if (inSqlStatement && currentStatement.Count > 0)
                    {
                        var statement = string.Join(" ", currentStatement).Trim();
                        if (IsParsableSqlStatement(statement))
                        {
                            DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Found SQL: {statement.Substring(0, Math.Min(50, statement.Length))}...");
                            sqlStatements.Add(statement);
                        }
                        currentStatement.Clear();
                    }
                    
                    inSqlStatement = true;
                    currentStatement.Add(trimmedLine);
                }
                else if (inSqlStatement)
                {
                    currentStatement.Add(trimmedLine);
                    
                    // Check if this line ends the SQL statement
                    if (trimmedLine.EndsWith(";"))
                    {
                        var statement = string.Join(" ", currentStatement).Trim();
                        if (IsParsableSqlStatement(statement))
                        {
                            DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Found SQL: {statement.Substring(0, Math.Min(50, statement.Length))}...");
                            sqlStatements.Add(statement);
                        }
                        currentStatement.Clear();
                        inSqlStatement = false;
                    }
                }
            }
            
            // Handle any remaining statement
            if (inSqlStatement && currentStatement.Count > 0)
            {
                var statement = string.Join(" ", currentStatement).Trim();
                if (IsParsableSqlStatement(statement))
                {
                    sqlStatements.Add(statement);
                }
            }
            
            DebugLog($"[ExtractSqlStatementsFromPlPgSqlBlock] Extracted {sqlStatements.Count} SQL statements");
            return sqlStatements;
        }

        /// <summary>
        /// Check if a line starts a SQL statement.
        /// </summary>
        private static bool IsStartOfSqlStatement(string line)
        {
            var sqlKeywords = new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "WITH", "CREATE", "DROP", "ALTER", "GRANT", "REVOKE" };
            var upperLine = line.ToUpperInvariant();
            
            return sqlKeywords.Any(keyword => upperLine.StartsWith(keyword + " ") || upperLine.Equals(keyword));
        }

        /// <summary>
        /// Check if a statement looks like parsable SQL.
        /// </summary>
        private static bool IsParsableSqlStatement(string statement)
        {
            if (string.IsNullOrWhiteSpace(statement))
                return false;
            
            // Remove common PL/pgSQL constructs that would make SQL unparsable
            var cleanStatement = statement
                .Replace("EXECUTE IMMEDIATE", "")
                .Replace("EXECUTE", "")
                .Trim();
            
            // Must start with a SQL keyword
            return IsStartOfSqlStatement(cleanStatement) && 
                   !cleanStatement.ToUpperInvariant().Contains("BEGIN") &&
                   !cleanStatement.ToUpperInvariant().Contains("END") &&
                   !cleanStatement.ToUpperInvariant().Contains("DECLARE");
        }
    }
} 