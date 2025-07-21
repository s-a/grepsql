#include "libpg_query/pg_query.h"

// Protobuf wrapper functions (what the C# code expects)
PgQueryProtobufParseResult pg_query_parse_protobuf_wrapper(const char* input) {
    return pg_query_parse_protobuf(input);
}

PgQueryProtobufParseResult pg_query_parse_protobuf_opts_wrapper(const char* input, int parser_options) {
    return pg_query_parse_protobuf_opts(input, parser_options);
}

void pg_query_free_protobuf_parse_result_wrapper(PgQueryProtobufParseResult result) {
    pg_query_free_protobuf_parse_result(result);
}

PgQueryDeparseResult pg_query_deparse_protobuf_wrapper(PgQueryProtobuf parse_tree) {
    return pg_query_deparse_protobuf(parse_tree);
}

// Traditional parse wrapper functions (for compatibility)
PgQueryParseResult pg_query_parse_wrapper(const char* input) {
    return pg_query_parse(input);
}

void pg_query_free_parse_result_wrapper(PgQueryParseResult result) {
    pg_query_free_parse_result(result);
}

PgQueryNormalizeResult pg_query_normalize_wrapper(const char* input) {
    return pg_query_normalize(input);
}

void pg_query_free_normalize_result_wrapper(PgQueryNormalizeResult result) {
    pg_query_free_normalize_result(result);
}
