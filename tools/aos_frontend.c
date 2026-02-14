#include <ctype.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct {
    int line;
    int col;
    const char *code;
    const char *message;
} ParseError;

typedef enum {
    TK_EOF,
    TK_IDENT,
    TK_STRING,
    TK_INT,
    TK_TRUE,
    TK_FALSE,
    TK_HASH,
    TK_LPAREN,
    TK_RPAREN,
    TK_LBRACE,
    TK_RBRACE,
    TK_EQ
} TokenKind;

typedef struct {
    TokenKind kind;
    char *text;
    int line;
    int col;
} Token;

typedef enum {
    AV_STRING,
    AV_INT,
    AV_BOOL,
    AV_IDENT
} AttrKind;

typedef struct {
    char *key;
    AttrKind kind;
    char *value;
} Attr;

typedef struct Node {
    char *kind;
    char *id;
    Attr *attrs;
    int attr_count;
    int attr_cap;
    struct Node **children;
    int child_count;
    int child_cap;
} Node;

typedef struct {
    const char *src;
    size_t len;
    size_t pos;
    int line;
    int col;
} Scanner;

typedef struct {
    Token *items;
    int count;
    int cap;
    int index;
} TokenList;

static void free_node(Node *n);

static void set_error(ParseError *err, int line, int col, const char *code, const char *message) {
    if (err->code == NULL) {
        err->line = line;
        err->col = col;
        err->code = code;
        err->message = message;
    }
}

static char *dup_slice(const char *s, size_t n) {
    char *out = (char *)malloc(n + 1);
    if (!out) {
        return NULL;
    }
    memcpy(out, s, n);
    out[n] = '\0';
    return out;
}

static void push_token(TokenList *list, Token t) {
    if (list->count == list->cap) {
        int next = list->cap == 0 ? 32 : list->cap * 2;
        Token *resized = (Token *)realloc(list->items, (size_t)next * sizeof(Token));
        if (!resized) {
            fprintf(stderr, "ERR PAR900 1 1 out of memory\n");
            exit(2);
        }
        list->items = resized;
        list->cap = next;
    }
    list->items[list->count++] = t;
}

static int is_kind_name(const char *s) {
    if (!s || !(*s)) {
        return 0;
    }
    if (!(isalpha((unsigned char)s[0]) || s[0] == '_')) {
        return 0;
    }
    for (int i = 1; s[i]; i++) {
        char c = s[i];
        if (!(isalnum((unsigned char)c) || c == '_')) {
            return 0;
        }
    }
    return 1;
}

static int is_id_name(const char *s) {
    if (!s || !(*s)) {
        return 0;
    }
    for (int i = 0; s[i]; i++) {
        char c = s[i];
        if (!(isalnum((unsigned char)c) || c == '_' || c == '-')) {
            return 0;
        }
    }
    return 1;
}

static char peek(Scanner *sc) {
    if (sc->pos >= sc->len) {
        return '\0';
    }
    return sc->src[sc->pos];
}

static char peek2(Scanner *sc) {
    if (sc->pos + 1 >= sc->len) {
        return '\0';
    }
    return sc->src[sc->pos + 1];
}

static char advance(Scanner *sc) {
    char c = peek(sc);
    if (c == '\0') {
        return c;
    }
    sc->pos++;
    if (c == '\n') {
        sc->line++;
        sc->col = 1;
    } else {
        sc->col++;
    }
    return c;
}

static void skip_ws_comments(Scanner *sc) {
    for (;;) {
        char c = peek(sc);
        if (c == '\0') {
            return;
        }
        if (isspace((unsigned char)c)) {
            advance(sc);
            continue;
        }
        if (c == '/' && peek2(sc) == '/') {
            while (peek(sc) != '\0' && peek(sc) != '\n') {
                advance(sc);
            }
            continue;
        }
        return;
    }
}

static int scan_tokens(const char *src, TokenList *out, ParseError *err) {
    Scanner sc;
    sc.src = src;
    sc.len = strlen(src);
    sc.pos = 0;
    sc.line = 1;
    sc.col = 1;

    while (1) {
        skip_ws_comments(&sc);
        int line = sc.line;
        int col = sc.col;
        char c = peek(&sc);
        if (c == '\0') {
            Token t = {TK_EOF, dup_slice("", 0), line, col};
            push_token(out, t);
            return 1;
        }

        if (c == '#') {
            advance(&sc);
            push_token(out, (Token){TK_HASH, dup_slice("#", 1), line, col});
            continue;
        }
        if (c == '(') { advance(&sc); push_token(out, (Token){TK_LPAREN, dup_slice("(", 1), line, col}); continue; }
        if (c == ')') { advance(&sc); push_token(out, (Token){TK_RPAREN, dup_slice(")", 1), line, col}); continue; }
        if (c == '{') { advance(&sc); push_token(out, (Token){TK_LBRACE, dup_slice("{", 1), line, col}); continue; }
        if (c == '}') { advance(&sc); push_token(out, (Token){TK_RBRACE, dup_slice("}", 1), line, col}); continue; }
        if (c == '=') { advance(&sc); push_token(out, (Token){TK_EQ, dup_slice("=", 1), line, col}); continue; }

        if (c == '"') {
            advance(&sc);
            size_t cap = 32;
            size_t len = 0;
            char *buf = (char *)malloc(cap);
            if (!buf) {
                set_error(err, line, col, "PAR900", "out of memory");
                return 0;
            }
            while (1) {
                char d = peek(&sc);
                if (d == '\0') {
                    free(buf);
                    set_error(err, line, col, "PAR010", "unterminated string");
                    return 0;
                }
                if (d == '"') {
                    advance(&sc);
                    break;
                }
                if (d == '\\') {
                    advance(&sc);
                    char e = peek(&sc);
                    if (e == '\0') {
                        free(buf);
                        set_error(err, line, col, "PAR010", "unterminated string");
                        return 0;
                    }
                    char outc = '\0';
                    if (e == 'n') outc = '\n';
                    else if (e == 'r') outc = '\r';
                    else if (e == 't') outc = '\t';
                    else if (e == '\\') outc = '\\';
                    else if (e == '"') outc = '"';
                    else {
                        free(buf);
                        set_error(err, sc.line, sc.col, "PAR011", "invalid string escape");
                        return 0;
                    }
                    advance(&sc);
                    if (len + 2 >= cap) {
                        cap *= 2;
                        char *next = (char *)realloc(buf, cap);
                        if (!next) {
                            free(buf);
                            set_error(err, line, col, "PAR900", "out of memory");
                            return 0;
                        }
                        buf = next;
                    }
                    buf[len++] = outc;
                } else {
                    advance(&sc);
                    if (len + 2 >= cap) {
                        cap *= 2;
                        char *next = (char *)realloc(buf, cap);
                        if (!next) {
                            free(buf);
                            set_error(err, line, col, "PAR900", "out of memory");
                            return 0;
                        }
                        buf = next;
                    }
                    buf[len++] = d;
                }
            }
            buf[len] = '\0';
            push_token(out, (Token){TK_STRING, buf, line, col});
            continue;
        }

        if (c == '-' && isdigit((unsigned char)peek2(&sc))) {
            size_t start = sc.pos;
            advance(&sc);
            while (isdigit((unsigned char)peek(&sc))) advance(&sc);
            size_t n = sc.pos - start;
            push_token(out, (Token){TK_INT, dup_slice(sc.src + start, n), line, col});
            continue;
        }
        if (isdigit((unsigned char)c)) {
            size_t start = sc.pos;
            while (isdigit((unsigned char)peek(&sc))) advance(&sc);
            size_t n = sc.pos - start;
            push_token(out, (Token){TK_INT, dup_slice(sc.src + start, n), line, col});
            continue;
        }

        if (isalpha((unsigned char)c) || c == '_') {
            size_t start = sc.pos;
            advance(&sc);
            while (1) {
                char d = peek(&sc);
                if (isalnum((unsigned char)d) || d == '_' || d == '.' || d == ',') {
                    advance(&sc);
                    continue;
                }
                break;
            }
            size_t n = sc.pos - start;
            char *txt = dup_slice(sc.src + start, n);
            TokenKind k = TK_IDENT;
            if (strcmp(txt, "true") == 0) k = TK_TRUE;
            else if (strcmp(txt, "false") == 0) k = TK_FALSE;
            push_token(out, (Token){k, txt, line, col});
            continue;
        }

        set_error(err, line, col, "PAR012", "unexpected character");
        return 0;
    }
}

static Token *peek_tok(TokenList *toks) {
    return &toks->items[toks->index];
}

static Token *advance_tok(TokenList *toks) {
    Token *t = &toks->items[toks->index];
    if (toks->index < toks->count - 1) {
        toks->index++;
    }
    return t;
}

static int match(TokenList *toks, TokenKind k) {
    if (peek_tok(toks)->kind == k) {
        advance_tok(toks);
        return 1;
    }
    return 0;
}

static Node *new_node(void) {
    Node *n = (Node *)calloc(1, sizeof(Node));
    return n;
}

static void push_attr(Node *n, Attr a) {
    if (n->attr_count == n->attr_cap) {
        int next = n->attr_cap == 0 ? 8 : n->attr_cap * 2;
        Attr *resized = (Attr *)realloc(n->attrs, (size_t)next * sizeof(Attr));
        if (!resized) {
            fprintf(stderr, "ERR PAR900 1 1 out of memory\n");
            exit(2);
        }
        n->attrs = resized;
        n->attr_cap = next;
    }
    n->attrs[n->attr_count++] = a;
}

static void push_child(Node *n, Node *child) {
    if (n->child_count == n->child_cap) {
        int next = n->child_cap == 0 ? 8 : n->child_cap * 2;
        Node **resized = (Node **)realloc(n->children, (size_t)next * sizeof(Node *));
        if (!resized) {
            fprintf(stderr, "ERR PAR900 1 1 out of memory\n");
            exit(2);
        }
        n->children = resized;
        n->child_cap = next;
    }
    n->children[n->child_count++] = child;
}

static Node *parse_node(TokenList *toks, ParseError *err);

static int expect(TokenList *toks, TokenKind kind, ParseError *err, const char *code, const char *msg) {
    Token *t = peek_tok(toks);
    if (t->kind != kind) {
        set_error(err, t->line, t->col, code, msg);
        return 0;
    }
    advance_tok(toks);
    return 1;
}

static AttrKind attr_kind_from_token(Token *t) {
    if (t->kind == TK_STRING) return AV_STRING;
    if (t->kind == TK_INT) return AV_INT;
    if (t->kind == TK_TRUE || t->kind == TK_FALSE) return AV_BOOL;
    return AV_IDENT;
}

static Node *parse_node(TokenList *toks, ParseError *err) {
    Token *kind = peek_tok(toks);
    if (kind->kind != TK_IDENT) {
        set_error(err, kind->line, kind->col, "PAR001", "expected node kind");
        return NULL;
    }
    if (!is_kind_name(kind->text)) {
        set_error(err, kind->line, kind->col, "PAR002", "invalid node kind");
        return NULL;
    }
    advance_tok(toks);
    char *id_text = NULL;
    if (match(toks, TK_HASH)) {
        Token *id = peek_tok(toks);
        if (id->kind != TK_IDENT && id->kind != TK_INT && id->kind != TK_TRUE && id->kind != TK_FALSE) {
            set_error(err, id->line, id->col, "PAR004", "expected node id");
            return NULL;
        }
        if (!is_id_name(id->text)) {
            set_error(err, id->line, id->col, "PAR005", "invalid node id");
            return NULL;
        }
        id_text = dup_slice(id->text, strlen(id->text));
        if (!id_text) {
            set_error(err, id->line, id->col, "PAR900", "out of memory");
            return NULL;
        }
        advance_tok(toks);
    } else {
        id_text = dup_slice("", 0);
        if (!id_text) {
            set_error(err, kind->line, kind->col, "PAR900", "out of memory");
            return NULL;
        }
    }

    Node *n = new_node();
    if (!n) {
        set_error(err, kind->line, kind->col, "PAR900", "out of memory");
        free(id_text);
        return NULL;
    }
    n->kind = dup_slice(kind->text, strlen(kind->text));
    n->id = id_text;

    if (match(toks, TK_LPAREN)) {
        while (peek_tok(toks)->kind != TK_RPAREN && peek_tok(toks)->kind != TK_EOF) {
            Token *k = peek_tok(toks);
            if (k->kind != TK_IDENT || !is_kind_name(k->text)) {
                set_error(err, k->line, k->col, "PAR006", "expected attribute name");
                free_node(n);
                return NULL;
            }
            advance_tok(toks);
            if (!expect(toks, TK_EQ, err, "PAR007", "expected '=' after attribute name")) {
                free_node(n);
                return NULL;
            }
            Token *v = peek_tok(toks);
            if (v->kind != TK_STRING && v->kind != TK_INT && v->kind != TK_TRUE && v->kind != TK_FALSE && v->kind != TK_IDENT) {
                set_error(err, v->line, v->col, "PAR008", "expected attribute value");
                free_node(n);
                return NULL;
            }
            advance_tok(toks);
            Attr a;
            a.key = dup_slice(k->text, strlen(k->text));
            a.kind = attr_kind_from_token(v);
            if (v->kind == TK_TRUE) {
                a.value = dup_slice("true", 4);
            } else if (v->kind == TK_FALSE) {
                a.value = dup_slice("false", 5);
            } else {
                a.value = dup_slice(v->text, strlen(v->text));
            }
            push_attr(n, a);
        }
        if (!expect(toks, TK_RPAREN, err, "PAR009", "expected ')' after attributes")) {
            free_node(n);
            return NULL;
        }
    }

    if (match(toks, TK_LBRACE)) {
        while (peek_tok(toks)->kind != TK_RBRACE && peek_tok(toks)->kind != TK_EOF) {
            Node *child = parse_node(toks, err);
            if (!child) {
                free_node(n);
                return NULL;
            }
            push_child(n, child);
        }
        if (!expect(toks, TK_RBRACE, err, "PAR010", "expected '}' after children")) {
            free_node(n);
            return NULL;
        }
    }

    return n;
}

static void free_tokens(TokenList *toks) {
    for (int i = 0; i < toks->count; i++) {
        free(toks->items[i].text);
    }
    free(toks->items);
}

static void free_node(Node *n) {
    if (!n) return;
    free(n->kind);
    free(n->id);
    for (int i = 0; i < n->attr_count; i++) {
        free(n->attrs[i].key);
        free(n->attrs[i].value);
    }
    free(n->attrs);
    for (int i = 0; i < n->child_count; i++) {
        free_node(n->children[i]);
    }
    free(n->children);
    free(n);
}

static void write_lp(FILE *out, const char *s) {
    size_t n = strlen(s);
    fprintf(out, "%zu:", n);
    if (n > 0) fwrite(s, 1, n, out);
}

static void emit_node(FILE *out, Node *n) {
    fprintf(out, "N ");
    write_lp(out, n->kind);
    fputc(' ', out);
    write_lp(out, n->id);
    fprintf(out, " %d %d\n", n->attr_count, n->child_count);
    for (int i = 0; i < n->attr_count; i++) {
        Attr *a = &n->attrs[i];
        fprintf(out, "A ");
        write_lp(out, a->key);
        fputc(' ', out);
        char t = 'D';
        if (a->kind == AV_STRING) t = 'S';
        else if (a->kind == AV_INT) t = 'I';
        else if (a->kind == AV_BOOL) t = 'B';
        fputc(t, out);
        fputc(' ', out);
        write_lp(out, a->value);
        fputc('\n', out);
    }
    for (int i = 0; i < n->child_count; i++) {
        emit_node(out, n->children[i]);
    }
    fprintf(out, "E\n");
}

static char *read_stdin_all(void) {
    size_t cap = 4096;
    size_t len = 0;
    char *buf = (char *)malloc(cap);
    if (!buf) return NULL;
    while (!feof(stdin)) {
        if (len + 2048 >= cap) {
            cap *= 2;
            char *next = (char *)realloc(buf, cap);
            if (!next) {
                free(buf);
                return NULL;
            }
            buf = next;
        }
        size_t n = fread(buf + len, 1, 2048, stdin);
        len += n;
    }
    buf[len] = '\0';
    return buf;
}

static char *read_file_all(const char *path) {
    FILE *f = fopen(path, "rb");
    if (!f) return NULL;
    if (fseek(f, 0, SEEK_END) != 0) { fclose(f); return NULL; }
    long sz = ftell(f);
    if (sz < 0) { fclose(f); return NULL; }
    if (fseek(f, 0, SEEK_SET) != 0) { fclose(f); return NULL; }
    char *buf = (char *)malloc((size_t)sz + 1);
    if (!buf) { fclose(f); return NULL; }
    size_t n = fread(buf, 1, (size_t)sz, f);
    fclose(f);
    buf[n] = '\0';
    return buf;
}

int main(int argc, char **argv) {
    char *source = NULL;
    if (argc >= 2 && strcmp(argv[1], "--stdin") == 0) {
        source = read_stdin_all();
    } else if (argc >= 2) {
        source = read_file_all(argv[1]);
    } else {
        fprintf(stderr, "ERR PAR000 1 1 missing input\n");
        return 2;
    }

    if (!source) {
        fprintf(stderr, "ERR PAR000 1 1 failed to read input\n");
        return 2;
    }

    ParseError err = {0, 0, NULL, NULL};
    TokenList toks;
    memset(&toks, 0, sizeof(toks));
    if (!scan_tokens(source, &toks, &err)) {
        fprintf(stderr, "ERR %s %d %d %s\n", err.code, err.line, err.col, err.message);
        free(source);
        free_tokens(&toks);
        return 2;
    }

    Node *root = parse_node(&toks, &err);
    if (!root) {
        fprintf(stderr, "ERR %s %d %d %s\n", err.code, err.line, err.col, err.message);
        free(source);
        free_tokens(&toks);
        return 2;
    }

    if (peek_tok(&toks)->kind != TK_EOF) {
        Token *t = peek_tok(&toks);
        fprintf(stderr, "ERR PAR013 %d %d unexpected tokens after root\n", t->line, t->col);
        free_node(root);
        free(source);
        free_tokens(&toks);
        return 2;
    }

    fprintf(stdout, "AOSAST1\n");
    emit_node(stdout, root);

    free_node(root);
    free(source);
    free_tokens(&toks);
    return 0;
}
