#include <errno.h>
#include <ctype.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <direct.h>
#include <io.h>
#include <process.h>
#include <sys/stat.h>
#include <windows.h>
#include <psapi.h>
#ifndef PATH_MAX
#define PATH_MAX 260
#endif
#define AIVM_PATH_SEP '\\'
#define AIVM_EXE_EXT ".exe"
#else
#include <sys/resource.h>
#include <dirent.h>
#include <fcntl.h>
#include <signal.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <sys/time.h>
#include <unistd.h>
#include <sys/wait.h>
/* Some test translation units include this file directly without POSIX feature
   macros, which can hide realpath(3) declaration on glibc. */
extern char* realpath(const char* path, char* resolved_path);
extern int lstat(const char* path, struct stat* buffer);
extern int kill(pid_t pid, int sig);
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

static int sample_process_rss_kb(int64_t* out_rss_kb)
{
    if (out_rss_kb == NULL) {
        return 0;
    }
#ifdef _WIN32
    PROCESS_MEMORY_COUNTERS pmc;
    if (!GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc))) {
        return 0;
    }
    *out_rss_kb = (int64_t)(pmc.PeakWorkingSetSize / 1024ULL);
    return 1;
#else
    struct rusage usage;
    if (getrusage(RUSAGE_SELF, &usage) != 0) {
        return 0;
    }
#ifdef __APPLE__
    *out_rss_kb = (int64_t)(usage.ru_maxrss / 1024L);
#else
    *out_rss_kb = (int64_t)usage.ru_maxrss;
#endif
    return 1;
#endif
}

static int monotonic_time_ns(int64_t* out_ns)
{
    if (out_ns == NULL) {
        return 0;
    }
#ifdef _WIN32
    LARGE_INTEGER freq;
    LARGE_INTEGER counter;
    if (!QueryPerformanceFrequency(&freq) || !QueryPerformanceCounter(&counter) || freq.QuadPart <= 0) {
        return 0;
    }
    *out_ns = (int64_t)((counter.QuadPart * 1000000000LL) / freq.QuadPart);
    return 1;
#else
    struct timeval tv;
    if (gettimeofday(&tv, NULL) != 0) {
        return 0;
    }
    *out_ns = ((int64_t)tv.tv_sec * 1000000000LL) + ((int64_t)tv.tv_usec * 1000LL);
    return 1;
#endif
}

static void write_toml_escaped(FILE* f, const char* value)
{
    const unsigned char* p = (const unsigned char*)value;
    if (f == NULL) {
        return;
    }
    if (value == NULL) {
        return;
    }
    while (*p != '\0') {
        if (*p == '\\' || *p == '"') {
            (void)fputc('\\', f);
            (void)fputc((int)*p, f);
        } else if (*p == '\n') {
            (void)fputs("\\n", f);
        } else if (*p == '\r') {
            (void)fputs("\\r", f);
        } else if (*p == '\t') {
            (void)fputs("\\t", f);
        } else {
            (void)fputc((int)*p, f);
        }
        p += 1;
    }
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
        "  debug mem <program> [--iterations <n>] [--max-growth-kb <n>] [--out <path>] [--vm=<selector>]\n"
        "  debug profile <program> [--iterations <n>] [--max-growth-kb <n>] [--out <path>] [--vm=<selector>]\n"
        "  publish <program(.aibc1|.aos|project-dir|project.aiproj)> [--target <rid>] [--out <dir>]\n"
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

#define NATIVE_PROCESS_CAPACITY 32U
#define NATIVE_PROCESS_READ_CHUNK 4096U

typedef struct NativeProcessState
{
    int used;
    int finished;
    int exit_code;
    int stdout_closed;
    int stderr_closed;
#ifdef _WIN32
    HANDLE process_handle;
    HANDLE stdout_read;
    HANDLE stderr_read;
#else
    pid_t pid;
    int stdout_fd;
    int stderr_fd;
#endif
} NativeProcessState;

static NativeProcessState g_native_processes[NATIVE_PROCESS_CAPACITY];
static uint8_t g_native_process_read_scratch[NATIVE_PROCESS_READ_CHUNK];

static void native_process_init_slot(NativeProcessState* process)
{
    if (process == NULL) {
        return;
    }
    memset(process, 0, sizeof(*process));
#ifndef _WIN32
    process->pid = (pid_t)-1;
    process->stdout_fd = -1;
    process->stderr_fd = -1;
#endif
}

static void native_process_release_slot(NativeProcessState* process)
{
    if (process == NULL || !process->used) {
        return;
    }
#ifdef _WIN32
    if (process->process_handle != NULL) {
        CloseHandle(process->process_handle);
        process->process_handle = NULL;
    }
    if (process->stdout_read != NULL) {
        CloseHandle(process->stdout_read);
        process->stdout_read = NULL;
    }
    if (process->stderr_read != NULL) {
        CloseHandle(process->stderr_read);
        process->stderr_read = NULL;
    }
#else
    if (process->stdout_fd >= 0) {
        close(process->stdout_fd);
        process->stdout_fd = -1;
    }
    if (process->stderr_fd >= 0) {
        close(process->stderr_fd);
        process->stderr_fd = -1;
    }
#endif
    native_process_init_slot(process);
}

static NativeProcessState* native_process_lookup(int64_t handle_value)
{
    size_t index;
    if (handle_value <= 0 || handle_value > (int64_t)NATIVE_PROCESS_CAPACITY) {
        return NULL;
    }
    index = (size_t)(handle_value - 1);
    if (!g_native_processes[index].used) {
        return NULL;
    }
    return &g_native_processes[index];
}

static int64_t native_process_allocate_slot(void)
{
    size_t index;
    for (index = 0U; index < NATIVE_PROCESS_CAPACITY; index += 1U) {
        if (!g_native_processes[index].used) {
            native_process_init_slot(&g_native_processes[index]);
            g_native_processes[index].used = 1;
            return (int64_t)(index + 1U);
        }
    }
    return -1;
}

#ifdef _WIN32
static int native_delete_dir_recursive_windows(const char* path)
{
    char pattern[PATH_MAX];
    WIN32_FIND_DATAA find_data;
    HANDLE find_handle;

    if (path == NULL) {
        return 0;
    }
    if (!join_path(path, "*", pattern, sizeof(pattern))) {
        return 0;
    }

    find_handle = FindFirstFileA(pattern, &find_data);
    if (find_handle == INVALID_HANDLE_VALUE) {
        return RemoveDirectoryA(path) != 0;
    }

    do {
        char child[PATH_MAX];
        if (strcmp(find_data.cFileName, ".") == 0 || strcmp(find_data.cFileName, "..") == 0) {
            continue;
        }
        if (!join_path(path, find_data.cFileName, child, sizeof(child))) {
            FindClose(find_handle);
            return 0;
        }
        if ((find_data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0U) {
            if (!native_delete_dir_recursive_windows(child)) {
                FindClose(find_handle);
                return 0;
            }
        } else {
            if (!DeleteFileA(child)) {
                FindClose(find_handle);
                return 0;
            }
        }
    } while (FindNextFileA(find_handle, &find_data) != 0);

    FindClose(find_handle);
    return RemoveDirectoryA(path) != 0;
}

static void native_process_refresh(NativeProcessState* process)
{
    DWORD wait_status;
    DWORD exit_code;
    if (process == NULL || process->finished || process->process_handle == NULL) {
        return;
    }
    wait_status = WaitForSingleObject(process->process_handle, 0);
    if (wait_status == WAIT_OBJECT_0) {
        process->finished = 1;
        if (GetExitCodeProcess(process->process_handle, &exit_code) != 0) {
            process->exit_code = (int)exit_code;
        } else {
            process->exit_code = -1;
        }
    }
}
#else
static void native_process_set_nonblocking(int fd)
{
    int flags;
    if (fd < 0) {
        return;
    }
    flags = fcntl(fd, F_GETFL, 0);
    if (flags >= 0) {
        (void)fcntl(fd, F_SETFL, flags | O_NONBLOCK);
    }
}

static int native_delete_dir_recursive_posix(const char* path)
{
    DIR* dir;
    struct dirent* entry;
    if (path == NULL) {
        return 0;
    }
    dir = opendir(path);
    if (dir == NULL) {
        return 0;
    }
    while ((entry = readdir(dir)) != NULL) {
        char child[PATH_MAX];
        struct stat st;
        if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0) {
            continue;
        }
        if (!join_path(path, entry->d_name, child, sizeof(child))) {
            (void)closedir(dir);
            return 0;
        }
        if (lstat(child, &st) != 0) {
            (void)closedir(dir);
            return 0;
        }
        if (S_ISDIR(st.st_mode)) {
            if (!native_delete_dir_recursive_posix(child)) {
                (void)closedir(dir);
                return 0;
            }
        } else {
            if (remove(child) != 0) {
                (void)closedir(dir);
                return 0;
            }
        }
    }
    (void)closedir(dir);
    return rmdir(path) == 0;
}

static void native_process_refresh(NativeProcessState* process)
{
    int status;
    pid_t wait_result;
    if (process == NULL || process->finished) {
        return;
    }
    wait_result = waitpid(process->pid, &status, WNOHANG);
    if (wait_result == process->pid) {
        process->finished = 1;
        if (WIFEXITED(status)) {
            process->exit_code = WEXITSTATUS(status);
        } else if (WIFSIGNALED(status)) {
            process->exit_code = 128 + WTERMSIG(status);
        } else {
            process->exit_code = -1;
        }
    }
}
#endif

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

static int native_syscall_fs_file_delete(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    struct stat st;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (stat(args[0].string_value, &st) != 0) {
        *result = aivm_value_bool(0);
        return AIVM_SYSCALL_OK;
    }
#ifdef _WIN32
    if ((st.st_mode & _S_IFREG) == 0) {
#else
    if (!S_ISREG(st.st_mode)) {
#endif
        *result = aivm_value_bool(0);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_bool(remove(args[0].string_value) == 0 ? 1 : 0);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_fs_dir_delete(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    const char* path;
    int recursive;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 2U || args == NULL || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL || args[1].type != AIVM_VAL_BOOL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    path = args[0].string_value;
    recursive = args[1].bool_value ? 1 : 0;
#ifdef _WIN32
    if (recursive) {
        *result = aivm_value_bool(native_delete_dir_recursive_windows(path) ? 1 : 0);
    } else {
        *result = aivm_value_bool(_rmdir(path) == 0 ? 1 : 0);
    }
#else
    if (recursive) {
        *result = aivm_value_bool(native_delete_dir_recursive_posix(path) ? 1 : 0);
    } else {
        *result = aivm_value_bool(rmdir(path) == 0 ? 1 : 0);
    }
#endif
    return AIVM_SYSCALL_OK;
}

static int native_syscall_process_spawn(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 4U || args == NULL ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_NODE ||
        args[2].type != AIVM_VAL_STRING || args[2].string_value == NULL ||
        args[3].type != AIVM_VAL_NODE) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
#ifdef _WIN32
    {
        SECURITY_ATTRIBUTES security_attributes;
        HANDLE stdout_read = NULL;
        HANDLE stdout_write = NULL;
        HANDLE stderr_read = NULL;
        HANDLE stderr_write = NULL;
        PROCESS_INFORMATION process_info;
        STARTUPINFOA startup_info;
        int64_t slot_handle;
        NativeProcessState* process;
        char* command_line;
        const char* cwd = NULL;

        memset(&security_attributes, 0, sizeof(security_attributes));
        security_attributes.nLength = sizeof(security_attributes);
        security_attributes.bInheritHandle = TRUE;
        security_attributes.lpSecurityDescriptor = NULL;

        if (!CreatePipe(&stdout_read, &stdout_write, &security_attributes, 0) ||
            !CreatePipe(&stderr_read, &stderr_write, &security_attributes, 0)) {
            if (stdout_read != NULL) {
                CloseHandle(stdout_read);
            }
            if (stdout_write != NULL) {
                CloseHandle(stdout_write);
            }
            if (stderr_read != NULL) {
                CloseHandle(stderr_read);
            }
            if (stderr_write != NULL) {
                CloseHandle(stderr_write);
            }
            *result = aivm_value_int(-1);
            return AIVM_SYSCALL_OK;
        }

        (void)SetHandleInformation(stdout_read, HANDLE_FLAG_INHERIT, 0);
        (void)SetHandleInformation(stderr_read, HANDLE_FLAG_INHERIT, 0);

        memset(&startup_info, 0, sizeof(startup_info));
        memset(&process_info, 0, sizeof(process_info));
        startup_info.cb = sizeof(startup_info);
        startup_info.dwFlags = STARTF_USESTDHANDLES;
        startup_info.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
        startup_info.hStdOutput = stdout_write;
        startup_info.hStdError = stderr_write;

        command_line = _strdup(args[0].string_value);
        if (command_line == NULL) {
            CloseHandle(stdout_read);
            CloseHandle(stdout_write);
            CloseHandle(stderr_read);
            CloseHandle(stderr_write);
            *result = aivm_value_int(-1);
            return AIVM_SYSCALL_OK;
        }
        if (args[2].string_value[0] != '\0') {
            cwd = args[2].string_value;
        }

        if (!CreateProcessA(
                NULL,
                command_line,
                NULL,
                NULL,
                TRUE,
                CREATE_NO_WINDOW,
                NULL,
                cwd,
                &startup_info,
                &process_info)) {
            free(command_line);
            CloseHandle(stdout_read);
            CloseHandle(stdout_write);
            CloseHandle(stderr_read);
            CloseHandle(stderr_write);
            *result = aivm_value_int(-1);
            return AIVM_SYSCALL_OK;
        }

        free(command_line);
        CloseHandle(stdout_write);
        CloseHandle(stderr_write);
        CloseHandle(process_info.hThread);

        slot_handle = native_process_allocate_slot();
        if (slot_handle < 0) {
            (void)TerminateProcess(process_info.hProcess, 1);
            CloseHandle(process_info.hProcess);
            CloseHandle(stdout_read);
            CloseHandle(stderr_read);
            *result = aivm_value_int(-1);
            return AIVM_SYSCALL_OK;
        }

        process = native_process_lookup(slot_handle);
        if (process == NULL) {
            (void)TerminateProcess(process_info.hProcess, 1);
            CloseHandle(process_info.hProcess);
            CloseHandle(stdout_read);
            CloseHandle(stderr_read);
            *result = aivm_value_int(-1);
            return AIVM_SYSCALL_OK;
        }
        process->process_handle = process_info.hProcess;
        process->stdout_read = stdout_read;
        process->stderr_read = stderr_read;
        *result = aivm_value_int(slot_handle);
        return AIVM_SYSCALL_OK;
    }
#else
    {
        int stdout_pipe[2] = { -1, -1 };
        int stderr_pipe[2] = { -1, -1 };
        pid_t pid;
        int64_t slot_handle;
        NativeProcessState* process;
        if (pipe(stdout_pipe) != 0 || pipe(stderr_pipe) != 0) {
            if (stdout_pipe[0] >= 0) {
                close(stdout_pipe[0]);
            }
            if (stdout_pipe[1] >= 0) {
                close(stdout_pipe[1]);
            }
            if (stderr_pipe[0] >= 0) {
                close(stderr_pipe[0]);
            }
            if (stderr_pipe[1] >= 0) {
                close(stderr_pipe[1]);
            }
            *result = aivm_value_int(-1);
            return AIVM_SYSCALL_OK;
        }

        pid = fork();
        if (pid == 0) {
            if (args[2].string_value[0] != '\0') {
                if (chdir(args[2].string_value) != 0) {
                    _exit(126);
                }
            }
            dup2(stdout_pipe[1], STDOUT_FILENO);
            dup2(stderr_pipe[1], STDERR_FILENO);
            close(stdout_pipe[0]);
            close(stdout_pipe[1]);
            close(stderr_pipe[0]);
            close(stderr_pipe[1]);
            execl("/bin/sh", "sh", "-c", args[0].string_value, (char*)NULL);
            _exit(127);
        }
        if (pid < 0) {
            close(stdout_pipe[0]);
            close(stdout_pipe[1]);
            close(stderr_pipe[0]);
            close(stderr_pipe[1]);
            *result = aivm_value_int(-1);
            return AIVM_SYSCALL_OK;
        }

        slot_handle = native_process_allocate_slot();
        if (slot_handle < 0) {
            kill(pid, SIGTERM);
            close(stdout_pipe[0]);
            close(stdout_pipe[1]);
            close(stderr_pipe[0]);
            close(stderr_pipe[1]);
            *result = aivm_value_int(-1);
            return AIVM_SYSCALL_OK;
        }

        close(stdout_pipe[1]);
        close(stderr_pipe[1]);
        process = native_process_lookup(slot_handle);
        if (process == NULL) {
            kill(pid, SIGTERM);
            close(stdout_pipe[0]);
            close(stderr_pipe[0]);
            *result = aivm_value_int(-1);
            return AIVM_SYSCALL_OK;
        }
        process->pid = pid;
        process->stdout_fd = stdout_pipe[0];
        process->stderr_fd = stderr_pipe[0];
        native_process_set_nonblocking(process->stdout_fd);
        native_process_set_nonblocking(process->stderr_fd);
        *result = aivm_value_int(slot_handle);
        return AIVM_SYSCALL_OK;
    }
#endif
}

static int native_syscall_process_wait(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeProcessState* process;
    int exit_code;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    process = native_process_lookup(args[0].int_value);
    if (process == NULL) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
#ifdef _WIN32
    if (!process->finished) {
        DWORD wait_status = WaitForSingleObject(process->process_handle, INFINITE);
        if (wait_status == WAIT_OBJECT_0) {
            DWORD exit_code;
            process->finished = 1;
            if (GetExitCodeProcess(process->process_handle, &exit_code) != 0) {
                process->exit_code = (int)exit_code;
            } else {
                process->exit_code = -1;
            }
        }
    }
    exit_code = process->exit_code;
    *result = aivm_value_int((int64_t)exit_code);
    native_process_release_slot(process);
    return AIVM_SYSCALL_OK;
#else
    if (!process->finished) {
        int status;
        pid_t wait_result = waitpid(process->pid, &status, 0);
        if (wait_result == process->pid) {
            process->finished = 1;
            if (WIFEXITED(status)) {
                process->exit_code = WEXITSTATUS(status);
            } else if (WIFSIGNALED(status)) {
                process->exit_code = 128 + WTERMSIG(status);
            } else {
                process->exit_code = -1;
            }
        }
    }
    exit_code = process->exit_code;
    *result = aivm_value_int((int64_t)exit_code);
    native_process_release_slot(process);
    return AIVM_SYSCALL_OK;
#endif
}

static int native_syscall_process_poll(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeProcessState* process;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    process = native_process_lookup(args[0].int_value);
    if (process == NULL) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
#ifdef _WIN32
    native_process_refresh(process);
    *result = aivm_value_int(process->finished ? 1 : 0);
    return AIVM_SYSCALL_OK;
#else
    native_process_refresh(process);
    *result = aivm_value_int(process->finished ? 1 : 0);
    return AIVM_SYSCALL_OK;
#endif
}

static int native_syscall_process_kill(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeProcessState* process;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    process = native_process_lookup(args[0].int_value);
    if (process == NULL) {
        *result = aivm_value_bool(0);
        return AIVM_SYSCALL_OK;
    }
#ifdef _WIN32
    if (process->finished) {
        *result = aivm_value_bool(0);
    } else {
        *result = aivm_value_bool(TerminateProcess(process->process_handle, 1) ? 1 : 0);
    }
#else
    if (process->finished) {
        *result = aivm_value_bool(0);
    } else {
        *result = aivm_value_bool(kill(process->pid, SIGTERM) == 0 ? 1 : 0);
    }
#endif
    return AIVM_SYSCALL_OK;
}

static int native_syscall_process_stream_read(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result,
    int read_stdout)
{
    NativeProcessState* process;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    process = native_process_lookup(args[0].int_value);
    if (process == NULL) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
#ifdef _WIN32
    {
        HANDLE stream = read_stdout ? process->stdout_read : process->stderr_read;
        int* closed_flag = read_stdout ? &process->stdout_closed : &process->stderr_closed;
        DWORD available = 0;
        DWORD read_count = 0;
        if (*closed_flag || stream == NULL) {
            *result = aivm_value_bytes(NULL, 0U);
            return AIVM_SYSCALL_OK;
        }
        if (!PeekNamedPipe(stream, NULL, 0, NULL, &available, NULL)) {
            DWORD err = GetLastError();
            if (err == ERROR_BROKEN_PIPE) {
                CloseHandle(stream);
                if (read_stdout) {
                    process->stdout_read = NULL;
                } else {
                    process->stderr_read = NULL;
                }
                *closed_flag = 1;
                *result = aivm_value_bytes(NULL, 0U);
                return AIVM_SYSCALL_OK;
            }
            *result = aivm_value_bytes(NULL, 0U);
            return AIVM_SYSCALL_OK;
        }
        if (available == 0) {
            *result = aivm_value_bytes(NULL, 0U);
            return AIVM_SYSCALL_OK;
        }
        if (available > (DWORD)NATIVE_PROCESS_READ_CHUNK) {
            available = (DWORD)NATIVE_PROCESS_READ_CHUNK;
        }
        if (!ReadFile(stream, g_native_process_read_scratch, available, &read_count, NULL)) {
            DWORD err = GetLastError();
            if (err == ERROR_BROKEN_PIPE) {
                CloseHandle(stream);
                if (read_stdout) {
                    process->stdout_read = NULL;
                } else {
                    process->stderr_read = NULL;
                }
                *closed_flag = 1;
            }
            *result = aivm_value_bytes(NULL, 0U);
            return AIVM_SYSCALL_OK;
        }
        if (read_count == 0) {
            *result = aivm_value_bytes(NULL, 0U);
            return AIVM_SYSCALL_OK;
        }
        *result = aivm_value_bytes(g_native_process_read_scratch, (size_t)read_count);
        return AIVM_SYSCALL_OK;
    }
#else
    {
        int fd = read_stdout ? process->stdout_fd : process->stderr_fd;
        int* closed_flag = read_stdout ? &process->stdout_closed : &process->stderr_closed;
        ssize_t read_count;
        if (*closed_flag || fd < 0) {
            *result = aivm_value_bytes(NULL, 0U);
            return AIVM_SYSCALL_OK;
        }
        read_count = read(fd, g_native_process_read_scratch, NATIVE_PROCESS_READ_CHUNK);
        if (read_count > 0) {
            *result = aivm_value_bytes(g_native_process_read_scratch, (size_t)read_count);
            return AIVM_SYSCALL_OK;
        }
        if (read_count == 0) {
            close(fd);
            *closed_flag = 1;
            if (read_stdout) {
                process->stdout_fd = -1;
            } else {
                process->stderr_fd = -1;
            }
            *result = aivm_value_bytes(NULL, 0U);
            return AIVM_SYSCALL_OK;
        }
        if (errno == EAGAIN || errno == EWOULDBLOCK) {
            *result = aivm_value_bytes(NULL, 0U);
            return AIVM_SYSCALL_OK;
        }
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
#endif
}

static int native_syscall_process_stdout_read(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    return native_syscall_process_stream_read(target, args, arg_count, result, 1);
}

static int native_syscall_process_stderr_read(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    return native_syscall_process_stream_read(target, args, arg_count, result, 0);
}

static int native_base64_decode_char(char ch)
{
    if (ch >= 'A' && ch <= 'Z') {
        return (int)(ch - 'A');
    }
    if (ch >= 'a' && ch <= 'z') {
        return (int)(ch - 'a') + 26;
    }
    if (ch >= '0' && ch <= '9') {
        return (int)(ch - '0') + 52;
    }
    if (ch == '+') {
        return 62;
    }
    if (ch == '/') {
        return 63;
    }
    return -1;
}

#define NATIVE_BYTES_SCRATCH_CAPACITY 131072U
static uint8_t g_native_bytes_scratch[NATIVE_BYTES_SCRATCH_CAPACITY];
static char g_native_base64_scratch[NATIVE_BYTES_SCRATCH_CAPACITY];
static char g_native_utf8_scratch[8];

static int native_bytes_from_base64(
    const char* input,
    uint8_t* out_bytes,
    size_t out_capacity,
    size_t* out_length)
{
    size_t input_len;
    size_t i;
    size_t out_index = 0U;
    if (out_length == NULL) {
        return 0;
    }
    *out_length = 0U;
    if (input == NULL) {
        return 0;
    }
    input_len = strlen(input);
    if (input_len == 0U) {
        return 1;
    }
    if ((input_len % 4U) != 0U) {
        return 0;
    }

    for (i = 0U; i < input_len; i += 4U) {
        int c0 = native_base64_decode_char(input[i]);
        int c1 = native_base64_decode_char(input[i + 1U]);
        int c2;
        int c3;
        uint32_t chunk;
        int pad = 0;
        if (c0 < 0 || c1 < 0) {
            return 0;
        }
        if (input[i + 2U] == '=') {
            c2 = 0;
            pad += 1;
            if (input[i + 3U] != '=') {
                return 0;
            }
            c3 = 0;
            pad += 1;
        } else {
            c2 = native_base64_decode_char(input[i + 2U]);
            if (c2 < 0) {
                return 0;
            }
            if (input[i + 3U] == '=') {
                c3 = 0;
                pad += 1;
            } else {
                c3 = native_base64_decode_char(input[i + 3U]);
                if (c3 < 0) {
                    return 0;
                }
            }
        }
        if (pad > 0 && i + 4U != input_len) {
            return 0;
        }
        chunk = ((uint32_t)c0 << 18U) |
                ((uint32_t)c1 << 12U) |
                ((uint32_t)c2 << 6U) |
                (uint32_t)c3;

        if (out_bytes != NULL && out_index < out_capacity) {
            out_bytes[out_index] = (uint8_t)((chunk >> 16U) & 0xffU);
        }
        out_index += 1U;
        if (pad < 2) {
            if (out_bytes != NULL && out_index < out_capacity) {
                out_bytes[out_index] = (uint8_t)((chunk >> 8U) & 0xffU);
            }
            out_index += 1U;
        }
        if (pad == 0) {
            if (out_bytes != NULL && out_index < out_capacity) {
                out_bytes[out_index] = (uint8_t)(chunk & 0xffU);
            }
            out_index += 1U;
        }
    }

    if (out_bytes != NULL && out_index > out_capacity) {
        return 0;
    }
    *out_length = out_index;
    return 1;
}

static int native_bytes_to_base64(
    const uint8_t* input,
    size_t input_len,
    char* out_text,
    size_t out_capacity)
{
    static const char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    size_t i = 0U;
    size_t out_index = 0U;

    if (out_capacity == 0U) {
        return 0;
    }
    if (input_len == 0U) {
        if (out_capacity < 1U) {
            return 0;
        }
        out_text[0] = '\0';
        return 1;
    }
    if (input == NULL) {
        return 0;
    }

    while (i < input_len) {
        uint32_t chunk = 0U;
        size_t remain = input_len - i;
        size_t bytes_in_chunk = remain >= 3U ? 3U : remain;
        chunk |= (uint32_t)input[i] << 16U;
        if (bytes_in_chunk > 1U) {
            chunk |= (uint32_t)input[i + 1U] << 8U;
        }
        if (bytes_in_chunk > 2U) {
            chunk |= (uint32_t)input[i + 2U];
        }
        if (out_index + 4U >= out_capacity) {
            return 0;
        }
        out_text[out_index++] = alphabet[(chunk >> 18U) & 0x3fU];
        out_text[out_index++] = alphabet[(chunk >> 12U) & 0x3fU];
        out_text[out_index++] = (bytes_in_chunk > 1U) ? alphabet[(chunk >> 6U) & 0x3fU] : '=';
        out_text[out_index++] = (bytes_in_chunk > 2U) ? alphabet[chunk & 0x3fU] : '=';
        i += bytes_in_chunk;
    }
    out_text[out_index] = '\0';
    return 1;
}

static int native_is_valid_utf8_without_nul(const uint8_t* data, size_t len)
{
    size_t i = 0U;
    if (data == NULL) {
        return len == 0U;
    }
    while (i < len) {
        uint8_t b0 = data[i];
        if (b0 == 0U) {
            return 0;
        }
        if (b0 <= 0x7FU) {
            i += 1U;
            continue;
        }
        if (b0 >= 0xC2U && b0 <= 0xDFU) {
            if (i + 1U >= len) {
                return 0;
            }
            if ((data[i + 1U] & 0xC0U) != 0x80U) {
                return 0;
            }
            i += 2U;
            continue;
        }
        if (b0 == 0xE0U) {
            if (i + 2U >= len) {
                return 0;
            }
            if (data[i + 1U] < 0xA0U || data[i + 1U] > 0xBFU) {
                return 0;
            }
            if ((data[i + 2U] & 0xC0U) != 0x80U) {
                return 0;
            }
            i += 3U;
            continue;
        }
        if ((b0 >= 0xE1U && b0 <= 0xECU) || (b0 >= 0xEEU && b0 <= 0xEFU)) {
            if (i + 2U >= len) {
                return 0;
            }
            if ((data[i + 1U] & 0xC0U) != 0x80U || (data[i + 2U] & 0xC0U) != 0x80U) {
                return 0;
            }
            i += 3U;
            continue;
        }
        if (b0 == 0xEDU) {
            if (i + 2U >= len) {
                return 0;
            }
            if (data[i + 1U] < 0x80U || data[i + 1U] > 0x9FU) {
                return 0;
            }
            if ((data[i + 2U] & 0xC0U) != 0x80U) {
                return 0;
            }
            i += 3U;
            continue;
        }
        if (b0 == 0xF0U) {
            if (i + 3U >= len) {
                return 0;
            }
            if (data[i + 1U] < 0x90U || data[i + 1U] > 0xBFU) {
                return 0;
            }
            if ((data[i + 2U] & 0xC0U) != 0x80U || (data[i + 3U] & 0xC0U) != 0x80U) {
                return 0;
            }
            i += 4U;
            continue;
        }
        if (b0 >= 0xF1U && b0 <= 0xF3U) {
            if (i + 3U >= len) {
                return 0;
            }
            if ((data[i + 1U] & 0xC0U) != 0x80U ||
                (data[i + 2U] & 0xC0U) != 0x80U ||
                (data[i + 3U] & 0xC0U) != 0x80U) {
                return 0;
            }
            i += 4U;
            continue;
        }
        if (b0 == 0xF4U) {
            if (i + 3U >= len) {
                return 0;
            }
            if (data[i + 1U] < 0x80U || data[i + 1U] > 0x8FU) {
                return 0;
            }
            if ((data[i + 2U] & 0xC0U) != 0x80U || (data[i + 3U] & 0xC0U) != 0x80U) {
                return 0;
            }
            i += 4U;
            continue;
        }
        return 0;
    }
    return 1;
}

static int native_syscall_bytes_length(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_BYTES) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    *result = aivm_value_int((int64_t)args[0].bytes_value.length);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_bytes_at(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t index;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 2U || args == NULL || args[0].type != AIVM_VAL_BYTES || args[1].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (args[1].int_value < 0) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    index = (size_t)args[1].int_value;
    if (index >= args[0].bytes_value.length || args[0].bytes_value.data == NULL) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_int((int64_t)args[0].bytes_value.data[index]);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_bytes_slice(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t start;
    size_t length;
    size_t end;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 3U || args == NULL || args[0].type != AIVM_VAL_BYTES || args[1].type != AIVM_VAL_INT || args[2].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (args[1].int_value <= 0) {
        start = 0U;
    } else if ((uint64_t)args[1].int_value >= (uint64_t)args[0].bytes_value.length) {
        start = args[0].bytes_value.length;
    } else {
        start = (size_t)args[1].int_value;
    }
    if (args[2].int_value <= 0) {
        length = 0U;
    } else {
        length = (size_t)args[2].int_value;
    }
    end = start + length;
    if (end < start || end > args[0].bytes_value.length) {
        end = args[0].bytes_value.length;
    }
    if (start >= end || args[0].bytes_value.data == NULL) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_bytes(&args[0].bytes_value.data[start], end - start);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_bytes_concat(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t left_len;
    size_t right_len;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 2U || args == NULL || args[0].type != AIVM_VAL_BYTES || args[1].type != AIVM_VAL_BYTES) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    left_len = args[0].bytes_value.length;
    right_len = args[1].bytes_value.length;
    if (left_len == 0U && right_len == 0U) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
    if (left_len + right_len > NATIVE_BYTES_SCRATCH_CAPACITY) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (left_len > 0U && args[0].bytes_value.data != NULL) {
        memcpy(g_native_bytes_scratch, args[0].bytes_value.data, left_len);
    } else if (left_len > 0U) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (right_len > 0U && args[1].bytes_value.data != NULL) {
        memcpy(g_native_bytes_scratch + left_len, args[1].bytes_value.data, right_len);
    } else if (right_len > 0U) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_bytes(g_native_bytes_scratch, left_len + right_len);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_bytes_from_base64(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t out_len = 0U;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_bytes_from_base64(args[0].string_value, NULL, 0U, &out_len)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (out_len == 0U) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
    if (out_len > NATIVE_BYTES_SCRATCH_CAPACITY) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_bytes_from_base64(args[0].string_value, g_native_bytes_scratch, out_len, &out_len)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_bytes(g_native_bytes_scratch, out_len);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_bytes_to_base64(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t in_len;
    size_t out_len;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_BYTES) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    in_len = args[0].bytes_value.length;
    out_len = ((in_len + 2U) / 3U) * 4U;
    if (out_len + 1U > NATIVE_BYTES_SCRATCH_CAPACITY) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_bytes_to_base64(args[0].bytes_value.data, in_len, g_native_base64_scratch, out_len + 1U)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_string(g_native_base64_scratch);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_bytes_to_utf8_string(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t i;
    size_t in_len;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_BYTES) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    in_len = args[0].bytes_value.length;
    if (in_len == 0U) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    if (args[0].bytes_value.data == NULL || in_len + 1U > NATIVE_BYTES_SCRATCH_CAPACITY) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_is_valid_utf8_without_nul(args[0].bytes_value.data, in_len)) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    for (i = 0U; i < in_len; i += 1U) {
        g_native_base64_scratch[i] = (char)args[0].bytes_value.data[i];
    }
    g_native_base64_scratch[in_len] = '\0';
    *result = aivm_value_string(g_native_base64_scratch);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_str_from_codepoint(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    uint32_t cp;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (args[0].int_value < 0 || args[0].int_value > 0x10FFFFLL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    cp = (uint32_t)args[0].int_value;
    if (cp >= 0xD800U && cp <= 0xDFFFU) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (cp <= 0x7FU) {
        g_native_utf8_scratch[0] = (char)cp;
        g_native_utf8_scratch[1] = '\0';
    } else if (cp <= 0x7FFU) {
        g_native_utf8_scratch[0] = (char)(0xC0U | (cp >> 6U));
        g_native_utf8_scratch[1] = (char)(0x80U | (cp & 0x3FU));
        g_native_utf8_scratch[2] = '\0';
    } else if (cp <= 0xFFFFU) {
        g_native_utf8_scratch[0] = (char)(0xE0U | (cp >> 12U));
        g_native_utf8_scratch[1] = (char)(0x80U | ((cp >> 6U) & 0x3FU));
        g_native_utf8_scratch[2] = (char)(0x80U | (cp & 0x3FU));
        g_native_utf8_scratch[3] = '\0';
    } else {
        g_native_utf8_scratch[0] = (char)(0xF0U | (cp >> 18U));
        g_native_utf8_scratch[1] = (char)(0x80U | ((cp >> 12U) & 0x3FU));
        g_native_utf8_scratch[2] = (char)(0x80U | ((cp >> 6U) & 0x3FU));
        g_native_utf8_scratch[3] = (char)(0x80U | (cp & 0x3FU));
        g_native_utf8_scratch[4] = '\0';
    }
    *result = aivm_value_string(g_native_utf8_scratch);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_str_decode_unicode_hex4(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    const char* text;
    size_t len;
    uint32_t cp;
    char* end_ptr = NULL;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    text = args[0].string_value;
    len = strlen(text);
    if (len != 4U) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    cp = (uint32_t)strtoul(text, &end_ptr, 16);
    if (end_ptr == NULL || *end_ptr != '\0') {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    if (cp > 0x10FFFFU || (cp >= 0xD800U && cp <= 0xDFFFU)) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    if (cp <= 0x7FU) {
        g_native_utf8_scratch[0] = (char)cp;
        g_native_utf8_scratch[1] = '\0';
    } else if (cp <= 0x7FFU) {
        g_native_utf8_scratch[0] = (char)(0xC0U | (cp >> 6U));
        g_native_utf8_scratch[1] = (char)(0x80U | (cp & 0x3FU));
        g_native_utf8_scratch[2] = '\0';
    } else if (cp <= 0xFFFFU) {
        g_native_utf8_scratch[0] = (char)(0xE0U | (cp >> 12U));
        g_native_utf8_scratch[1] = (char)(0x80U | ((cp >> 6U) & 0x3FU));
        g_native_utf8_scratch[2] = (char)(0x80U | (cp & 0x3FU));
        g_native_utf8_scratch[3] = '\0';
    } else {
        g_native_utf8_scratch[0] = (char)(0xF0U | (cp >> 18U));
        g_native_utf8_scratch[1] = (char)(0x80U | ((cp >> 12U) & 0x3FU));
        g_native_utf8_scratch[2] = (char)(0x80U | ((cp >> 6U) & 0x3FU));
        g_native_utf8_scratch[3] = (char)(0x80U | (cp & 0x3FU));
        g_native_utf8_scratch[4] = '\0';
    }
    *result = aivm_value_string(g_native_utf8_scratch);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_str_decode_unicode_surrogate_pair_hex4(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    const char* high_text;
    const char* low_text;
    char* end_ptr = NULL;
    uint32_t high_surrogate;
    uint32_t low_surrogate;
    uint32_t cp;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 2U ||
        args == NULL ||
        args[0].type != AIVM_VAL_STRING ||
        args[1].type != AIVM_VAL_STRING ||
        args[0].string_value == NULL ||
        args[1].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    high_text = args[0].string_value;
    low_text = args[1].string_value;
    if (strlen(high_text) != 4U || strlen(low_text) != 4U) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    high_surrogate = (uint32_t)strtoul(high_text, &end_ptr, 16);
    if (end_ptr == NULL || *end_ptr != '\0') {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    low_surrogate = (uint32_t)strtoul(low_text, &end_ptr, 16);
    if (end_ptr == NULL || *end_ptr != '\0') {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    if (high_surrogate < 0xD800U || high_surrogate > 0xDBFFU ||
        low_surrogate < 0xDC00U || low_surrogate > 0xDFFFU) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    cp = 0x10000U + ((high_surrogate - 0xD800U) << 10U) + (low_surrogate - 0xDC00U);
    g_native_utf8_scratch[0] = (char)(0xF0U | (cp >> 18U));
    g_native_utf8_scratch[1] = (char)(0x80U | ((cp >> 12U) & 0x3FU));
    g_native_utf8_scratch[2] = (char)(0x80U | ((cp >> 6U) & 0x3FU));
    g_native_utf8_scratch[3] = (char)(0x80U | (cp & 0x3FU));
    g_native_utf8_scratch[4] = '\0';
    *result = aivm_value_string(g_native_utf8_scratch);
    return AIVM_SYSCALL_OK;
}

static int run_native_compiled_program(
    const AivmProgram* program,
    const char* vm_error_message,
    const char* const* process_argv,
    size_t process_argv_count)
{
    AivmSyscallBinding bindings[22];
    AivmCResult result;

    if (program == NULL) {
        return 2;
    }

    bindings[0].target = "sys.stdout.writeLine";
    bindings[0].handler = native_syscall_stdout_write_line;
    bindings[1].target = "io.print";
    bindings[1].handler = native_syscall_stdout_write_line;
    bindings[2].target = "io.write";
    bindings[2].handler = native_syscall_stdout_write_line;
    bindings[3].target = "sys.process.args";
    bindings[3].handler = native_syscall_process_argv;
    bindings[4].target = "sys.bytes.length";
    bindings[4].handler = native_syscall_bytes_length;
    bindings[5].target = "sys.bytes.at";
    bindings[5].handler = native_syscall_bytes_at;
    bindings[6].target = "sys.bytes.slice";
    bindings[6].handler = native_syscall_bytes_slice;
    bindings[7].target = "sys.bytes.concat";
    bindings[7].handler = native_syscall_bytes_concat;
    bindings[8].target = "sys.bytes.fromBase64";
    bindings[8].handler = native_syscall_bytes_from_base64;
    bindings[9].target = "sys.bytes.toBase64";
    bindings[9].handler = native_syscall_bytes_to_base64;
    bindings[10].target = "sys.fs.file.delete";
    bindings[10].handler = native_syscall_fs_file_delete;
    bindings[11].target = "sys.fs.dir.delete";
    bindings[11].handler = native_syscall_fs_dir_delete;
    bindings[12].target = "sys.process.spawn";
    bindings[12].handler = native_syscall_process_spawn;
    bindings[13].target = "sys.process.wait";
    bindings[13].handler = native_syscall_process_wait;
    bindings[14].target = "sys.process.kill";
    bindings[14].handler = native_syscall_process_kill;
    bindings[15].target = "sys.process.stdout.read";
    bindings[15].handler = native_syscall_process_stdout_read;
    bindings[16].target = "sys.process.stderr.read";
    bindings[16].handler = native_syscall_process_stderr_read;
    bindings[17].target = "sys.process.poll";
    bindings[17].handler = native_syscall_process_poll;
    bindings[18].target = "sys.str.fromCodePoint";
    bindings[18].handler = native_syscall_str_from_codepoint;
    bindings[19].target = "sys.str.decodeUnicodeHex4";
    bindings[19].handler = native_syscall_str_decode_unicode_hex4;
    bindings[20].target = "sys.str.decodeUnicodeSurrogatePairHex4";
    bindings[20].handler = native_syscall_str_decode_unicode_surrogate_pair_hex4;
    bindings[21].target = "sys.bytes.toUtf8String";
    bindings[21].handler = native_syscall_bytes_to_utf8_string;
    result = aivm_c_execute_program_with_syscalls_and_argv(
        program,
        bindings,
        22U,
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
            if (strcmp(target, "io.print") == 0 || strcmp(target, "io.write") == 0 || strcmp(target, "sys.stdout.writeLine") == 0) {
                mapped = "sys.stdout.writeLine";
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
        } else if (v.type == AIVM_VAL_BYTES) {
            if (v.bytes_value.length > 0xffffffffU) {
                return 0;
            }
            const_payload_size += 1U + 4U + (uint32_t)v.bytes_value.length;
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
            } else if (v.type == AIVM_VAL_BYTES) {
                uint32_t len = (uint32_t)v.bytes_value.length;
                (void)fputc(5, f);
                write_u32_le(f, len);
                if (len > 0U && v.bytes_value.data != NULL) {
                    (void)fwrite(v.bytes_value.data, 1U, len, f);
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

static int parse_positive_int_text(const char* text, int* out_value)
{
    char* end = NULL;
    long value;
    if (text == NULL || out_value == NULL) {
        return 0;
    }
    value = strtol(text, &end, 10);
    if (end == text || *end != '\0' || value <= 0L || value > 100000000L) {
        return 0;
    }
    *out_value = (int)value;
    return 1;
}

static int handle_debug_mem_or_profile(int argc, char** argv, int start_index, const char* mode_name)
{
    const char* target_path = ".";
    int target_explicit = 0;
    const char* out_path = NULL;
    int iterations = 20;
    int max_growth_kb = 2048;
    int i;
    int run_failures = 0;
    int unavailable = 0;
    int64_t first_rss = 0;
    int64_t last_rss = 0;
    int64_t peak_rss = 0;
    int64_t growth_kb = 0;
    int64_t duration_total_ns = 0;
    int64_t duration_peak_ns = 0;
    int64_t rss_series[512];
    int exit_codes[512];
    int64_t duration_series_ns[512];
    int recorded = 0;
    FILE* out_file = NULL;
    int close_file = 0;

    if (mode_name == NULL) {
        mode_name = "mem";
    }

    for (i = start_index; i < argc; i += 1) {
        const char* arg = argv[i];
        if (strcmp(arg, "--iterations") == 0) {
            if (i + 1 >= argc || !parse_positive_int_text(argv[i + 1], &iterations)) {
                fprintf(stderr, "Err#err1(code=RUN001 message=\"Invalid --iterations value.\" nodeId=argv)\n");
                return 2;
            }
            i += 1;
            continue;
        }
        if (strcmp(arg, "--max-growth-kb") == 0) {
            if (i + 1 >= argc || !parse_positive_int_text(argv[i + 1], &max_growth_kb)) {
                fprintf(stderr, "Err#err1(code=RUN001 message=\"Invalid --max-growth-kb value.\" nodeId=argv)\n");
                return 2;
            }
            i += 1;
            continue;
        }
        if (strcmp(arg, "--out") == 0) {
            if (i + 1 >= argc || argv[i + 1][0] == '\0') {
                fprintf(stderr, "Err#err1(code=RUN001 message=\"Missing --out value.\" nodeId=argv)\n");
                return 2;
            }
            out_path = argv[i + 1];
            i += 1;
            continue;
        }
        if (starts_with(arg, "--vm=")) {
            const char* vm_mode = arg + 5;
            if (strcmp(vm_mode, "c") != 0 && !is_reserved_cv_selector(vm_mode)) {
                return print_unsupported_vm_mode(vm_mode);
            }
            continue;
        }
        if (arg[0] == '-') {
            fprintf(stderr, "Err#err1(code=RUN001 message=\"Unsupported debug flag.\" nodeId=argv)\n");
            return 2;
        }
        if (target_explicit) {
            fprintf(stderr, "Err#err1(code=RUN001 message=\"Too many positional arguments for debug audit.\" nodeId=argv)\n");
            return 2;
        }
        target_path = arg;
        target_explicit = 1;
    }

    if (iterations > (int)(sizeof(rss_series) / sizeof(rss_series[0]))) {
        fprintf(stderr, "Err#err1(code=RUN001 message=\"--iterations too large for debug audit.\" nodeId=argv)\n");
        return 2;
    }

    for (i = 0; i < iterations; i += 1) {
        int rc;
        int64_t t0_ns = 0;
        int64_t t1_ns = 0;
        int64_t rss_kb = 0;
        int64_t elapsed_ns = 0;

        if (!monotonic_time_ns(&t0_ns)) {
            t0_ns = 0;
        }
        rc = run_via_resolved_input(target_path, NULL, 0U);
        if (!monotonic_time_ns(&t1_ns)) {
            t1_ns = t0_ns;
        }
        if (t1_ns > t0_ns) {
            elapsed_ns = t1_ns - t0_ns;
        }
        if (!sample_process_rss_kb(&rss_kb) || rss_kb <= 0) {
            unavailable = 1;
            exit_codes[recorded] = rc;
            duration_series_ns[recorded] = elapsed_ns;
            rss_series[recorded] = 0;
            recorded += 1;
            break;
        }

        if (rc != 0) {
            run_failures += 1;
        }

        if (recorded == 0) {
            first_rss = rss_kb;
            peak_rss = rss_kb;
        }
        last_rss = rss_kb;
        if (rss_kb > peak_rss) {
            peak_rss = rss_kb;
        }
        duration_total_ns += elapsed_ns;
        if (elapsed_ns > duration_peak_ns) {
            duration_peak_ns = elapsed_ns;
        }

        rss_series[recorded] = rss_kb;
        exit_codes[recorded] = rc;
        duration_series_ns[recorded] = elapsed_ns;
        recorded += 1;
    }

    if (recorded > 0) {
        growth_kb = last_rss - first_rss;
    }

    if (out_path != NULL) {
        out_file = fopen(out_path, "wb");
        if (out_file == NULL) {
            fprintf(stderr, "Err#err1(code=RUN001 message=\"Failed to write debug audit output.\" nodeId=debug)\n");
            return 2;
        }
        close_file = 1;
    } else {
        out_file = stdout;
    }

    (void)fprintf(out_file, "format = \"aivm_debug_mem_v1\"\n");
    (void)fprintf(out_file, "mode = \"");
    write_toml_escaped(out_file, mode_name);
    (void)fprintf(out_file, "\"\n");
    (void)fprintf(out_file, "target = \"");
    write_toml_escaped(out_file, target_path);
    (void)fprintf(out_file, "\"\n");
    (void)fprintf(out_file, "iterations_requested = %d\n", iterations);
    (void)fprintf(out_file, "iterations_recorded = %d\n", recorded);
    (void)fprintf(out_file, "max_growth_kb = %d\n", max_growth_kb);
    (void)fprintf(out_file, "first_rss_kb = %lld\n", (long long)first_rss);
    (void)fprintf(out_file, "last_rss_kb = %lld\n", (long long)last_rss);
    (void)fprintf(out_file, "peak_rss_kb = %lld\n", (long long)peak_rss);
    (void)fprintf(out_file, "growth_kb = %lld\n", (long long)growth_kb);
    (void)fprintf(out_file, "duration_total_ns = %lld\n", (long long)duration_total_ns);
    (void)fprintf(out_file, "duration_peak_ns = %lld\n", (long long)duration_peak_ns);
    if (unavailable) {
        (void)fprintf(out_file, "status = \"unavailable\"\n");
    } else if (run_failures > 0) {
        (void)fprintf(out_file, "status = \"fail\"\n");
    } else if (growth_kb > (int64_t)max_growth_kb) {
        (void)fprintf(out_file, "status = \"fail\"\n");
    } else {
        (void)fprintf(out_file, "status = \"pass\"\n");
    }
    (void)fprintf(out_file, "run_failures = %d\n", run_failures);
    (void)fprintf(out_file, "rss_series_kb = [");
    for (i = 0; i < recorded; i += 1) {
        if (i > 0) {
            (void)fprintf(out_file, ", ");
        }
        (void)fprintf(out_file, "%lld", (long long)rss_series[i]);
    }
    (void)fprintf(out_file, "]\n");
    (void)fprintf(out_file, "exit_codes = [");
    for (i = 0; i < recorded; i += 1) {
        if (i > 0) {
            (void)fprintf(out_file, ", ");
        }
        (void)fprintf(out_file, "%d", exit_codes[i]);
    }
    (void)fprintf(out_file, "]\n");
    (void)fprintf(out_file, "duration_series_ns = [");
    for (i = 0; i < recorded; i += 1) {
        if (i > 0) {
            (void)fprintf(out_file, ", ");
        }
        (void)fprintf(out_file, "%lld", (long long)duration_series_ns[i]);
    }
    (void)fprintf(out_file, "]\n");

    if (close_file) {
        (void)fclose(out_file);
        printf("Ok#ok1(type=string value=\"%s\")\n", out_path);
    }

    if (unavailable) {
        return 3;
    }
    if (run_failures > 0 || growth_kb > (int64_t)max_growth_kb) {
        return 1;
    }
    return 0;
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
    if (argc >= 3 && strcmp(argv[2], "mem") == 0) {
        return handle_debug_mem_or_profile(argc, argv, 3, "mem");
    }
    if (argc >= 3 && strcmp(argv[2], "profile") == 0) {
        return handle_debug_mem_or_profile(argc, argv, 3, "profile");
    }
    fprintf(stderr,
        "Err#err1(code=DEV008 message=\"Native debug supports: debug run|mem|profile\" nodeId=command)\n");
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

    bindings[0].target = "sys.stdout.writeLine";
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
    char resolved_program[PATH_MAX];
    char source_aos[PATH_MAX];
    char artifact_dir[PATH_MAX];
    char runtime_bin[64];
    char publish_app_name[128];
    char publish_runtime_name[160];
    char runtime_src[PATH_MAX];
    char runtime_dst[PATH_MAX];
    char app_dst[PATH_MAX];
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
    if (!derive_publish_app_name(program_input, publish_app_name, sizeof(publish_app_name))) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to derive publish app name.\" nodeId=publish)\n");
        return 2;
    }
    if (strstr(runtime_bin, ".exe") != NULL) {
        if (snprintf(publish_runtime_name, sizeof(publish_runtime_name), "%s.exe", publish_app_name) >= (int)sizeof(publish_runtime_name)) {
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
