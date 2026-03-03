#include <errno.h>
#include <ctype.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <direct.h>
#include <process.h>
#include <sys/stat.h>
#include <windows.h>
#ifndef PATH_MAX
#define PATH_MAX 260
#endif
#define AIVM_PATH_SEP '\\'
#define AIVM_EXE_EXT ".exe"
#else
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>
#include <sys/wait.h>
#ifndef PATH_MAX
#define PATH_MAX 4096
#endif
#define AIVM_PATH_SEP '/'
#define AIVM_EXE_EXT ""
#endif

#include "aivm_c_api.h"

static int join_path(const char* left, const char* right, char* out, size_t out_len);
static int find_executable_on_path(const char* name, char* out, size_t out_len);

static int ends_with(const char* value, const char* suffix)
{
    size_t value_len;
    size_t suffix_len;
    if (value == NULL || suffix == NULL) {
        return 0;
    }
    value_len = strlen(value);
    suffix_len = strlen(suffix);
    if (value_len < suffix_len) {
        return 0;
    }
    return strcmp(value + (value_len - suffix_len), suffix) == 0;
}

static int starts_with(const char* value, const char* prefix)
{
    size_t prefix_len;
    if (value == NULL || prefix == NULL) {
        return 0;
    }
    prefix_len = strlen(prefix);
    return strncmp(value, prefix, prefix_len) == 0;
}

static int file_exists(const char* path)
{
    struct stat st;
    if (path == NULL || stat(path, &st) != 0) {
        return 0;
    }
#ifdef _WIN32
    return (st.st_mode & _S_IFREG) != 0;
#else
    return S_ISREG(st.st_mode);
#endif
}

static int directory_exists(const char* path)
{
    struct stat st;
    if (path == NULL || stat(path, &st) != 0) {
        return 0;
    }
#ifdef _WIN32
    return (st.st_mode & _S_IFDIR) != 0;
#else
    return S_ISDIR(st.st_mode);
#endif
}

static int ensure_directory(const char* path)
{
    if (path == NULL) {
        return 0;
    }
    if (directory_exists(path)) {
        return 1;
    }
#ifdef _WIN32
    if (_mkdir(path) == 0) {
#else
    if (mkdir(path, 0755) == 0) {
#endif
        return 1;
    }
    return errno == EEXIST;
}

static int copy_file(const char* src, const char* dst)
{
    FILE* in;
    FILE* out;
    unsigned char buffer[4096];
    size_t n;

    if (src == NULL || dst == NULL) {
        return 0;
    }

    in = fopen(src, "rb");
    if (in == NULL) {
        return 0;
    }
    out = fopen(dst, "wb");
    if (out == NULL) {
        fclose(in);
        return 0;
    }

    while ((n = fread(buffer, 1U, sizeof(buffer), in)) > 0U) {
        if (fwrite(buffer, 1U, n, out) != n) {
            fclose(in);
            fclose(out);
            return 0;
        }
    }

    fclose(in);
    fclose(out);
    return 1;
}

static int copy_runtime_file(const char* src, const char* dst)
{
    if (!copy_file(src, dst)) {
        return 0;
    }
#ifndef _WIN32
    if (chmod(dst, 0755) != 0) {
        return 0;
    }
#endif
    return 1;
}

static int write_text_file(const char* path, const char* content, int executable)
{
    FILE* out;
    size_t len;
    if (path == NULL || content == NULL) {
        return 0;
    }
    out = fopen(path, "wb");
    if (out == NULL) {
        return 0;
    }
    len = strlen(content);
    if (len > 0U && fwrite(content, 1U, len, out) != len) {
        fclose(out);
        return 0;
    }
    if (fclose(out) != 0) {
        return 0;
    }
#ifndef _WIN32
    if (executable != 0 && chmod(path, 0755) != 0) {
        return 0;
    }
#else
    (void)executable;
#endif
    return 1;
}

static int read_binary_file(const char* path, unsigned char** out_bytes, size_t* out_size)
{
    FILE* f;
    long length;
    unsigned char* bytes;
    size_t read_count;

    if (path == NULL || out_bytes == NULL || out_size == NULL) {
        return 0;
    }

    f = fopen(path, "rb");
    if (f == NULL) {
        return 0;
    }
    if (fseek(f, 0L, SEEK_END) != 0) {
        fclose(f);
        return 0;
    }
    length = ftell(f);
    if (length < 0) {
        fclose(f);
        return 0;
    }
    if (fseek(f, 0L, SEEK_SET) != 0) {
        fclose(f);
        return 0;
    }

    bytes = (unsigned char*)malloc((size_t)length);
    if (bytes == NULL && length > 0) {
        fclose(f);
        return 0;
    }

    read_count = fread(bytes, 1U, (size_t)length, f);
    fclose(f);

    if (read_count != (size_t)length) {
        free(bytes);
        return 0;
    }

    *out_bytes = bytes;
    *out_size = (size_t)length;
    return 1;
}

static int read_text_file(const char* path, char* out, size_t out_len)
{
    FILE* f;
    size_t n;

    if (path == NULL || out == NULL || out_len == 0U) {
        return 0;
    }

    f = fopen(path, "rb");
    if (f == NULL) {
        return 0;
    }

    n = fread(out, 1U, out_len - 1U, f);
    fclose(f);
    out[n] = '\0';
    return 1;
}

static int join_path(const char* left, const char* right, char* out, size_t out_len)
{
    int n;
    if (left == NULL || right == NULL || out == NULL || out_len == 0U) {
        return 0;
    }
    n = snprintf(out, out_len, "%s%c%s", left, AIVM_PATH_SEP, right);
    return n >= 0 && (size_t)n < out_len;
}

static int replace_ext(const char* path, const char* from_ext, const char* to_ext, char* out, size_t out_len)
{
    size_t path_len;
    size_t from_len;
    size_t to_len;

    if (path == NULL || from_ext == NULL || to_ext == NULL || out == NULL) {
        return 0;
    }

    path_len = strlen(path);
    from_len = strlen(from_ext);
    to_len = strlen(to_ext);

    if (path_len < from_len || strcmp(path + (path_len - from_len), from_ext) != 0) {
        return 0;
    }
    if (path_len - from_len + to_len + 1U > out_len) {
        return 0;
    }

    memcpy(out, path, path_len - from_len);
    memcpy(out + (path_len - from_len), to_ext, to_len);
    out[path_len - from_len + to_len] = '\0';
    return 1;
}

static int dirname_of(const char* path, char* out, size_t out_len)
{
    const char* slash;
    const char* bslash;
    size_t len;

    if (path == NULL || out == NULL || out_len == 0U) {
        return 0;
    }

    slash = strrchr(path, '/');
    bslash = strrchr(path, '\\');
    if (slash == NULL || (bslash != NULL && bslash > slash)) {
        slash = bslash;
    }

    if (slash == NULL) {
        if (out_len < 2U) {
            return 0;
        }
        out[0] = '.';
        out[1] = '\0';
        return 1;
    }

    len = (size_t)(slash - path);
    if (len == 0U) {
        if (out_len < 2U) {
            return 0;
        }
        out[0] = '.';
        out[1] = '\0';
        return 1;
    }

    if (len + 1U > out_len) {
        return 0;
    }
    memcpy(out, path, len);
    out[len] = '\0';
    return 1;
}

static int resolve_executable_path(const char* argv0, char* out, size_t out_len)
{
#ifdef _WIN32
    (void)argv0;
    if (out == NULL || out_len == 0U) {
        return 0;
    }
    return GetModuleFileNameA(NULL, out, (DWORD)out_len) > 0U;
#else
    if (out != NULL && out_len > 0U) {
        out[0] = '\0';
    }
    if (argv0 == NULL || out == NULL || out_len == 0U) {
        return 0;
    }
    if (realpath(argv0, out) != NULL) {
        return 1;
    }
    if (strchr(argv0, '/') == NULL && strchr(argv0, '\\') == NULL) {
        if (find_executable_on_path(argv0, out, out_len)) {
            return 1;
        }
    }
    return 0;
#endif
}

static int find_executable_on_path(const char* name, char* out, size_t out_len)
{
#ifdef _WIN32
    (void)name;
    (void)out;
    (void)out_len;
    return 0;
#else
    const char* path_env;
    const char* segment;
    const char* end;

    if (name == NULL || out == NULL || out_len == 0U) {
        return 0;
    }

    path_env = getenv("PATH");
    if (path_env == NULL || path_env[0] == '\0') {
        return 0;
    }

    segment = path_env;
    while (segment != NULL && segment[0] != '\0') {
        char candidate[PATH_MAX];
        size_t segment_len;
        end = strchr(segment, ':');
        segment_len = (end == NULL) ? strlen(segment) : (size_t)(end - segment);
        if (segment_len > 0U && segment_len < sizeof(candidate)) {
            if (segment_len + 1U + strlen(name) + 1U < sizeof(candidate)) {
                int written;
                memcpy(candidate, segment, segment_len);
                candidate[segment_len] = '\0';
                written = snprintf(candidate + segment_len, sizeof(candidate) - segment_len, "%c%s", AIVM_PATH_SEP, name);
                if (written > 0 && (size_t)written < (sizeof(candidate) - segment_len) &&
                    realpath(candidate, out) != NULL) {
                    return 1;
                }
            }
        }
        if (end == NULL) {
            break;
        }
        segment = end + 1;
    }
    return 0;
#endif
}

static int extract_manifest_attr(const char* text, const char* key, char* out, size_t out_len)
{
    char needle[64];
    const char* pos;
    const char* value_start;
    const char* value_end;

    if (text == NULL || key == NULL || out == NULL || out_len == 0U) {
        return 0;
    }

    if (snprintf(needle, sizeof(needle), "%s=\"", key) >= (int)sizeof(needle)) {
        return 0;
    }

    pos = strstr(text, needle);
    if (pos == NULL) {
        return 0;
    }

    value_start = pos + strlen(needle);
    value_end = strchr(value_start, '"');
    if (value_end == NULL) {
        return 0;
    }

    if ((size_t)(value_end - value_start) + 1U > out_len) {
        return 0;
    }

    memcpy(out, value_start, (size_t)(value_end - value_start));
    out[value_end - value_start] = '\0';
    return 1;
}

static int resolve_input_to_aibc1(const char* input, char* out_path, size_t out_len)
{
    char manifest_path[PATH_MAX];
    char manifest_text[8192];
    char project_dir[PATH_MAX];
    char entry_file[PATH_MAX];
    char entry_aos[PATH_MAX];

    if (input == NULL || out_path == NULL || out_len == 0U) {
        return 0;
    }

    if (ends_with(input, ".aibc1") && file_exists(input)) {
        if (snprintf(out_path, out_len, "%s", input) >= (int)out_len) {
            return 0;
        }
        return 1;
    }

    if (ends_with(input, ".aos")) {
        if (!replace_ext(input, ".aos", ".aibc1", out_path, out_len)) {
            return 0;
        }
        return file_exists(out_path);
    }

    if (ends_with(input, "project.aiproj")) {
        if (snprintf(manifest_path, sizeof(manifest_path), "%s", input) >= (int)sizeof(manifest_path)) {
            return 0;
        }
        if (!dirname_of(input, project_dir, sizeof(project_dir))) {
            return 0;
        }
    } else if (directory_exists(input)) {
        if (!join_path(input, "project.aiproj", manifest_path, sizeof(manifest_path))) {
            return 0;
        }
        if (snprintf(project_dir, sizeof(project_dir), "%s", input) >= (int)sizeof(project_dir)) {
            return 0;
        }
    } else {
        return 0;
    }

    if (!file_exists(manifest_path) || !read_text_file(manifest_path, manifest_text, sizeof(manifest_text))) {
        return 0;
    }

    if (!extract_manifest_attr(manifest_text, "entryFile", entry_file, sizeof(entry_file))) {
        return 0;
    }
    if (!join_path(project_dir, entry_file, entry_aos, sizeof(entry_aos))) {
        return 0;
    }
    if (!replace_ext(entry_aos, ".aos", ".aibc1", out_path, out_len)) {
        return 0;
    }
    return file_exists(out_path);
}

static int resolve_input_to_aos(const char* input, char* out_path, size_t out_len)
{
    char manifest_path[PATH_MAX];
    char manifest_text[8192];
    char project_dir[PATH_MAX];
    char entry_file[PATH_MAX];

    if (input == NULL || out_path == NULL || out_len == 0U) {
        return 0;
    }

    if (ends_with(input, ".aos") && file_exists(input)) {
        if (snprintf(out_path, out_len, "%s", input) >= (int)out_len) {
            return 0;
        }
        return 1;
    }

    if (ends_with(input, "project.aiproj")) {
        if (snprintf(manifest_path, sizeof(manifest_path), "%s", input) >= (int)sizeof(manifest_path)) {
            return 0;
        }
        if (!dirname_of(input, project_dir, sizeof(project_dir))) {
            return 0;
        }
    } else if (directory_exists(input)) {
        if (!join_path(input, "project.aiproj", manifest_path, sizeof(manifest_path))) {
            return 0;
        }
        if (snprintf(project_dir, sizeof(project_dir), "%s", input) >= (int)sizeof(project_dir)) {
            return 0;
        }
    } else {
        return 0;
    }

    if (!file_exists(manifest_path) || !read_text_file(manifest_path, manifest_text, sizeof(manifest_text))) {
        return 0;
    }
    if (!extract_manifest_attr(manifest_text, "entryFile", entry_file, sizeof(entry_file))) {
        return 0;
    }
    if (!join_path(project_dir, entry_file, out_path, out_len)) {
        return 0;
    }
    return file_exists(out_path);
}

static const char* path_basename_ptr(const char* path)
{
    const char* slash;
    const char* bslash;
    if (path == NULL) {
        return NULL;
    }
    slash = strrchr(path, '/');
    bslash = strrchr(path, '\\');
    if (slash == NULL || (bslash != NULL && bslash > slash)) {
        slash = bslash;
    }
    return (slash == NULL) ? path : (slash + 1);
}

static int strip_known_extension(const char* name, char* out, size_t out_len)
{
    size_t n;
    if (name == NULL || out == NULL || out_len == 0U) {
        return 0;
    }
    n = strlen(name);
    if (n >= 4U && memcmp(name + (n - 4U), ".aos", 4U) == 0) {
        n -= 4U;
    } else if (n >= 6U && memcmp(name + (n - 6U), ".aibc1", 6U) == 0) {
        n -= 6U;
    } else if (n >= 7U && memcmp(name + (n - 7U), ".aiproj", 7U) == 0) {
        n -= 7U;
    }
    if (n >= 3U && memcmp(name + (n - 3U), ".in", 3U) == 0) {
        n -= 3U;
    } else if (n >= 4U && memcmp(name + (n - 4U), ".out", 4U) == 0) {
        n -= 4U;
    }
    if (n == 0U || n + 1U > out_len) {
        return 0;
    }
    memcpy(out, name, n);
    out[n] = '\0';
    return 1;
}

static int sanitize_app_name(const char* input, char* out, size_t out_len)
{
    size_t i;
    size_t w = 0U;
    if (input == NULL || out == NULL || out_len == 0U) {
        return 0;
    }
    for (i = 0U; input[i] != '\0'; i += 1U) {
        char c = input[i];
        int keep = (c >= 'a' && c <= 'z') ||
                   (c >= 'A' && c <= 'Z') ||
                   (c >= '0' && c <= '9') ||
                   c == '_' || c == '-';
        if (!keep) {
            continue;
        }
        if (w + 1U >= out_len) {
            return 0;
        }
        out[w++] = c;
    }
    if (w == 0U) {
        if (out_len < 4U) {
            return 0;
        }
        memcpy(out, "app", 4U);
        return 1;
    }
    out[w] = '\0';
    return 1;
}

static int derive_publish_app_name(const char* program_input, char* out_name, size_t out_len)
{
    char manifest_path[PATH_MAX];
    char manifest_text[8192];
    char raw_name[PATH_MAX];
    const char* base;

    if (program_input == NULL || out_name == NULL || out_len == 0U) {
        return 0;
    }
    if (ends_with(program_input, "project.aiproj")) {
        if (read_text_file(program_input, manifest_text, sizeof(manifest_text)) &&
            extract_manifest_attr(manifest_text, "name", raw_name, sizeof(raw_name))) {
            return sanitize_app_name(raw_name, out_name, out_len);
        }
    } else if (directory_exists(program_input)) {
        if (!join_path(program_input, "project.aiproj", manifest_path, sizeof(manifest_path))) {
            return 0;
        }
        if (read_text_file(manifest_path, manifest_text, sizeof(manifest_text)) &&
            extract_manifest_attr(manifest_text, "name", raw_name, sizeof(raw_name))) {
            return sanitize_app_name(raw_name, out_name, out_len);
        }
    }
    base = path_basename_ptr(program_input);
    if (base == NULL || !strip_known_extension(base, raw_name, sizeof(raw_name))) {
        return sanitize_app_name("app", out_name, out_len);
    }
    return sanitize_app_name(raw_name, out_name, out_len);
}

static int resolve_manifest_path_for_input(const char* input, char* out_manifest, size_t out_len)
{
    if (input == NULL || out_manifest == NULL || out_len == 0U) {
        return 0;
    }
    if (ends_with(input, "project.aiproj") && file_exists(input)) {
        return snprintf(out_manifest, out_len, "%s", input) < (int)out_len;
    }
    if (directory_exists(input)) {
        if (!join_path(input, "project.aiproj", out_manifest, out_len)) {
            return 0;
        }
        return file_exists(out_manifest);
    }
    return 0;
}

static int pick_single_target_from_list(const char* csv, char* out_target, size_t out_len)
{
    const char* p;
    const char* first_start;
    const char* first_end;
    const char* second_start;
    size_t n;
    if (csv == NULL || out_target == NULL || out_len == 0U) {
        return 0;
    }

    p = csv;
    while (*p == ' ' || *p == '\t') {
        p += 1;
    }
    first_start = p;
    while (*p != '\0' && *p != ',') {
        p += 1;
    }
    first_end = p;
    while (first_end > first_start && (first_end[-1] == ' ' || first_end[-1] == '\t')) {
        first_end -= 1;
    }
    if (first_end <= first_start) {
        return 0;
    }
    if (*p == ',') {
        p += 1;
        while (*p == ' ' || *p == '\t') {
            p += 1;
        }
        second_start = p;
        if (*second_start != '\0') {
            return -1;
        }
    }

    n = (size_t)(first_end - first_start);
    if (n + 1U > out_len) {
        return 0;
    }
    memcpy(out_target, first_start, n);
    out_target[n] = '\0';
    return 1;
}

static int resolve_publish_target_from_manifest(const char* program_input, char* out_target, size_t out_len)
{
    char manifest_path[PATH_MAX];
    char manifest_text[8192];
    char target_value[128];
    int pick_rc;

    if (program_input == NULL || out_target == NULL || out_len == 0U) {
        return 0;
    }
    if (!resolve_manifest_path_for_input(program_input, manifest_path, sizeof(manifest_path))) {
        return 0;
    }
    if (!read_text_file(manifest_path, manifest_text, sizeof(manifest_text))) {
        return 0;
    }

    if (extract_manifest_attr(manifest_text, "publishTarget", target_value, sizeof(target_value))) {
        if (snprintf(out_target, out_len, "%s", target_value) >= (int)out_len) {
            return 0;
        }
        return 1;
    }
    if (extract_manifest_attr(manifest_text, "publishTargets", target_value, sizeof(target_value))) {
        pick_rc = pick_single_target_from_list(target_value, out_target, out_len);
        if (pick_rc == -1) {
            return -1;
        }
        return pick_rc;
    }

    return 0;
}

static const char* host_rid(void)
{
#ifdef _WIN32
#if defined(_M_ARM64) || defined(__aarch64__)
    return "windows-arm64";
#else
    return "windows-x64";
#endif
#elif defined(__APPLE__)
#if defined(__aarch64__) || defined(__arm64__)
    return "osx-arm64";
#else
    return "osx-x64";
#endif
#else
#if defined(__aarch64__) || defined(__arm64__)
    return "linux-arm64";
#else
    return "linux-x64";
#endif
#endif
}

static int parse_target_to_artifact(const char* rid, char* out_dir, size_t out_dir_len, char* out_bin, size_t out_bin_len)
{
    const char* platform;
    const char* arch;
    int n;

    if (rid == NULL || out_dir == NULL || out_bin == NULL) {
        return 0;
    }

    if (starts_with(rid, "osx-")) {
        platform = "osx";
        arch = rid + 4;
        if (strcmp(arch, "x64") != 0 && strcmp(arch, "arm64") != 0) {
            return 0;
        }
        n = snprintf(out_bin, out_bin_len, "airun");
    } else if (starts_with(rid, "linux-")) {
        platform = "linux";
        arch = rid + 6;
        if (strcmp(arch, "x64") != 0 && strcmp(arch, "arm64") != 0) {
            return 0;
        }
        n = snprintf(out_bin, out_bin_len, "airun");
    } else if (starts_with(rid, "windows-")) {
        platform = "windows";
        arch = rid + 8;
        if (strcmp(arch, "x64") != 0 && strcmp(arch, "arm64") != 0) {
            return 0;
        }
        n = snprintf(out_bin, out_bin_len, "airun.exe");
    } else if (strcmp(rid, "wasm32") == 0) {
        n = snprintf(out_bin, out_bin_len, "aivm-runtime-wasm32.wasm");
        if (n < 0 || (size_t)n >= out_bin_len) {
            return 0;
        }
        n = snprintf(out_dir, out_dir_len, ".artifacts/aivm-wasm32");
        return n >= 0 && (size_t)n < out_dir_len;
    } else {
        return 0;
    }

    if (n < 0 || (size_t)n >= out_bin_len) {
        return 0;
    }

    n = snprintf(out_dir, out_dir_len, ".artifacts/airun-%s-%s", platform, arch);
    return n >= 0 && (size_t)n < out_dir_len;
}

static void print_usage(void)
{
    fprintf(stderr,
        "Usage: airun <command> [options]\n"
        "\n"
        "Commands:\n"
        "  run <program(.aibc1|.aos|project-dir|project.aiproj)> [--vm=<selector>]\n"
        "  repl\n"
        "  bench [--iterations <n>] [--human]\n"
        "  debug run <program(.aibc1|.aos|project-dir|project.aiproj)> [--vm=<selector>]\n"
        "  publish <program(.aibc1|.aos|project-dir|project.aiproj)> [--target <rid>] [--out <dir>] [--wasm-profile <web|cli>]\n"
        "  version | --version\n"
        "\n"
        "VM selectors:\n"
        "  c      current stable C VM (default)\n"
        "  cvN    reserved future C VM profile/version selector (currently maps to c)\n");
}

static int print_unsupported_vm_mode(const char* mode)
{
    if (mode != NULL && strcmp(mode, "ast") == 0) {
        fprintf(stderr,
            "Err#err1(code=DEV008 message=\"AST VM mode is debug-only and not supported by native runtime.\" nodeId=vmMode)\n");
        return 2;
    }
    fprintf(stderr,
        "Err#err1(code=DEV008 message=\"Unsupported VM mode for native C runtime: %s\" nodeId=vmMode)\n",
        mode);
    return 2;
}

static int is_reserved_cv_selector(const char* mode)
{
    size_t i;
    if (mode == NULL || mode[0] != 'c' || mode[1] != 'v' || mode[2] == '\0') {
        return 0;
    }
    for (i = 2U; mode[i] != '\0'; i += 1U) {
        if (!isdigit((unsigned char)mode[i])) {
            return 0;
        }
    }
    return 1;
}

static int native_syscall_stdout_write_line(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    printf("%s\n", args[0].string_value);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_process_argv(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    (void)args;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 0U) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    *result = aivm_value_node(1);
    return AIVM_SYSCALL_OK;
}

static int run_native_compiled_program(
    const AivmProgram* program,
    const char* vm_error_message,
    const char* const* process_argv,
    size_t process_argv_count)
{
    AivmSyscallBinding bindings[4];
    AivmCResult result;

    if (program == NULL) {
        return 2;
    }

    bindings[0].target = "sys.stdout_writeLine";
    bindings[0].handler = native_syscall_stdout_write_line;
    bindings[1].target = "io.print";
    bindings[1].handler = native_syscall_stdout_write_line;
    bindings[2].target = "io.write";
    bindings[2].handler = native_syscall_stdout_write_line;
    bindings[3].target = "sys.process_argv";
    bindings[3].handler = native_syscall_process_argv;
    result = aivm_c_execute_program_with_syscalls_and_argv(
        program,
        bindings,
        4U,
        process_argv,
        process_argv_count);

    if (!result.loaded || result.load_status != AIVM_PROGRAM_OK) {
        fprintf(stderr, "Err#err1(code=RUN001 message=\"Failed to load native program.\" nodeId=program)\n");
        return 2;
    }
    if (!result.ok || result.status == AIVM_VM_STATUS_ERROR) {
        fprintf(stderr, "Err#err1(code=RUN001 message=\"%s\" nodeId=vm)\n", vm_error_message);
        return 3;
    }
    if (result.has_exit_code) {
        printf("Ok#ok1(type=int value=%d)\n", result.exit_code);
        return result.exit_code;
    }
    return 0;
}

static int run_native_aibc1(const char* path, const char* const* process_argv, size_t process_argv_count)
{
    unsigned char* bytes = NULL;
    size_t byte_count = 0U;
    AivmProgram program;
    AivmProgramLoadResult load_result;

    if (!read_binary_file(path, &bytes, &byte_count)) {
        fprintf(stderr, "Err#err1(code=RUN001 message=\"Failed to read AiBC1 file.\" nodeId=program)\n");
        return 2;
    }

    load_result = aivm_program_load_aibc1(bytes, byte_count, &program);
    free(bytes);

    if (load_result.status != AIVM_PROGRAM_OK) {
        fprintf(stderr, "Err#err1(code=RUN001 message=\"Failed to load AiBC1 program.\" nodeId=program)\n");
        return 2;
    }
    return run_native_compiled_program(&program, "AiBC1 execution failed.", process_argv, process_argv_count);
}

static int parse_attr_span(const char* attrs, const char* key, char* out, size_t out_len)
{
    char needle[64];
    const char* pos;
    const char* vstart;
    const char* vend;
    size_t n;

    if (attrs == NULL || key == NULL || out == NULL || out_len == 0U) {
        return 0;
    }
    if (snprintf(needle, sizeof(needle), "%s=", key) >= (int)sizeof(needle)) {
        return 0;
    }
    pos = strstr(attrs, needle);
    if (pos == NULL) {
        return 0;
    }
    vstart = pos + strlen(needle);
    if (*vstart == '"') {
        vstart += 1;
        vend = strchr(vstart, '"');
    } else {
        vend = vstart;
        while (*vend != '\0' && *vend != ' ' && *vend != '\t' && *vend != '\r' && *vend != '\n') {
            vend += 1;
        }
    }
    if (vend == NULL || vend < vstart) {
        return 0;
    }
    n = (size_t)(vend - vstart);
    if (n + 1U > out_len) {
        return 0;
    }
    memcpy(out, vstart, n);
    out[n] = '\0';
    return 1;
}

static int has_attr_key(const char* attrs, const char* key)
{
    char tmp[8];
    return parse_attr_span(attrs, key, tmp, sizeof(tmp));
}

static int parse_attr_int64(const char* attrs, const char* key, int64_t* out_value)
{
    char text[64];
    char* end = NULL;
    long long v;
    if (out_value == NULL || !parse_attr_span(attrs, key, text, sizeof(text))) {
        return 0;
    }
    v = strtoll(text, &end, 10);
    if (end == text || *end != '\0') {
        return 0;
    }
    *out_value = (int64_t)v;
    return 1;
}

static int parse_attr_bool(const char* attrs, const char* key, int* out_value)
{
    char text[16];
    if (out_value == NULL || !parse_attr_span(attrs, key, text, sizeof(text))) {
        return 0;
    }
    if (strcmp(text, "true") == 0) {
        *out_value = 1;
        return 1;
    }
    if (strcmp(text, "false") == 0) {
        *out_value = 0;
        return 1;
    }
    return 0;
}

static int unescape_string(const char* input, char* out, size_t out_len)
{
    size_t i = 0U;
    size_t w = 0U;
    if (input == NULL || out == NULL || out_len == 0U) {
        return 0;
    }
    while (input[i] != '\0') {
        char c = input[i];
        if (c == '\\') {
            char e = input[i + 1U];
            if (e == '\0') {
                return 0;
            }
            if (w + 1U >= out_len) {
                return 0;
            }
            if (e == 'n') {
                out[w++] = '\n';
            } else if (e == 'r') {
                out[w++] = '\r';
            } else if (e == 't') {
                out[w++] = '\t';
            } else if (e == '"' || e == '\\') {
                out[w++] = e;
            } else {
                return 0;
            }
            i += 2U;
            continue;
        }
        if (w + 1U >= out_len) {
            return 0;
        }
        out[w++] = c;
        i += 1U;
    }
    out[w] = '\0';
    return 1;
}

static int opcode_from_text(const char* op_text, AivmOpcode* out_opcode)
{
    if (op_text == NULL || out_opcode == NULL) {
        return 0;
    }
#define MAP_OP(name) if (strcmp(op_text, #name) == 0) { *out_opcode = AIVM_OP_##name; return 1; }
    MAP_OP(NOP)
    MAP_OP(HALT)
    MAP_OP(STUB)
    MAP_OP(PUSH_INT)
    MAP_OP(POP)
    MAP_OP(STORE_LOCAL)
    MAP_OP(LOAD_LOCAL)
    MAP_OP(ADD_INT)
    MAP_OP(JUMP)
    MAP_OP(JUMP_IF_FALSE)
    MAP_OP(PUSH_BOOL)
    MAP_OP(CALL)
    MAP_OP(RET)
    MAP_OP(EQ_INT)
    MAP_OP(EQ)
    MAP_OP(CONST)
    MAP_OP(STR_CONCAT)
    MAP_OP(TO_STRING)
    MAP_OP(STR_ESCAPE)
    MAP_OP(RETURN)
    MAP_OP(STR_SUBSTRING)
    MAP_OP(STR_REMOVE)
    MAP_OP(CALL_SYS)
    MAP_OP(ASYNC_CALL)
    MAP_OP(ASYNC_CALL_SYS)
    MAP_OP(AWAIT)
    MAP_OP(PAR_BEGIN)
    MAP_OP(PAR_FORK)
    MAP_OP(PAR_JOIN)
    MAP_OP(PAR_CANCEL)
    MAP_OP(STR_UTF8_BYTE_COUNT)
    MAP_OP(NODE_KIND)
    MAP_OP(NODE_ID)
    MAP_OP(ATTR_COUNT)
    MAP_OP(ATTR_KEY)
    MAP_OP(ATTR_VALUE_KIND)
    MAP_OP(ATTR_VALUE_STRING)
    MAP_OP(ATTR_VALUE_INT)
    MAP_OP(ATTR_VALUE_BOOL)
    MAP_OP(CHILD_COUNT)
    MAP_OP(CHILD_AT)
    MAP_OP(MAKE_BLOCK)
    MAP_OP(APPEND_CHILD)
    MAP_OP(MAKE_ERR)
    MAP_OP(MAKE_LIT_STRING)
    MAP_OP(MAKE_LIT_INT)
    MAP_OP(MAKE_NODE)
#undef MAP_OP
    return 0;
}

static int parse_bytecode_aos_to_program_text(const char* source, AivmProgram* out_program)
{
    const char* p;

    if (source == NULL || out_program == NULL) {
        return 0;
    }
    if (strstr(source, "Bytecode#") == NULL) {
        return 0;
    }

    aivm_program_clear(out_program);
    out_program->instructions = out_program->instruction_storage;
    out_program->constants = out_program->constant_storage;
    out_program->format_version = 1U;
    out_program->format_flags = 0U;

    p = source;
    while ((p = strstr(p, "Const#")) != NULL) {
        const char* lparen = strchr(p, '(');
        const char* rparen;
        char attrs[512];
        char kind[32];
        size_t n;
        if (out_program->constant_count >= AIVM_PROGRAM_MAX_CONSTANTS || lparen == NULL) {
            return 0;
        }
        rparen = strchr(lparen, ')');
        if (rparen == NULL || rparen <= lparen) {
            return 0;
        }
        n = (size_t)(rparen - (lparen + 1));
        if (n + 1U > sizeof(attrs)) {
            return 0;
        }
        memcpy(attrs, lparen + 1, n);
        attrs[n] = '\0';
        if (!parse_attr_span(attrs, "kind", kind, sizeof(kind))) {
            return 0;
        }
        if (strcmp(kind, "int") == 0) {
            int64_t v = 0;
            if (!parse_attr_int64(attrs, "value", &v)) {
                return 0;
            }
            out_program->constant_storage[out_program->constant_count] = aivm_value_int(v);
        } else if (strcmp(kind, "bool") == 0) {
            int b = 0;
            if (!parse_attr_bool(attrs, "value", &b)) {
                return 0;
            }
            out_program->constant_storage[out_program->constant_count] = aivm_value_bool(b);
        } else if (strcmp(kind, "string") == 0) {
            char raw[1024];
            char unescaped[1024];
            size_t len;
            size_t base;
            if (!parse_attr_span(attrs, "value", raw, sizeof(raw))) {
                return 0;
            }
            if (!unescape_string(raw, unescaped, sizeof(unescaped))) {
                return 0;
            }
            len = strlen(unescaped);
            base = out_program->string_storage_used;
            if (base + len + 1U > AIVM_PROGRAM_MAX_STRING_BYTES) {
                return 0;
            }
            memcpy(&out_program->string_storage[base], unescaped, len + 1U);
            out_program->string_storage_used += (len + 1U);
            out_program->constant_storage[out_program->constant_count] = aivm_value_string(&out_program->string_storage[base]);
        } else if (strcmp(kind, "void") == 0) {
            out_program->constant_storage[out_program->constant_count] = aivm_value_void();
        } else {
            return 0;
        }
        out_program->constant_count += 1U;
        p = rparen + 1;
    }

    p = source;
    while ((p = strstr(p, "Inst#")) != NULL) {
        const char* lparen = strchr(p, '(');
        const char* rparen;
        char attrs[512];
        char op[64];
        int64_t a = 0;
        AivmOpcode opcode;
        size_t n;

        if (out_program->instruction_count >= AIVM_PROGRAM_MAX_INSTRUCTIONS || lparen == NULL) {
            return 0;
        }
        rparen = strchr(lparen, ')');
        if (rparen == NULL || rparen <= lparen) {
            return 0;
        }
        n = (size_t)(rparen - (lparen + 1));
        if (n + 1U > sizeof(attrs)) {
            return 0;
        }
        memcpy(attrs, lparen + 1, n);
        attrs[n] = '\0';

        if (!parse_attr_span(attrs, "op", op, sizeof(op)) || !opcode_from_text(op, &opcode)) {
            return 0;
        }
        if (has_attr_key(attrs, "b") || has_attr_key(attrs, "s")) {
            return 0;
        }
        (void)parse_attr_int64(attrs, "a", &a);

        out_program->instruction_storage[out_program->instruction_count].opcode = opcode;
        out_program->instruction_storage[out_program->instruction_count].operand_int = a;
        out_program->instruction_count += 1U;
        p = rparen + 1;
    }

    return out_program->instruction_count > 0U;
}

static int parse_bytecode_aos_to_program_file(const char* aos_path, AivmProgram* out_program)
{
    char source[131072];
    if (aos_path == NULL || out_program == NULL) {
        return 0;
    }
    if (!read_text_file(aos_path, source, sizeof(source))) {
        return 0;
    }
    return parse_bytecode_aos_to_program_text(source, out_program);
}

static int source_file_looks_like_bytecode_aos(const char* aos_path)
{
    char source[4096];
    if (aos_path == NULL) {
        return 0;
    }
    if (!read_text_file(aos_path, source, sizeof(source))) {
        return 0;
    }
    return strstr(source, "Bytecode#") != NULL;
}

typedef struct {
    char kind[32];
    char attrs[512];
    const char* body_start;
    const char* body_end;
    const char* next;
} SimpleNodeView;

static int simple_find_matching_brace(const char* open_brace, const char* end, const char** out_close)
{
    int depth = 0;
    int in_string = 0;
    const char* q;
    if (open_brace == NULL || end == NULL || out_close == NULL || *open_brace != '{') {
        return 0;
    }
    q = open_brace;
    while (q < end && *q != '\0') {
        char c = *q;
        if (in_string) {
            if (c == '\\' && (q + 1) < end) {
                q += 2;
                continue;
            }
            if (c == '"') {
                in_string = 0;
            }
            q += 1;
            continue;
        }
        if (c == '"') {
            in_string = 1;
            q += 1;
            continue;
        }
        if (c == '{') {
            depth += 1;
        } else if (c == '}') {
            depth -= 1;
            if (depth == 0) {
                *out_close = q;
                return 1;
            }
        }
        q += 1;
    }
    return 0;
}

static int simple_parse_next_node(const char* cursor, const char* end, SimpleNodeView* out_node)
{
    const char* kstart;
    const char* khash;
    const char* kid_end;
    const char* lparen;
    const char* rparen;
    const char* open_brace;
    const char* close_brace;
    size_t kind_len;
    size_t attrs_len;

    if (cursor == NULL || end == NULL || out_node == NULL) {
        return 0;
    }
    while (cursor < end && *cursor != '\0' && isspace((unsigned char)*cursor)) {
        cursor += 1;
    }
    if (cursor >= end || *cursor == '\0') {
        return 0;
    }
    kstart = cursor;
    khash = strchr(kstart, '#');
    if (khash == NULL || khash >= end) {
        return 0;
    }
    kind_len = (size_t)(khash - kstart);
    if (kind_len == 0U || kind_len + 1U > sizeof(out_node->kind)) {
        return 0;
    }
    memcpy(out_node->kind, kstart, kind_len);
    out_node->kind[kind_len] = '\0';

    kid_end = khash + 1;
    while (kid_end < end && *kid_end != '\0' &&
           !isspace((unsigned char)*kid_end) &&
           *kid_end != '(' && *kid_end != '{') {
        kid_end += 1;
    }

    lparen = NULL;
    if (kid_end < end && *kid_end == '(') {
        lparen = kid_end;
    }
    if (lparen != NULL) {
        rparen = strchr(lparen + 1, ')');
        if (rparen == NULL || rparen >= end) {
            return 0;
        }
        attrs_len = (size_t)(rparen - (lparen + 1));
        if (attrs_len + 1U > sizeof(out_node->attrs)) {
            return 0;
        }
        memcpy(out_node->attrs, lparen + 1, attrs_len);
        out_node->attrs[attrs_len] = '\0';
        open_brace = rparen + 1;
    } else {
        out_node->attrs[0] = '\0';
        open_brace = kid_end;
    }
    while (open_brace < end && *open_brace != '\0' && isspace((unsigned char)*open_brace)) {
        open_brace += 1;
    }
    if (open_brace < end && *open_brace == '{') {
        if (!simple_find_matching_brace(open_brace, end, &close_brace)) {
            return 0;
        }
        out_node->body_start = open_brace + 1;
        out_node->body_end = close_brace;
        out_node->next = close_brace + 1;
    } else {
        out_node->body_start = open_brace;
        out_node->body_end = open_brace;
        out_node->next = open_brace;
    }
    return 1;
}

static int simple_fail(const char* message)
{
    (void)message;
    return 0;
}

static int simple_emit_instruction(AivmProgram* program, AivmOpcode opcode, int64_t operand_int)
{
    if (program == NULL || program->instruction_count >= AIVM_PROGRAM_MAX_INSTRUCTIONS) {
        return 0;
    }
    program->instruction_storage[program->instruction_count].opcode = opcode;
    program->instruction_storage[program->instruction_count].operand_int = operand_int;
    program->instruction_count += 1U;
    return 1;
}

static int simple_lookup_local(char names[][64], size_t* local_count, const char* name, size_t* out_idx, int create_if_missing)
{
    size_t i;
    if (names == NULL || local_count == NULL || name == NULL || out_idx == NULL) {
        return 0;
    }
    for (i = 0U; i < *local_count; i += 1U) {
        if (strcmp(names[i], name) == 0) {
            *out_idx = i;
            return 1;
        }
    }
    if (!create_if_missing || *local_count >= 256U) {
        return 0;
    }
    if (snprintf(names[*local_count], 64U, "%s", name) >= 64) {
        return 0;
    }
    *out_idx = *local_count;
    *local_count += 1U;
    return 1;
}

static int simple_add_string_const(AivmProgram* program, const char* value, size_t* out_idx)
{
    size_t i;
    size_t len;
    size_t base;
    if (program == NULL || value == NULL || out_idx == NULL) {
        return 0;
    }
    for (i = 0U; i < program->constant_count; i += 1U) {
        if (program->constant_storage[i].type == AIVM_VAL_STRING &&
            program->constant_storage[i].string_value != NULL &&
            strcmp(program->constant_storage[i].string_value, value) == 0) {
            *out_idx = i;
            return 1;
        }
    }
    if (program->constant_count >= AIVM_PROGRAM_MAX_CONSTANTS) {
        return 0;
    }
    len = strlen(value);
    base = program->string_storage_used;
    if (base + len + 1U > AIVM_PROGRAM_MAX_STRING_BYTES) {
        return 0;
    }
    memcpy(&program->string_storage[base], value, len + 1U);
    program->string_storage_used += (len + 1U);
    program->constant_storage[program->constant_count] = aivm_value_string(&program->string_storage[base]);
    *out_idx = program->constant_count;
    program->constant_count += 1U;
    return 1;
}

static int simple_compile_expr_node(const SimpleNodeView* node, AivmProgram* program, char locals[][64], size_t* local_count)
{
    if (node == NULL || program == NULL || locals == NULL || local_count == NULL) {
        return 0;
    }
    if (strcmp(node->kind, "Lit") == 0) {
        char value[256];
        if (!parse_attr_span(node->attrs, "value", value, sizeof(value))) {
            return simple_emit_instruction(program, AIVM_OP_HALT, 0);
        }
        if (strcmp(value, "true") == 0 || strcmp(value, "false") == 0) {
            return simple_emit_instruction(program, AIVM_OP_PUSH_BOOL, (strcmp(value, "true") == 0) ? 1 : 0);
        }
        {
            char* end = NULL;
            long long parsed = strtoll(value, &end, 10);
            if (end != value && *end == '\0') {
                return simple_emit_instruction(program, AIVM_OP_PUSH_INT, (int64_t)parsed);
            }
        }
        {
            char unescaped[256];
            size_t idx = 0U;
            if (!unescape_string(value, unescaped, sizeof(unescaped))) {
                return 0;
            }
            if (!simple_add_string_const(program, unescaped, &idx)) {
                return 0;
            }
            return simple_emit_instruction(program, AIVM_OP_CONST, (int64_t)idx);
        }
    }
    if (strcmp(node->kind, "Var") == 0) {
        char name[64];
        size_t idx = 0U;
        if (!parse_attr_span(node->attrs, "name", name, sizeof(name))) {
            return 0;
        }
        if (!simple_lookup_local(locals, local_count, name, &idx, 0)) {
            return 0;
        }
        return simple_emit_instruction(program, AIVM_OP_LOAD_LOCAL, (int64_t)idx);
    }
    if (strcmp(node->kind, "StrConcat") == 0) {
        const char* c = node->body_start;
        int first = 1;
        SimpleNodeView child;
        while (simple_parse_next_node(c, node->body_end, &child)) {
            if (!simple_compile_expr_node(&child, program, locals, local_count)) {
                return 0;
            }
            if (!first && !simple_emit_instruction(program, AIVM_OP_STR_CONCAT, 0)) {
                return 0;
            }
            first = 0;
            c = child.next;
        }
        return !first;
    }
    return simple_fail("unsupported expr kind");
}

static int parse_simple_program_aos_to_program_text(const char* source, AivmProgram* out_program)
{
    const char* program_pos;
    const char* first_open;
    const char* program_close;
    const char* p;
    char locals[256][64];
    size_t local_count = 0U;
    int saw_return = 0;

    if (source == NULL || out_program == NULL) {
        return simple_fail("missing source/program");
    }
    program_pos = strstr(source, "Program#");
    if (program_pos == NULL || strstr(source, "Bytecode#") != NULL) {
        return simple_fail("missing Program# or bytecode input");
    }

    first_open = strchr(program_pos, '{');
    if (first_open == NULL || !simple_find_matching_brace(first_open, source + strlen(source), &program_close)) {
        return simple_fail("program braces not found");
    }

    aivm_program_clear(out_program);
    out_program->instructions = out_program->instruction_storage;
    out_program->constants = out_program->constant_storage;
    out_program->format_version = 1U;
    out_program->format_flags = 0U;

    p = first_open + 1;
    while (p < program_close) {
        SimpleNodeView node;
        if (!simple_parse_next_node(p, program_close, &node)) {
            break;
        }
        if (strcmp(node.kind, "Export") == 0) {
            p = node.next;
            continue;
        }
        if (strcmp(node.kind, "Let") == 0) {
            char name[64];
            size_t local_idx = 0U;
            SimpleNodeView expr;
            if (!parse_attr_span(node.attrs, "name", name, sizeof(name))) {
                return simple_fail("let missing name");
            }
            if (!simple_lookup_local(locals, &local_count, name, &local_idx, 1)) {
                return simple_fail("let local lookup failed");
            }
            if (!simple_parse_next_node(node.body_start, node.body_end, &expr)) {
                return simple_fail("let missing expression");
            }
            if (!simple_compile_expr_node(&expr, out_program, locals, &local_count)) {
                return simple_fail("let expression compile failed");
            }
            if (!simple_emit_instruction(out_program, AIVM_OP_STORE_LOCAL, (int64_t)local_idx)) {
                return simple_fail("let STORE_LOCAL emit failed");
            }
            p = node.next;
            continue;
        }
        if (strcmp(node.kind, "Call") == 0) {
            char target[128];
            const char* mapped = NULL;
            size_t target_idx = 0U;
            SimpleNodeView arg;
            if (!parse_attr_span(node.attrs, "target", target, sizeof(target))) {
                return simple_fail("call missing target");
            }
            if (strcmp(target, "io.print") == 0 || strcmp(target, "io.write") == 0 || strcmp(target, "sys.stdout_writeLine") == 0) {
                mapped = "sys.stdout_writeLine";
            } else {
                return simple_fail("call unsupported target");
            }
            if (!simple_add_string_const(out_program, mapped, &target_idx)) {
                return simple_fail("call target const add failed");
            }
            if (!simple_emit_instruction(out_program, AIVM_OP_CONST, (int64_t)target_idx)) {
                return simple_fail("call target const emit failed");
            }
            if (!simple_parse_next_node(node.body_start, node.body_end, &arg)) {
                return simple_fail("call missing argument");
            }
            if (!simple_compile_expr_node(&arg, out_program, locals, &local_count)) {
                return simple_fail("call argument compile failed");
            }
            if (!simple_emit_instruction(out_program, AIVM_OP_CALL_SYS, 1) ||
                !simple_emit_instruction(out_program, AIVM_OP_POP, 0)) {
                return simple_fail("call emit failed");
            }
            p = node.next;
            continue;
        }
        if (strcmp(node.kind, "Return") == 0) {
            SimpleNodeView expr;
            if (!simple_parse_next_node(node.body_start, node.body_end, &expr)) {
                return simple_fail("return missing expression");
            }
            if (!simple_compile_expr_node(&expr, out_program, locals, &local_count) ||
                !simple_emit_instruction(out_program, AIVM_OP_RETURN, 0)) {
                return simple_fail("return emit failed");
            }
            saw_return = 1;
            p = node.next;
            continue;
        }
        return simple_fail("unsupported top-level node");
    }

    if (!saw_return && !simple_emit_instruction(out_program, AIVM_OP_HALT, 0)) {
        return simple_fail("halt emit failed");
    }
    return out_program->instruction_count > 0U;
}

static int parse_simple_program_aos_to_program_file(const char* aos_path, AivmProgram* out_program)
{
    char source[131072];
    if (aos_path == NULL || out_program == NULL) {
        return 0;
    }
    if (!read_text_file(aos_path, source, sizeof(source))) {
        return 0;
    }
    return parse_simple_program_aos_to_program_text(source, out_program);
}

static int run_native_simple_program_aos(const char* aos_path, const char* const* process_argv, size_t process_argv_count)
{
    AivmProgram program;

    if (!parse_simple_program_aos_to_program_file(aos_path, &program)) {
        return -1;
    }
    return run_native_compiled_program(
        &program,
        "Native simple source execution failed.",
        process_argv,
        process_argv_count);
}

static int run_native_bytecode_aos(const char* aos_path, const char* const* process_argv, size_t process_argv_count)
{
    AivmProgram program;

    if (!parse_bytecode_aos_to_program_file(aos_path, &program)) {
        return -1;
    }
    return run_native_compiled_program(
        &program,
        "Native bytecode program execution failed.",
        process_argv,
        process_argv_count);
}

static int run_native_bundle(const char* bundle_path, const char* const* process_argv, size_t process_argv_count)
{
    return run_native_bytecode_aos(bundle_path, process_argv, process_argv_count);
}

static void write_u32_le(FILE* f, uint32_t value)
{
    uint8_t bytes[4];
    bytes[0] = (uint8_t)(value & 0xffU);
    bytes[1] = (uint8_t)((value >> 8U) & 0xffU);
    bytes[2] = (uint8_t)((value >> 16U) & 0xffU);
    bytes[3] = (uint8_t)((value >> 24U) & 0xffU);
    (void)fwrite(bytes, 1U, 4U, f);
}

static void write_i64_le(FILE* f, int64_t value)
{
    uint64_t u = (uint64_t)value;
    uint8_t bytes[8];
    bytes[0] = (uint8_t)(u & 0xffU);
    bytes[1] = (uint8_t)((u >> 8U) & 0xffU);
    bytes[2] = (uint8_t)((u >> 16U) & 0xffU);
    bytes[3] = (uint8_t)((u >> 24U) & 0xffU);
    bytes[4] = (uint8_t)((u >> 32U) & 0xffU);
    bytes[5] = (uint8_t)((u >> 40U) & 0xffU);
    bytes[6] = (uint8_t)((u >> 48U) & 0xffU);
    bytes[7] = (uint8_t)((u >> 56U) & 0xffU);
    (void)fwrite(bytes, 1U, 8U, f);
}

static int write_program_as_aibc1(const AivmProgram* program, const char* out_path)
{
    FILE* f;
    uint32_t section_count = 1U;
    uint32_t inst_payload_size;
    uint32_t const_payload_size = 4U;
    size_t i;

    if (program == NULL || out_path == NULL || program->instruction_count == 0U) {
        return 0;
    }

    for (i = 0U; i < program->constant_count; i += 1U) {
        AivmValue v = program->constant_storage[i];
        if (v.type == AIVM_VAL_INT) {
            const_payload_size += 1U + 8U;
        } else if (v.type == AIVM_VAL_BOOL) {
            const_payload_size += 1U + 1U;
        } else if (v.type == AIVM_VAL_STRING) {
            size_t len = (v.string_value == NULL) ? 0U : strlen(v.string_value);
            if (len > 0xffffffffU) {
                return 0;
            }
            const_payload_size += 1U + 4U + (uint32_t)len;
        } else {
            const_payload_size += 1U;
        }
    }
    if (program->constant_count > 0U) {
        section_count = 2U;
    }

    if (program->instruction_count > (size_t)((0xffffffffU - 4U) / 12U)) {
        return 0;
    }
    inst_payload_size = 4U + (uint32_t)(program->instruction_count * 12U);

    f = fopen(out_path, "wb");
    if (f == NULL) {
        return 0;
    }

    (void)fwrite("AIBC", 1U, 4U, f);
    write_u32_le(f, 1U);
    write_u32_le(f, 0U);
    write_u32_le(f, section_count);

    write_u32_le(f, AIVM_PROGRAM_SECTION_INSTRUCTIONS);
    write_u32_le(f, inst_payload_size);
    write_u32_le(f, (uint32_t)program->instruction_count);
    for (i = 0U; i < program->instruction_count; i += 1U) {
        write_u32_le(f, (uint32_t)program->instruction_storage[i].opcode);
        write_i64_le(f, program->instruction_storage[i].operand_int);
    }

    if (section_count == 2U) {
        write_u32_le(f, AIVM_PROGRAM_SECTION_CONSTANTS);
        write_u32_le(f, const_payload_size);
        write_u32_le(f, (uint32_t)program->constant_count);
        for (i = 0U; i < program->constant_count; i += 1U) {
            AivmValue v = program->constant_storage[i];
            if (v.type == AIVM_VAL_INT) {
                (void)fputc(1, f);
                write_i64_le(f, v.int_value);
            } else if (v.type == AIVM_VAL_BOOL) {
                (void)fputc(2, f);
                (void)fputc(v.bool_value ? 1 : 0, f);
            } else if (v.type == AIVM_VAL_STRING) {
                uint32_t len = (uint32_t)((v.string_value == NULL) ? 0U : strlen(v.string_value));
                (void)fputc(3, f);
                write_u32_le(f, len);
                if (len > 0U) {
                    (void)fwrite(v.string_value, 1U, len, f);
                }
            } else {
                (void)fputc(4, f);
            }
        }
    }

    if (fclose(f) != 0) {
        return 0;
    }
    return 1;
}

typedef struct {
    const char* program_path;
    int app_arg_start;
    int app_arg_count;
} RunTarget;

static int parse_run_target(int argc, char** argv, int start_index, RunTarget* out_target)
{
    int i;
    const char* program_path = NULL;
    int app_arg_start = -1;

    if (out_target == NULL) {
        return 2;
    }
    for (i = start_index; i < argc; i++) {
        const char* arg = argv[i];
        if (strcmp(arg, "--") == 0) {
            if (app_arg_start < 0) {
                app_arg_start = i + 1;
            }
            break;
        }
        if (app_arg_start < 0 && starts_with(arg, "--vm=")) {
            const char* mode = arg + 5;
            if (strcmp(mode, "c") != 0 && !is_reserved_cv_selector(mode)) {
                return print_unsupported_vm_mode(mode);
            }
            continue;
        }
        if (program_path == NULL && arg[0] != '-' && app_arg_start < 0) {
            program_path = arg;
            continue;
        }
        if (program_path == NULL && arg[0] == '-' && app_arg_start < 0) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Unsupported flag for native C runtime.\" nodeId=argv)\n");
            return 2;
        }
        if (app_arg_start < 0) {
            app_arg_start = i;
        }
        break;
    }

    /* Preserve wrapper contract: default to current project directory when omitted. */
    out_target->program_path = (program_path != NULL) ? program_path : ".";
    if (app_arg_start >= 0 && app_arg_start <= argc) {
        out_target->app_arg_start = app_arg_start;
        out_target->app_arg_count = argc - app_arg_start;
    } else {
        out_target->app_arg_start = argc;
        out_target->app_arg_count = 0;
    }
    return 0;
}

static int run_via_resolved_input(const char* input, const char* const* process_argv, size_t process_argv_count)
{
    char resolved[PATH_MAX];
    char source_aos[PATH_MAX];
    if (input != NULL && ends_with(input, ".aibundle") && file_exists(input)) {
        int rc = run_native_bundle(input, process_argv, process_argv_count);
        if (rc >= 0) {
            return rc;
        }
        fprintf(stderr,
            "Err#err1(code=DEV008 message=\"Native bundle input uses unsupported bytecode fields. Provide supported Bytecode# shape.\" nodeId=program)\n");
        return 2;
    }
    if (resolve_input_to_aibc1(input, resolved, sizeof(resolved))) {
        return run_native_aibc1(resolved, process_argv, process_argv_count);
    }
    if (resolve_input_to_aos(input, source_aos, sizeof(source_aos))) {
        int rc = run_native_bytecode_aos(source_aos, process_argv, process_argv_count);
        if (rc >= 0) {
            return rc;
        }
        if (source_file_looks_like_bytecode_aos(source_aos)) {
            fprintf(stderr,
                "Err#err1(code=DEV008 message=\"Native bytecode AOS input uses unsupported fields. Build precompiled .aibc1 or use supported instruction attributes.\" nodeId=program)\n");
            return 2;
        }
        rc = run_native_simple_program_aos(source_aos, process_argv, process_argv_count);
        if (rc >= 0) {
            return rc;
        }
    }
    fprintf(stderr,
        "Err#err1(code=DEV008 message=\"Native C runtime cannot compile this source/project shape yet. Provide .aibc1 or supported Program#/Bytecode# AOS.\" nodeId=program)\n");
    return 2;
}

static int handle_run(int argc, char** argv)
{
    RunTarget target;
    int parse_rc = parse_run_target(argc, argv, 2, &target);
    if (parse_rc != 0) {
        return parse_rc;
    }
    return run_via_resolved_input(
        target.program_path,
        (const char* const*)&argv[target.app_arg_start],
        (size_t)target.app_arg_count);
}

static int handle_serve(int argc, char** argv)
{
    (void)argc;
    (void)argv;
    fprintf(stderr,
        "Err#err1(code=DEV008 message=\"serve is not part of native airun runtime surface.\" nodeId=command)\n");
    return 2;
}

static int handle_debug(int argc, char** argv)
{
    RunTarget target;
    int parse_rc;
    if (argc >= 3 && strcmp(argv[2], "run") == 0) {
        parse_rc = parse_run_target(argc, argv, 3, &target);
        if (parse_rc != 0) {
            return parse_rc;
        }
        return run_via_resolved_input(
            target.program_path,
            (const char* const*)&argv[target.app_arg_start],
            (size_t)target.app_arg_count);
    }
    fprintf(stderr,
        "Err#err1(code=DEV008 message=\"Native debug supports only: debug run <program>\" nodeId=command)\n");
    return 2;
}

static int handle_repl(void)
{
    char line[1024];

    while (fgets(line, sizeof(line), stdin) != NULL) {
        size_t n = strlen(line);
        while (n > 0U && (line[n - 1U] == '\n' || line[n - 1U] == '\r')) {
            line[--n] = '\0';
        }
        if (n == 0U) {
            continue;
        }
        if (strcmp(line, "quit") == 0 || strcmp(line, "exit") == 0) {
            return 0;
        }
        if (strcmp(line, "version") == 0) {
            printf("airun-native-c abi=%u\n", aivm_c_abi_version());
            continue;
        }
        if (starts_with(line, "run ")) {
            return run_via_resolved_input(line + 4, NULL, 0U);
        }

        fprintf(stderr,
            "Err#err1(code=DEV008 message=\"Unsupported repl command.\" nodeId=repl)\n");
        return 2;
    }

    return 0;
}

static int parse_int(const char* text, int* out)
{
    char* end = NULL;
    long v;

    if (text == NULL || out == NULL) {
        return 0;
    }

    v = strtol(text, &end, 10);
    if (end == text || *end != '\0' || v <= 0L || v > 100000000L) {
        return 0;
    }
    *out = (int)v;
    return 1;
}

static int bench_syscall_sink(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    (void)args;
    (void)arg_count;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int bench_execute_program_iterations(const AivmProgram* program, int iterations, uint64_t* out_ticks)
{
    AivmSyscallBinding bindings[3];
    int i;
    uint64_t ticks = 0U;

    if (program == NULL || out_ticks == NULL || iterations <= 0) {
        return 0;
    }

    bindings[0].target = "sys.stdout_writeLine";
    bindings[0].handler = bench_syscall_sink;
    bindings[1].target = "io.print";
    bindings[1].handler = bench_syscall_sink;
    bindings[2].target = "io.write";
    bindings[2].handler = bench_syscall_sink;

    for (i = 0; i < iterations; i += 1) {
        AivmCResult result = aivm_c_execute_program_with_syscalls(program, bindings, 3U);
        if (!result.loaded || result.load_status != AIVM_PROGRAM_OK || !result.ok || result.status == AIVM_VM_STATUS_ERROR) {
            return 0;
        }
        ticks += (uint64_t)program->instruction_count;
    }

    *out_ticks = ticks;
    return 1;
}

static int handle_bench(int argc, char** argv)
{
    int iterations = 10000;
    int human = 0;
    int i;
    int failures = 0;
    AivmInstruction loop_instructions[2];
    uint64_t runtime_loop_ticks = 0U;
    uint64_t compiler_program_ticks = 0U;
    uint64_t compiler_bytecode_ticks = 0U;
    uint64_t app_hello_ticks = 0U;
    uint64_t app_echo_ticks = 0U;
    uint64_t app_bundle_ticks = 0U;
    const char* compiler_program_status = "ok";
    const char* compiler_bytecode_status = "ok";
    const char* runtime_loop_status = "ok";
    const char* app_hello_status = "ok";
    const char* app_echo_status = "ok";
    const char* app_bundle_status = "ok";
    static const char* program_bench_source =
        "Program#p1 {\n"
        "  Let#l1(name=message) { Lit#s1(value=\"Hello from VM\") }\n"
        "  Call#c1(target=io.print) { Var#v1(name=message) }\n"
        "}";
    static const char* bytecode_bench_source =
        "Bytecode#bc1(magic=\"AIBC\" format=\"AiBC1\" version=1 flags=0) {\n"
        "  Const#k0(kind=string value=\"hello\")\n"
        "  Func#f1(name=main params=\"argv\" locals=\"\") {\n"
        "    Inst#i1(op=HALT)\n"
        "  }\n"
        "}";
    static const char* app_hello_source =
        "Program#p1 {\n"
        "  Let#l1(name=message) { Lit#s1(value=\"Hello from VM\") }\n"
        "  Call#c1(target=io.print) { Var#v1(name=message) }\n"
        "}";
    static const char* app_echo_source =
        "Program#p1 {\n"
        "  Let#l1(name=line) { Lit#s0(value=\"vm-echo\") }\n"
        "  Let#l2(name=out) {\n"
        "    StrConcat#s1 {\n"
        "      Lit#s2(value=\"echo:\")\n"
        "      Var#v1(name=line)\n"
        "    }\n"
        "  }\n"
        "  Call#c2(target=io.print) { Var#v2(name=out) }\n"
        "}";
    static const char* app_bundle_source =
        "Bytecode#bc1(flags=0 format=\"AiBC1\" magic=\"AIBC\" version=1) {\n"
        "  Const#k0(kind=int value=0)\n"
        "  Func#f_main(locals=\"argv,start\" name=main params=\"argv\") {\n"
        "    Inst#i0(a=0 op=CONST)\n"
        "    Inst#i1(a=1 op=STORE_LOCAL)\n"
        "    Inst#i2(op=RETURN)\n"
        "  }\n"
        "}";

    for (i = 2; i < argc; i++) {
        if (strcmp(argv[i], "--human") == 0) {
            human = 1;
            continue;
        }
        if (strcmp(argv[i], "--iterations") == 0) {
            if (i + 1 >= argc || !parse_int(argv[i + 1], &iterations)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Invalid --iterations value.\" nodeId=argv)\n");
                return 2;
            }
            i += 1;
            continue;
        }
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Unsupported bench flag.\" nodeId=argv)\n");
        return 2;
    }

    loop_instructions[0].opcode = AIVM_OP_PUSH_INT;
    loop_instructions[0].operand_int = 1;
    loop_instructions[1].opcode = AIVM_OP_HALT;
    loop_instructions[1].operand_int = 0;

    for (i = 0; i < iterations; i += 1) {
        AivmCResult result = aivm_c_execute_instructions(loop_instructions, 2U);
        if (!result.ok || result.status == AIVM_VM_STATUS_ERROR) {
            runtime_loop_status = "fail";
            failures += 1;
            break;
        }
        runtime_loop_ticks += 2U;
    }

    for (i = 0; i < iterations; i += 1) {
        AivmProgram program;
        if (!parse_simple_program_aos_to_program_text(program_bench_source, &program)) {
            compiler_program_status = "fail";
            failures += 1;
            break;
        }
        compiler_program_ticks += (uint64_t)(program.instruction_count + program.constant_count);
    }

    for (i = 0; i < iterations; i += 1) {
        AivmProgram program;
        if (!parse_bytecode_aos_to_program_text(bytecode_bench_source, &program)) {
            compiler_bytecode_status = "fail";
            failures += 1;
            break;
        }
        compiler_bytecode_ticks += (uint64_t)(program.instruction_count + program.constant_count);
    }

    {
        AivmProgram hello_program;
        if (!parse_simple_program_aos_to_program_text(app_hello_source, &hello_program) ||
            !bench_execute_program_iterations(&hello_program, iterations, &app_hello_ticks)) {
            app_hello_status = "fail";
            failures += 1;
        }
    }

    {
        AivmProgram echo_program;
        if (!parse_simple_program_aos_to_program_text(app_echo_source, &echo_program) ||
            !bench_execute_program_iterations(&echo_program, iterations, &app_echo_ticks)) {
            app_echo_status = "fail";
            failures += 1;
        }
    }

    {
        AivmProgram bundle_program;
        if (!parse_bytecode_aos_to_program_text(app_bundle_source, &bundle_program) ||
            !bench_execute_program_iterations(&bundle_program, iterations, &app_bundle_ticks)) {
            app_bundle_status = "fail";
            failures += 1;
        }
    }

    if (human) {
        printf("name\tstatus\tunit\tvalue\n");
        printf("runtime_loop\t%s\tvm_ticks\t%llu\n", runtime_loop_status, (unsigned long long)runtime_loop_ticks);
        printf("compiler_parse_program\t%s\tvm_ticks\t%llu\n", compiler_program_status, (unsigned long long)compiler_program_ticks);
        printf("compiler_parse_bytecode\t%s\tvm_ticks\t%llu\n", compiler_bytecode_status, (unsigned long long)compiler_bytecode_ticks);
        printf("app_run_hello\t%s\tvm_ticks\t%llu\n", app_hello_status, (unsigned long long)app_hello_ticks);
        printf("app_run_echo\t%s\tvm_ticks\t%llu\n", app_echo_status, (unsigned long long)app_echo_ticks);
        printf("app_run_bundle\t%s\tvm_ticks\t%llu\n", app_bundle_status, (unsigned long long)app_bundle_ticks);
    } else {
        printf("Ok#ok1(type=int value=%d)\n", iterations - failures);
    }
    if (failures > 0) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Native benchmark VM execution failed.\" nodeId=bench)\n");
        return 3;
    }
    return 0;
}

static int handle_publish(int argc, char** argv)
{
    const char* program_input = NULL;
    const char* target = NULL;
    const char* out_dir = "dist";
    const char* wasm_profile = "web";
    int wasm_profile_set = 0;
    char resolved_program[PATH_MAX];
    char source_aos[PATH_MAX];
    char artifact_dir[PATH_MAX];
    char runtime_bin[64];
    char publish_app_name[128];
    char publish_runtime_name[160];
    char runtime_src[PATH_MAX];
    char runtime_dst[PATH_MAX];
    char app_dst[PATH_MAX];
    char wasm_readme[PATH_MAX];
    char wasm_run_sh[PATH_MAX];
    char wasm_run_ps1[PATH_MAX];
    char wasm_index_html[PATH_MAX];
    char wasm_main_js[PATH_MAX];
    char manifest_target[64];
    int i;

    for (i = 2; i < argc; i++) {
        if (strcmp(argv[i], "--target") == 0) {
            if (i + 1 >= argc) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Missing --target value.\" nodeId=argv)\n");
                return 2;
            }
            target = argv[++i];
            continue;
        }
        if (strcmp(argv[i], "--out") == 0) {
            if (i + 1 >= argc) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Missing --out value.\" nodeId=argv)\n");
                return 2;
            }
            out_dir = argv[++i];
            continue;
        }
        if (strcmp(argv[i], "--wasm-profile") == 0) {
            if (i + 1 >= argc) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Missing --wasm-profile value.\" nodeId=argv)\n");
                return 2;
            }
            wasm_profile = argv[++i];
            wasm_profile_set = 1;
            if (strcmp(wasm_profile, "web") != 0 && strcmp(wasm_profile, "cli") != 0) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Unsupported --wasm-profile value. Use web or cli.\" nodeId=argv)\n");
                return 2;
            }
            continue;
        }
        if (program_input == NULL && argv[i][0] != '-') {
            program_input = argv[i];
            continue;
        }
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Unsupported publish argument.\" nodeId=argv)\n");
        return 2;
    }

    if (program_input == NULL) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Missing publish program path.\" nodeId=argv)\n");
        return 2;
    }

    if (!resolve_input_to_aibc1(program_input, resolved_program, sizeof(resolved_program))) {
        if (resolve_input_to_aos(program_input, source_aos, sizeof(source_aos))) {
            AivmProgram program;
            if (parse_bytecode_aos_to_program_file(source_aos, &program)) {
                if (!ensure_directory(out_dir)) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"Failed to create publish output directory.\" nodeId=outDir)\n");
                    return 2;
                }
                if (!join_path(out_dir, "app.aibc1", app_dst, sizeof(app_dst))) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"App destination path overflow.\" nodeId=publish)\n");
                    return 2;
                }
                if (!write_program_as_aibc1(&program, app_dst)) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"Failed to emit native AiBC1 for publish.\" nodeId=publish)\n");
                    return 2;
                }
                resolved_program[0] = '\0';
            } else {
                if (parse_simple_program_aos_to_program_file(source_aos, &program)) {
                    if (!ensure_directory(out_dir)) {
                        fprintf(stderr,
                            "Err#err1(code=RUN001 message=\"Failed to create publish output directory.\" nodeId=outDir)\n");
                        return 2;
                    }
                    if (!join_path(out_dir, "app.aibc1", app_dst, sizeof(app_dst))) {
                        fprintf(stderr,
                            "Err#err1(code=RUN001 message=\"App destination path overflow.\" nodeId=publish)\n");
                        return 2;
                    }
                    if (!write_program_as_aibc1(&program, app_dst)) {
                        fprintf(stderr,
                            "Err#err1(code=RUN001 message=\"Failed to emit native AiBC1 for publish.\" nodeId=publish)\n");
                        return 2;
                    }
                    resolved_program[0] = '\0';
                } else if (source_file_looks_like_bytecode_aos(source_aos)) {
                    fprintf(stderr,
                        "Err#err1(code=DEV008 message=\"Native publish cannot encode this bytecode AOS shape yet. Build precompiled .aibc1.\" nodeId=program)\n");
                    return 2;
                } else {
                    fprintf(stderr,
                        "Err#err1(code=DEV008 message=\"Publish needs prebuilt .aibc1 unless source is bytecode-style AOS.\" nodeId=program)\n");
                    return 2;
                }
            }
        } else {
            fprintf(stderr,
                "Err#err1(code=DEV008 message=\"Publish needs prebuilt .aibc1 unless source is bytecode-style AOS.\" nodeId=program)\n");
            return 2;
        }
    }

    if (target == NULL) {
        int manifest_target_rc = resolve_publish_target_from_manifest(program_input, manifest_target, sizeof(manifest_target));
        if (manifest_target_rc == -1) {
            fprintf(stderr,
                "Err#err1(code=VAL002 message=\"Project publishTargets must contain exactly one target or use --target explicitly.\" nodeId=project)\n");
            return 2;
        }
        if (manifest_target_rc == 1) {
            target = manifest_target;
        } else {
            target = host_rid();
        }
    }

    if (!parse_target_to_artifact(target, artifact_dir, sizeof(artifact_dir), runtime_bin, sizeof(runtime_bin))) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Unsupported publish target RID.\" nodeId=target)\n");
        return 2;
    }
    if (wasm_profile_set != 0 && strcmp(target, "wasm32") != 0) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"--wasm-profile is only valid with --target wasm32.\" nodeId=argv)\n");
        return 2;
    }
    if (!derive_publish_app_name(program_input, publish_app_name, sizeof(publish_app_name))) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to derive publish app name.\" nodeId=publish)\n");
        return 2;
    }
    {
        const char* runtime_ext = strrchr(runtime_bin, '.');
        if (runtime_ext != NULL) {
            if (snprintf(publish_runtime_name, sizeof(publish_runtime_name), "%s%s", publish_app_name, runtime_ext) >= (int)sizeof(publish_runtime_name)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Publish app name overflow.\" nodeId=publish)\n");
                return 2;
            }
        } else {
            if (snprintf(publish_runtime_name, sizeof(publish_runtime_name), "%s", publish_app_name) >= (int)sizeof(publish_runtime_name)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Publish app name overflow.\" nodeId=publish)\n");
                return 2;
            }
        }
    }

    if (!ensure_directory(out_dir)) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to create publish output directory.\" nodeId=outDir)\n");
        return 2;
    }

    if (!join_path(artifact_dir, runtime_bin, runtime_src, sizeof(runtime_src))) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Runtime source path overflow.\" nodeId=publish)\n");
        return 2;
    }
    if (!join_path(out_dir, publish_runtime_name, runtime_dst, sizeof(runtime_dst))) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Runtime destination path overflow.\" nodeId=publish)\n");
        return 2;
    }
    if (resolved_program[0] != '\0') {
        if (!join_path(out_dir, "app.aibc1", app_dst, sizeof(app_dst))) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"App destination path overflow.\" nodeId=publish)\n");
            return 2;
        }
        if (!copy_file(resolved_program, app_dst)) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Failed to copy app bytecode for publish.\" nodeId=publish)\n");
            return 2;
        }
    }

    if (!copy_runtime_file(runtime_src, runtime_dst)) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to copy runtime for target RID. Build target runtime first.\" nodeId=publish)\n");
        return 2;
    }

    if (strcmp(target, "wasm32") == 0) {
        char readme_content[1024];
        if (snprintf(readme_content, sizeof(readme_content),
                "# %s (wasm32)\n\n"
                "Artifacts:\n"
                "- `%s`\n"
                "- `app.aibc1`\n\n"
                "Profile: %s\n",
                publish_app_name,
                publish_runtime_name,
                wasm_profile) >= (int)sizeof(readme_content)) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Wasm README content overflow.\" nodeId=publish)\n");
            return 2;
        }
        if (!join_path(out_dir, "README.md", wasm_readme, sizeof(wasm_readme)) ||
            !write_text_file(wasm_readme, readme_content, 0)) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Failed to write wasm publish README.\" nodeId=publish)\n");
            return 2;
        }

        if (strcmp(wasm_profile, "cli") == 0) {
            char run_sh_content[512];
            char run_ps1_content[512];
            if (snprintf(run_sh_content, sizeof(run_sh_content),
                    "#!/usr/bin/env bash\nset -euo pipefail\nwasmtime \"./%s\" \"./app.aibc1\" \"$@\"\n",
                    publish_runtime_name) >= (int)sizeof(run_sh_content) ||
                snprintf(run_ps1_content, sizeof(run_ps1_content),
                    "$ErrorActionPreference = 'Stop'\n& wasmtime \".\\%s\" \".\\app.aibc1\" @args\nexit $LASTEXITCODE\n",
                    publish_runtime_name) >= (int)sizeof(run_ps1_content)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Wasm launcher content overflow.\" nodeId=publish)\n");
                return 2;
            }
            if (!join_path(out_dir, "run.sh", wasm_run_sh, sizeof(wasm_run_sh)) ||
                !join_path(out_dir, "run.ps1", wasm_run_ps1, sizeof(wasm_run_ps1)) ||
                !write_text_file(wasm_run_sh, run_sh_content, 1) ||
                !write_text_file(wasm_run_ps1, run_ps1_content, 0)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Failed to write wasm CLI launcher files.\" nodeId=publish)\n");
                return 2;
            }
        } else {
            const char* index_html =
                "<!doctype html>\n"
                "<html lang=\"en\">\n"
                "  <head><meta charset=\"utf-8\"><title>AiLang wasm app</title></head>\n"
                "  <body>\n"
                "    <h1>AiLang wasm app</h1>\n"
                "    <pre id=\"log\"></pre>\n"
                "    <script type=\"module\" src=\"./main.js\"></script>\n"
                "  </body>\n"
                "</html>\n";
            char main_js[1024];
            if (snprintf(main_js, sizeof(main_js),
                    "const log = document.getElementById('log');\n"
                    "const wasmUrl = './%s';\n"
                    "const appUrl = './app.aibc1';\n"
                    "log.textContent = 'TODO: host bridge loader not implemented yet. Runtime=' + wasmUrl + ', App=' + appUrl + '\\n';\n",
                    publish_runtime_name) >= (int)sizeof(main_js)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Wasm web bootstrap content overflow.\" nodeId=publish)\n");
                return 2;
            }
            if (!join_path(out_dir, "index.html", wasm_index_html, sizeof(wasm_index_html)) ||
                !join_path(out_dir, "main.js", wasm_main_js, sizeof(wasm_main_js)) ||
                !write_text_file(wasm_index_html, index_html, 0) ||
                !write_text_file(wasm_main_js, main_js, 0)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Failed to write wasm web files.\" nodeId=publish)\n");
                return 2;
            }
        }
    }

    printf("Ok#ok1(type=string value=\"publish-complete\")\n");
    return 0;
}

int main(int argc, char** argv)
{
    char exe_path[PATH_MAX];
    char exe_dir[PATH_MAX];
    char bundled_aibc1[PATH_MAX];
    const char* exe_base;

    if (argv != NULL &&
        resolve_executable_path(argv[0], exe_path, sizeof(exe_path)) &&
        dirname_of(exe_path, exe_dir, sizeof(exe_dir)) &&
        join_path(exe_dir, "app.aibc1", bundled_aibc1, sizeof(bundled_aibc1)) &&
        file_exists(bundled_aibc1)) {
        exe_base = path_basename_ptr(exe_path);
        if (exe_base != NULL && strcmp(exe_base, "airun") != 0 && strcmp(exe_base, "airun.exe") != 0) {
            const char* const* app_argv = (argc > 1) ? (const char* const*)&argv[1] : NULL;
            size_t app_argc = (argc > 1) ? (size_t)(argc - 1) : 0U;
            return run_native_aibc1(bundled_aibc1, app_argv, app_argc);
        }
    }

    if (argc <= 1 || argv == NULL) {
        print_usage();
        return 2;
    }

    if (strcmp(argv[1], "version") == 0 || strcmp(argv[1], "--version") == 0) {
        printf("airun-native-c abi=%u\n", aivm_c_abi_version());
        return 0;
    }
    if (strcmp(argv[1], "help") == 0 || strcmp(argv[1], "--help") == 0) {
        print_usage();
        return 0;
    }
    if (strcmp(argv[1], "run") == 0) {
        return handle_run(argc, argv);
    }
    if (strcmp(argv[1], "serve") == 0) {
        return handle_serve(argc, argv);
    }
    if (strcmp(argv[1], "debug") == 0) {
        return handle_debug(argc, argv);
    }
    if (strcmp(argv[1], "repl") == 0) {
        return handle_repl();
    }
    if (strcmp(argv[1], "bench") == 0) {
        return handle_bench(argc, argv);
    }
    if (strcmp(argv[1], "publish") == 0) {
        return handle_publish(argc, argv);
    }

    fprintf(stderr, "Unknown command: %s\n", argv[1]);
    print_usage();
    return 2;
}
