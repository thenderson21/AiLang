#include <errno.h>
#include <ctype.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <time.h>

#ifdef _WIN32
#include <direct.h>
#include <io.h>
#include <process.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <sys/stat.h>
#include <windows.h>
#ifndef PATH_MAX
#define PATH_MAX 260
#endif
#define AIVM_PATH_SEP '\\'
#define AIVM_EXE_EXT ".exe"
#else
#include <arpa/inet.h>
#include <dirent.h>
#include <fcntl.h>
#include <netinet/in.h>
#include <signal.h>
#include <sys/select.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/time.h>
#include <sys/types.h>
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
#include "remote/aivm_remote_channel.h"
#include "remote/aivm_remote_session.h"
#include "aivm_runtime.h"
#include "airun_ui_host.h"

#ifndef AIRUN_UI_HOST_EXTERNAL
#ifndef AIRUN_ALLOW_INTERNAL_UI_FALLBACK
#error "Internal UI fallback backend is disabled by default. Define AIRUN_UI_HOST_EXTERNAL for production builds or AIRUN_ALLOW_INTERNAL_UI_FALLBACK for embedded tests."
#endif
#include "airun_ui_host_unavailable.c"
#endif

#if defined(AIRUN_MINIMAL_RUNTIME)
#if defined(_MSC_VER)
#define AIRUN_MAYBE_UNUSED
#else
#define AIRUN_MAYBE_UNUSED __attribute__((unused))
#endif
#else
#define AIRUN_MAYBE_UNUSED
#endif

static int join_path(const char* left, const char* right, char* out, size_t out_len);
static int find_executable_on_path(const char* name, char* out, size_t out_len);
static int write_text_file(const char* path, const char* text);
static int remove_file_if_exists(const char* path);
static int run_native_fullstack_server(const char* www_dir);

#ifdef _WIN32
typedef SOCKET NativeSocket;
#define NATIVE_INVALID_SOCKET INVALID_SOCKET
#define native_socket_close closesocket
#else
typedef int NativeSocket;
#define NATIVE_INVALID_SOCKET (-1)
#define native_socket_close close
#endif
static int resolve_executable_path(const char* argv0, char* out, size_t out_len);
static int dirname_of(const char* path, char* out, size_t out_len);
static int simple_resolve_path(const char* base_file, const char* import_path, char* out_path, size_t out_path_len);
static int simple_fail(const char* message);
static int simple_failf(const char* fmt, ...);
static const char* native_build_error(void);
static AivmVm* g_native_active_vm;
static int native_vm_append_host_node(
    AivmVm* vm,
    const char* kind,
    const char* id,
    const AivmNodeAttr* attrs,
    size_t attr_count,
    int64_t* out_handle);
static int native_bytes_to_base64(
    const uint8_t* input,
    size_t in_len,
    char* output,
    size_t output_capacity);
static int native_bytes_from_base64(
    const char* input,
    uint8_t* output,
    size_t output_capacity,
    size_t* out_len);

#define AIRUN_NATIVE_CACHE_SCHEMA "airun-native-cache-v2"
#define AIRUN_NATIVE_COMPILER_FINGERPRINT "native-compiler-2026-03-05-call-fixup-order-v2"

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

static int ensure_directory_recursive(const char* path)
{
    char tmp[PATH_MAX];
    size_t len;
    size_t i;
    if (path == NULL) {
        return 0;
    }
    len = strlen(path);
    if (len == 0U || len >= sizeof(tmp)) {
        return 0;
    }
    memcpy(tmp, path, len + 1U);
    for (i = 1U; i < len; i += 1U) {
        if (tmp[i] == '/' || tmp[i] == '\\') {
            char saved = tmp[i];
            tmp[i] = '\0';
            if (tmp[0] != '\0' && !ensure_directory(tmp)) {
                tmp[i] = saved;
                return 0;
            }
            tmp[i] = saved;
        }
    }
    return ensure_directory(tmp);
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

static int remove_file_if_exists(const char* path)
{
    if (path == NULL || path[0] == '\0') {
        return 0;
    }
    if (!file_exists(path)) {
        return 1;
    }
    return remove(path) == 0;
}

static volatile int g_fullstack_stop = 0;

#ifdef _WIN32
static BOOL WINAPI fullstack_ctrl_handler(DWORD ctrl_type)
{
    if (ctrl_type == CTRL_C_EVENT || ctrl_type == CTRL_BREAK_EVENT || ctrl_type == CTRL_CLOSE_EVENT) {
        g_fullstack_stop = 1;
        return TRUE;
    }
    return FALSE;
}
#else
static void fullstack_signal_handler(int signo)
{
    (void)signo;
    g_fullstack_stop = 1;
}
#endif

static int native_send_all(NativeSocket fd, const char* bytes, size_t length)
{
    size_t sent = 0U;
    while (sent < length) {
#ifdef _WIN32
        int n = send(fd, bytes + sent, (int)(length - sent), 0);
#else
        ssize_t n = send(fd, bytes + sent, length - sent, 0);
#endif
        if (n <= 0) {
            return 0;
        }
        sent += (size_t)n;
    }
    return 1;
}

static const char* fullstack_mime_type(const char* path)
{
    if (path == NULL) {
        return "application/octet-stream";
    }
    if (ends_with(path, ".html")) {
        return "text/html; charset=utf-8";
    }
    if (ends_with(path, ".js") || ends_with(path, ".mjs")) {
        return "text/javascript; charset=utf-8";
    }
    if (ends_with(path, ".wasm")) {
        return "application/wasm";
    }
    if (ends_with(path, ".aibc1")) {
        return "application/octet-stream";
    }
    return "application/octet-stream";
}

static int fullstack_send_error(NativeSocket client, int code, const char* message)
{
    char body[256];
    char header[512];
    int body_len;
    int header_len;
    if (message == NULL) {
        message = "error";
    }
    body_len = snprintf(body, sizeof(body), "%d %s\n", code, message);
    if (body_len < 0 || (size_t)body_len >= sizeof(body)) {
        return 0;
    }
    header_len = snprintf(
        header,
        sizeof(header),
        "HTTP/1.1 %d %s\r\n"
        "Content-Type: text/plain; charset=utf-8\r\n"
        "Content-Length: %d\r\n"
        "Connection: close\r\n\r\n",
        code,
        message,
        body_len);
    if (header_len < 0 || (size_t)header_len >= sizeof(header)) {
        return 0;
    }
    return native_send_all(client, header, (size_t)header_len) &&
           native_send_all(client, body, (size_t)body_len);
}

static int fullstack_serve_request(NativeSocket client, const char* www_dir)
{
    char request[4096];
    char method[16];
    char target[1024];
    char file_path[PATH_MAX];
    char relative[1024];
    char header[512];
    struct stat st;
    FILE* file;
    size_t n;
    int request_len;
    int i;
    const char* mime;
    int header_len;
    char buffer[4096];

    if (www_dir == NULL) {
        return fullstack_send_error(client, 500, "server error");
    }

#ifdef _WIN32
    request_len = recv(client, request, (int)(sizeof(request) - 1U), 0);
#else
    request_len = (int)recv(client, request, sizeof(request) - 1U, 0);
#endif
    if (request_len <= 0) {
        return 0;
    }
    request[request_len] = '\0';

    if (sscanf(request, "%15s %1023s", method, target) != 2) {
        return fullstack_send_error(client, 400, "bad request");
    }
    if (strcmp(method, "GET") != 0) {
        return fullstack_send_error(client, 405, "method not allowed");
    }
    for (i = 0; target[i] != '\0'; i += 1) {
        if (target[i] == '?') {
            target[i] = '\0';
            break;
        }
    }
    if (strstr(target, "..") != NULL) {
        return fullstack_send_error(client, 403, "forbidden");
    }
    if (strcmp(target, "/") == 0) {
        if (snprintf(relative, sizeof(relative), "index.html") >= (int)sizeof(relative)) {
            return fullstack_send_error(client, 500, "path overflow");
        }
    } else {
        const char* rel = target;
        if (target[0] == '/') {
            rel = target + 1;
        }
        if (snprintf(relative, sizeof(relative), "%s", rel) >= (int)sizeof(relative)) {
            return fullstack_send_error(client, 500, "path overflow");
        }
    }
    if (!join_path(www_dir, relative, file_path, sizeof(file_path))) {
        return fullstack_send_error(client, 500, "path overflow");
    }
    if (stat(file_path, &st) != 0) {
        return fullstack_send_error(client, 404, "not found");
    }
#ifdef _WIN32
    if ((st.st_mode & _S_IFREG) == 0) {
#else
    if (!S_ISREG(st.st_mode)) {
#endif
        return fullstack_send_error(client, 404, "not found");
    }

    file = fopen(file_path, "rb");
    if (file == NULL) {
        return fullstack_send_error(client, 404, "not found");
    }
    mime = fullstack_mime_type(file_path);
    header_len = snprintf(
        header,
        sizeof(header),
        "HTTP/1.1 200 OK\r\n"
        "Content-Type: %s\r\n"
        "Content-Length: %lld\r\n"
        "Connection: close\r\n\r\n",
        mime,
        (long long)st.st_size);
    if (header_len < 0 || (size_t)header_len >= sizeof(header)) {
        fclose(file);
        return fullstack_send_error(client, 500, "header overflow");
    }
    if (!native_send_all(client, header, (size_t)header_len)) {
        fclose(file);
        return 0;
    }
    while ((n = fread(buffer, 1U, sizeof(buffer), file)) > 0U) {
        if (!native_send_all(client, buffer, n)) {
            fclose(file);
            return 0;
        }
    }
    fclose(file);
    return 1;
}

static int run_native_fullstack_server(const char* www_dir)
{
    NativeSocket listener = NATIVE_INVALID_SOCKET;
    struct sockaddr_in addr;
    const char* port_env;
    int port = 8080;
    int reuse = 1;

    if (www_dir == NULL) {
        return 2;
    }
    port_env = getenv("PORT");
    if (port_env != NULL && port_env[0] != '\0') {
        int parsed = atoi(port_env);
        if (parsed > 0 && parsed <= 65535) {
            port = parsed;
        }
    }

#ifdef _WIN32
    {
        WSADATA wsa_data;
        if (WSAStartup(MAKEWORD(2, 2), &wsa_data) != 0) {
            fprintf(stderr, "Err#err1(code=RUN001 message=\"WSAStartup failed.\" nodeId=publish)\n");
            return 2;
        }
    }
    SetConsoleCtrlHandler(fullstack_ctrl_handler, TRUE);
#else
    signal(SIGINT, fullstack_signal_handler);
    signal(SIGTERM, fullstack_signal_handler);
#endif

    listener = socket(AF_INET, SOCK_STREAM, 0);
    if (listener == NATIVE_INVALID_SOCKET) {
        fprintf(stderr, "Err#err1(code=RUN001 message=\"Failed to create fullstack listener socket.\" nodeId=publish)\n");
#ifdef _WIN32
        WSACleanup();
#endif
        return 2;
    }
    (void)setsockopt(listener, SOL_SOCKET, SO_REUSEADDR, (const char*)&reuse, sizeof(reuse));
    memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    addr.sin_port = htons((uint16_t)port);
    if (bind(listener, (struct sockaddr*)&addr, sizeof(addr)) != 0 || listen(listener, 16) != 0) {
        fprintf(stderr, "Err#err1(code=RUN001 message=\"Failed to bind/listen fullstack server socket.\" nodeId=publish)\n");
        native_socket_close(listener);
#ifdef _WIN32
        WSACleanup();
#endif
        return 2;
    }

    printf("[fullstack] serving static client from %s at http://localhost:%d\n", www_dir, port);
    printf("[fullstack] press Ctrl+C to stop\n");
    fflush(stdout);

    g_fullstack_stop = 0;
    while (!g_fullstack_stop) {
        fd_set read_fds;
        struct timeval timeout;
        int ready;
        NativeSocket client;
        FD_ZERO(&read_fds);
        FD_SET(listener, &read_fds);
        timeout.tv_sec = 0;
        timeout.tv_usec = 250000;
#ifdef _WIN32
        ready = select(0, &read_fds, NULL, NULL, &timeout);
#else
        ready = select((int)listener + 1, &read_fds, NULL, NULL, &timeout);
#endif
        if (ready < 0) {
            if (g_fullstack_stop) {
                break;
            }
            continue;
        }
        if (ready == 0) {
            continue;
        }
        client = accept(listener, NULL, NULL);
        if (client == NATIVE_INVALID_SOCKET) {
            continue;
        }
        (void)fullstack_serve_request(client, www_dir);
        native_socket_close(client);
    }

    native_socket_close(listener);
#ifdef _WIN32
    WSACleanup();
#endif
    return 0;
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

static int write_text_file(const char* path, const char* text)
{
    FILE* f;
    size_t len;
    if (path == NULL || text == NULL) {
        return 0;
    }
    f = fopen(path, "wb");
    if (f == NULL) {
        return 0;
    }
    len = strlen(text);
    if (len > 0U && fwrite(text, 1U, len, f) != len) {
        fclose(f);
        return 0;
    }
    fclose(f);
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
    char full[PATH_MAX];
    DWORD found;
    if (name == NULL || out == NULL || out_len == 0U) {
        return 0;
    }
    found = SearchPathA(NULL, name, NULL, (DWORD)sizeof(full), full, NULL);
    if (found == 0U || found >= (DWORD)sizeof(full)) {
        return 0;
    }
    return snprintf(out, out_len, "%s", full) < (int)out_len;
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
    char project_app_aibc1[PATH_MAX];
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

    if (join_path(project_dir, "app.aibc1", project_app_aibc1, sizeof(project_app_aibc1)) &&
        file_exists(project_app_aibc1)) {
        return snprintf(out_path, out_len, "%s", project_app_aibc1) < (int)out_len;
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

static int resolve_publish_wasm_fullstack_host_target_from_manifest(const char* program_input, char* out_target, size_t out_len)
{
    char manifest_path[PATH_MAX];
    char manifest_text[8192];
    if (program_input == NULL || out_target == NULL || out_len == 0U) {
        return 0;
    }
    if (!resolve_manifest_path_for_input(program_input, manifest_path, sizeof(manifest_path))) {
        return 0;
    }
    if (!read_text_file(manifest_path, manifest_text, sizeof(manifest_text))) {
        return 0;
    }
    if (!extract_manifest_attr(manifest_text, "publishWasmFullstackHostTarget", out_target, out_len)) {
        return 0;
    }
    return 1;
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
        n = snprintf(out_bin, out_bin_len, "aivm-runtime");
    } else if (starts_with(rid, "linux-")) {
        platform = "linux";
        arch = rid + 6;
        if (strcmp(arch, "x64") != 0 && strcmp(arch, "arm64") != 0) {
            return 0;
        }
        n = snprintf(out_bin, out_bin_len, "aivm-runtime");
    } else if (starts_with(rid, "windows-")) {
        platform = "windows";
        arch = rid + 8;
        if (strcmp(arch, "x64") != 0 && strcmp(arch, "arm64") != 0) {
            return 0;
        }
        n = snprintf(out_bin, out_bin_len, "aivm-runtime.exe");
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
#ifdef AIRUN_MINIMAL_RUNTIME
    fprintf(stderr,
        "Usage: aivm-runtime <command> [options]\n"
        "\n"
        "Commands:\n"
        "  run <program(.aibc1|.aos|project-dir|project.aiproj)> [--vm=<selector>] [--no-cache]\n"
        "  version | --version\n"
        "\n"
        "VM selectors:\n"
        "  c      current stable C VM (default)\n"
        "  cvN    reserved future C VM profile/version selector (currently maps to c)\n");
#else
    fprintf(stderr,
        "Usage: airun <command> [options]\n"
        "\n"
        "Commands:\n"
        "  run <program(.aibc1|.aos|project-dir|project.aiproj)> [--vm=<selector>] [--no-cache]\n"
        "  build <program(.aibc1|.aos|project-dir|project.aiproj)> [--out <dir>] [--no-cache]\n"
        "  clean [program(.aibc1|.aos|project-dir|project.aiproj)]\n"
        "  repl\n"
        "  bench [--iterations <n>] [--human]\n"
        "  debug run <program(.aibc1|.aos|project-dir|project.aiproj)> [--vm=<selector>] [--out <dir>]\n"
        "  publish <program(.aibc1|.aos|project-dir|project.aiproj)> [--target <rid>] [--wasm-profile <cli|spa|fullstack>] [--wasm-fullstack-host-target <rid>] [--out <dir>]\n"
        "  version | --version\n"
        "\n"
        "VM selectors:\n"
        "  c      current stable C VM (default)\n"
        "  cvN    reserved future C VM profile/version selector (currently maps to c)\n");
#endif
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

static int wasm_profile_is_valid(const char* profile)
{
    if (profile == NULL) {
        return 0;
    }
    return strcmp(profile, "cli") == 0 ||
           strcmp(profile, "spa") == 0 ||
           strcmp(profile, "web") == 0 ||
           strcmp(profile, "fullstack") == 0;
}

static const char* wasm_profile_normalize(const char* profile)
{
    if (profile == NULL || strcmp(profile, "web") == 0) {
        return "spa";
    }
    return profile;
}

static int wasm_syscall_unavailable_for_profile(const char* profile, const char* target)
{
    if (profile == NULL || target == NULL) {
        return 0;
    }
    if (strcmp(profile, "cli") == 0) {
        return strcmp(target, "sys.process.spawn") == 0 ||
               strcmp(target, "sys.process.wait") == 0 ||
               strcmp(target, "sys.process.kill") == 0 ||
               strcmp(target, "sys.process.stdout.read") == 0 ||
               strcmp(target, "sys.process.stderr.read") == 0 ||
               strcmp(target, "sys.process.poll") == 0 ||
               strncmp(target, "sys.ui.", 7U) == 0 ||
               strncmp(target, "sys.ui_", 7U) == 0;
    }
    if (strcmp(profile, "spa") == 0 || strcmp(profile, "fullstack") == 0) {
        return strcmp(target, "sys.process.spawn") == 0 ||
               strcmp(target, "sys.process.wait") == 0 ||
               strcmp(target, "sys.process.kill") == 0 ||
               strcmp(target, "sys.process.stdout.read") == 0 ||
               strcmp(target, "sys.process.stderr.read") == 0 ||
               strcmp(target, "sys.process.poll") == 0 ||
               strncmp(target, "sys.net.", 8U) == 0 ||
               strncmp(target, "sys.fs.", 7U) == 0;
    }
    return 0;
}

static int syscall_target_already_reported(const AivmProgram* program, size_t index, const char* target)
{
    size_t j;
    if (program == NULL || target == NULL) {
        return 0;
    }
    for (j = 0U; j < index; j += 1U) {
        const char* prior_target;
        if (program->constants[j].type != AIVM_VAL_STRING || program->constants[j].string_value == NULL) {
            continue;
        }
        prior_target = program->constants[j].string_value;
        if (strncmp(prior_target, "sys.", 4U) != 0) {
            continue;
        }
        if (strcmp(prior_target, target) == 0) {
            return 1;
        }
    }
    return 0;
}

static void emit_wasm_profile_warnings(const char* profile, const AivmProgram* program)
{
    size_t i;
    if (profile == NULL || program == NULL) {
        return;
    }
    for (i = 0U; i < program->constant_count; i += 1U) {
        const char* target = NULL;
        if (program->constants[i].type != AIVM_VAL_STRING || program->constants[i].string_value == NULL) {
            continue;
        }
        target = program->constants[i].string_value;
        if (strncmp(target, "sys.", 4U) != 0) {
            continue;
        }
        if (!wasm_syscall_unavailable_for_profile(profile, target)) {
            continue;
        }
        if (!syscall_target_already_reported(program, i, target)) {
            fprintf(
                stderr,
                "Warn#warn1(code=WASM001 message=\"%s is not available on wasm profile '%s'; runtime will raise RUN101 if executed.\" nodeId=publish)\n",
                target,
                profile);
        }
    }
}

static int native_syscall_requires_host_ui_capability(const char* syscall_target)
{
    if (syscall_target == NULL) {
        return 0;
    }
    return strncmp(syscall_target, "sys.ui.", 7U) == 0 ||
           strncmp(syscall_target, "sys.ui_", 7U) == 0;
}

static int native_syscall_requires_host_process_capability(const char* syscall_target)
{
    if (syscall_target == NULL) {
        return 0;
    }
    return strcmp(syscall_target, "sys.process.spawn") == 0 ||
           strcmp(syscall_target, "sys.process.wait") == 0 ||
           strcmp(syscall_target, "sys.process.kill") == 0 ||
           strcmp(syscall_target, "sys.process.stdout.read") == 0 ||
           strcmp(syscall_target, "sys.process.stderr.read") == 0 ||
           strcmp(syscall_target, "sys.process.poll") == 0;
}

static int native_syscall_requires_host_fs_capability(const char* syscall_target)
{
    if (syscall_target == NULL) {
        return 0;
    }
    return strncmp(syscall_target, "sys.fs.", 7U) == 0 ||
           strncmp(syscall_target, "sys.fs_", 7U) == 0;
}

static int native_syscall_requires_host_net_capability(const char* syscall_target)
{
    if (syscall_target == NULL) {
        return 0;
    }
    return strncmp(syscall_target, "sys.net.", 8U) == 0 ||
           strncmp(syscall_target, "sys.net_", 8U) == 0;
}

static void emit_native_target_capability_warnings(const char* target_rid, const AivmProgram* program)
{
    size_t i;
    if (target_rid == NULL || program == NULL) {
        return;
    }
    for (i = 0U; i < program->constant_count; i += 1U) {
        const char* target = NULL;
        if (program->constants[i].type != AIVM_VAL_STRING || program->constants[i].string_value == NULL) {
            continue;
        }
        target = program->constants[i].string_value;
        if (syscall_target_already_reported(program, i, target)) {
            continue;
        }
        if (native_syscall_requires_host_ui_capability(target)) {
            fprintf(
                stderr,
                "Warn#warn1(code=CAP001 message=\"%s requires host UI capability on target '%s'; runtime will raise AIVMS001 if unavailable.\" nodeId=publish)\n",
                target,
                target_rid);
            continue;
        }
        if (native_syscall_requires_host_process_capability(target)) {
            fprintf(
                stderr,
                "Warn#warn1(code=CAP002 message=\"%s requires host process capability on target '%s'; runtime will raise AIVMS001 if unavailable.\" nodeId=publish)\n",
                target,
                target_rid);
            continue;
        }
        if (native_syscall_requires_host_fs_capability(target)) {
            fprintf(
                stderr,
                "Warn#warn1(code=CAP003 message=\"%s requires host filesystem capability on target '%s'; runtime will raise AIVMS001 if unavailable.\" nodeId=publish)\n",
                target,
                target_rid);
            continue;
        }
        if (native_syscall_requires_host_net_capability(target)) {
            fprintf(
                stderr,
                "Warn#warn1(code=CAP004 message=\"%s requires host network capability on target '%s'; runtime will raise AIVMS001 if unavailable.\" nodeId=publish)\n",
                target,
                target_rid);
        }
    }
}

static int emit_wasm_cli_launchers(const char* out_dir, const char* runtime_name, const char* app_name)
{
    char run_sh_path[PATH_MAX];
    char run_ps1_path[PATH_MAX];
    char run_sh[1024];
    char run_ps1[1024];
    if (out_dir == NULL || runtime_name == NULL || app_name == NULL) {
        return 0;
    }
    if (!join_path(out_dir, "run.sh", run_sh_path, sizeof(run_sh_path)) ||
        !join_path(out_dir, "run.ps1", run_ps1_path, sizeof(run_ps1_path))) {
        return 0;
    }
    if (snprintf(
            run_sh,
            sizeof(run_sh),
            "#!/usr/bin/env bash\nset -euo pipefail\nDIR=\"$(cd \"$(dirname \"${BASH_SOURCE[0]}\")\" && pwd)\"\nexec wasmtime run -C cache=n \"${DIR}/%s\" - < \"${DIR}/app.aibc1\"\n",
            runtime_name) >= (int)sizeof(run_sh)) {
        return 0;
    }
    if (snprintf(
            run_ps1,
            sizeof(run_ps1),
            "$ErrorActionPreference = 'Stop'\n$dir = Split-Path -Parent $MyInvocation.MyCommand.Path\nwasmtime run -C cache=n \"$dir/%s\" - < \"$dir/app.aibc1\"\n",
            runtime_name) >= (int)sizeof(run_ps1)) {
        return 0;
    }
    if (!write_text_file(run_sh_path, run_sh) || !write_text_file(run_ps1_path, run_ps1)) {
        return 0;
    }
#ifndef _WIN32
    if (chmod(run_sh_path, 0755) != 0) {
        return 0;
    }
#endif
    (void)app_name;
    return 1;
}

static int emit_wasm_spa_files(const char* out_dir)
{
    char index_path[PATH_MAX];
    char main_path[PATH_MAX];
    char remote_client_path[PATH_MAX];
    char index_html[2048];
    char main_js[32768];
    char remote_client_js[8192];
    if (out_dir == NULL) {
        return 0;
    }
    if (!join_path(out_dir, "index.html", index_path, sizeof(index_path)) ||
        !join_path(out_dir, "main.js", main_path, sizeof(main_path)) ||
        !join_path(out_dir, "remote-client.js", remote_client_path, sizeof(remote_client_path))) {
        return 0;
    }
    if (snprintf(
            index_html,
            sizeof(index_html),
            "<!doctype html>\n<html lang=\"en\">\n<head><meta charset=\"utf-8\"><title>AiLang wasm app</title></head>\n<body>\n  <pre id=\"output\"></pre>\n  <script type=\"module\" src=\"./main.js\"></script>\n</body>\n</html>\n") >= (int)sizeof(index_html)) {
        return 0;
    }
    {
        const char* main_js_head =
            "import createRuntime from './aivm-runtime-wasm32-web.mjs';\n"
            "import { createAivmRemoteClient } from './remote-client.js';\n"
            "const output = document.getElementById('output');\n"
            "const endpoint = globalThis.AIVM_REMOTE_WS_ENDPOINT || (`ws://${location.hostname}:8765`);\n"
            "const aiLangRoot = globalThis.AiLang || (globalThis.AiLang = {});\n"
            "const remoteMode = globalThis.AIVM_REMOTE_MODE || 'ws';\n"
            "if (remoteMode !== 'ws' && remoteMode !== 'js') {\n"
            "  throw new Error(`RUN101: unsupported AIVM_REMOTE_MODE '${remoteMode}' (expected 'ws' or 'js')`);\n"
            "}\n"
            "const stdinQueue = [];\n"
            "let stdinClosed = false;\n"
            "const uiState = { windows: new Map() };\n"
            "function xmlEscape(text) {\n"
            "  return String(text ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('\"', '&quot;').replaceAll(\"'\", '&apos;');\n"
            "}\n";
        const char* main_js_head2 =
            "function uiEventModifiers(ev) {\n"
            "  const mods = [];\n"
            "  if (ev?.altKey) mods.push('alt');\n"
            "  if (ev?.ctrlKey) mods.push('ctrl');\n"
            "  if (ev?.metaKey) mods.push('meta');\n"
            "  if (ev?.shiftKey) mods.push('shift');\n"
            "  return mods.join(',');\n"
            "}\n"
            "function uiEventKey(key) {\n"
            "  const raw = String(key ?? '');\n"
            "  if (raw.length === 1) return raw;\n"
            "  if (raw === 'Backspace') return 'backspace';\n"
            "  if (raw === 'Delete') return 'delete';\n"
            "  if (raw === 'Enter') return 'enter';\n"
            "  if (raw === 'Escape') return 'escape';\n"
            "  if (raw === 'Tab') return 'tab';\n"
            "  if (raw === 'ArrowLeft') return 'left';\n"
            "  if (raw === 'ArrowRight') return 'right';\n"
            "  if (raw === 'ArrowUp') return 'up';\n"
            "  if (raw === 'ArrowDown') return 'down';\n"
            "  return raw.toLowerCase();\n"
            "}\n"
            "function uiEventText(ev) {\n"
            "  const key = String(ev?.key ?? '');\n"
            "  if (key.length === 1 && !ev?.ctrlKey && !ev?.metaKey) return key;\n"
            "  return '';\n"
            "}\n"
            "function uiEventTargetId(ev) {\n"
            "  const target = ev?.target;\n"
            "  if (target && typeof target.getAttribute === 'function') {\n"
            "    return String(target.getAttribute('data-aivm-id') ?? '');\n"
            "  }\n"
            "  return '';\n"
            "}\n";
        const char* main_js_head3 =
            "function ensureUiWindow(windowId, title, width, height) {\n"
            "  if (typeof document === 'undefined' || !document || !document.createElement) return null;\n"
            "  const existing = uiState.windows.get(windowId);\n"
            "  if (existing) return existing;\n"
            "  const host = document.createElement('div');\n"
            "  host.setAttribute('data-aivm-window-id', String(windowId));\n"
            "  host.style.maxWidth = `${width}px`;\n"
            "  const label = document.createElement('div');\n"
            "  label.textContent = String(title ?? 'AiLang');\n"
            "  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');\n"
            "  svg.setAttribute('width', String(width));\n"
            "  svg.setAttribute('height', String(height));\n"
            "  svg.setAttribute('viewBox', `0 0 ${width} ${height}`);\n"
            "  svg.setAttribute('tabindex', '0');\n"
            "  svg.style.border = '1px solid #999';\n"
            "  svg.style.touchAction = 'none';\n"
            "  host.appendChild(label);\n"
            "  host.appendChild(svg);\n"
            "  (document.body || document.documentElement).appendChild(host);\n"
            "  const state = { host, svg, width, height, frameParts: [], nextElementId: 1, focusedTargetId: '', closed: false, closeConsumed: false, resizeHandler: null, pointerHandler: null, clickHandler: null, touchHandler: null, keyDownHandler: null, keyUpHandler: null, blurHandler: null, eventQueue: [], lastPolledEvent: { type: 'none', targetId: '', x: -1, y: -1, key: '', text: '', modifiers: '', repeat: false } };\n"
            "  const pushEvent = (evt) => { state.eventQueue.push(evt); if (state.eventQueue.length > 64) state.eventQueue.shift(); };\n"
            "  const clampToWindow = (x, y) => {\n"
            "    const maxX = Math.max(0, (state.width | 0) - 1);\n"
            "    const maxY = Math.max(0, (state.height | 0) - 1);\n"
            "    const nx = Math.min(maxX, Math.max(0, x | 0));\n"
            "    const ny = Math.min(maxY, Math.max(0, y | 0));\n"
            "    return { x: nx, y: ny };\n"
            "  };\n"
            "  const eventToLocal = (ev) => {\n"
            "    const ox = Number(ev?.offsetX);\n"
            "    const oy = Number(ev?.offsetY);\n"
            "    if (Number.isFinite(ox) && Number.isFinite(oy)) {\n"
            "      return { x: ox, y: oy };\n"
            "    }\n"
            "    const rect = svg.getBoundingClientRect();\n"
            "    const cx = Number(ev?.clientX);\n"
            "    const cy = Number(ev?.clientY);\n"
            "    return {\n"
            "      x: Number.isFinite(cx) ? (cx - rect.left) : 0,\n"
            "      y: Number.isFinite(cy) ? (cy - rect.top) : 0,\n"
            "    };\n"
            "  };\n"
            "  const syncWindowSize = () => {\n"
            "    const rect = svg.getBoundingClientRect();\n"
            "    const nextW = Math.max(1, rect.width | 0);\n"
            "    const nextH = Math.max(1, rect.height | 0);\n"
            "    state.width = nextW; state.height = nextH;\n"
            "    svg.setAttribute('width', String(nextW));\n"
            "    svg.setAttribute('height', String(nextH));\n"
            "    svg.setAttribute('viewBox', `0 0 ${nextW} ${nextH}`);\n"
            "  };\n";
        const char* main_js_head3b =
            "  const emitClickEvent = (sourceEv, pointEv) => {\n"
            "    svg.focus();\n"
            "    const targetId = uiEventTargetId(sourceEv);\n"
            "    const p = eventToLocal(pointEv ?? sourceEv);\n"
            "    const clamped = clampToWindow(p.x, p.y);\n"
            "    state.focusedTargetId = targetId;\n"
            "    pushEvent({ type: 'click', targetId, x: clamped.x, y: clamped.y, key: '', text: '', modifiers: uiEventModifiers(sourceEv), repeat: false });\n"
            "  };\n"
            "  if (typeof window !== 'undefined' && 'PointerEvent' in window) {\n"
            "    const onPointerDown = (ev) => { emitClickEvent(ev, ev); };\n"
            "    state.pointerHandler = onPointerDown;\n"
            "    svg.addEventListener('pointerdown', onPointerDown);\n"
            "  } else {\n"
            "    const onClick = (ev) => { emitClickEvent(ev, ev); };\n"
            "    const onTouchStart = (ev) => {\n"
            "      if (typeof ev?.preventDefault === 'function') ev.preventDefault();\n"
            "      const touch = (ev?.touches && ev.touches.length > 0) ? ev.touches[0] : null;\n"
            "      emitClickEvent(ev, touch);\n"
            "    };\n"
            "    state.clickHandler = onClick;\n"
            "    state.touchHandler = onTouchStart;\n"
            "    svg.addEventListener('click', onClick);\n"
            "    svg.addEventListener('touchstart', onTouchStart, { passive: false });\n"
            "  }\n"
            "  const emitKeyEvent = (ev) => {\n"
            "    pushEvent({ type: 'key', targetId: String(state.focusedTargetId ?? ''), x: -1, y: -1, key: uiEventKey(ev?.key), text: uiEventText(ev), modifiers: uiEventModifiers(ev), repeat: !!ev?.repeat });\n"
            "  };\n"
            "  const onKeyDown = (ev) => { emitKeyEvent(ev); };\n"
            "  const onKeyUp = (ev) => { emitKeyEvent(ev); };\n"
            "  state.keyDownHandler = onKeyDown;\n"
            "  state.keyUpHandler = onKeyUp;\n"
            "  svg.addEventListener('keydown', onKeyDown);\n"
            "  svg.addEventListener('keyup', onKeyUp);\n"
            "  const onBlur = () => { state.focusedTargetId = ''; };\n"
            "  state.blurHandler = onBlur;\n"
            "  svg.addEventListener('blur', onBlur);\n"
            "  if (typeof window !== 'undefined' && window?.addEventListener) {\n"
            "    const onResize = () => { if (!state.closed) syncWindowSize(); };\n"
            "    state.resizeHandler = onResize;\n"
            "    window.addEventListener('resize', onResize);\n"
            "  }\n"
            "  uiState.windows.set(windowId, state);\n"
            "  return state;\n"
            "}\n";
        const char* main_js_head4 =
            "globalThis.__aivmUiCreateWindow = (windowId, title, width, height) => {\n"
            "  if (!Number.isInteger(windowId) || windowId <= 0 || !Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0) return -1;\n"
            "  return ensureUiWindow(windowId, title, width, height) ? 0 : -1;\n"
            "};\n"
            "globalThis.__aivmUiBeginFrame = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  win.frameParts = [];\n"
            "  win.nextElementId = 1;\n"
            "  return 0;\n"
            "};\n"
            "globalThis.__aivmUiDrawRect = (windowId, x, y, w, h, color) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  const id = `n${win.nextElementId++}`;\n"
            "  win.frameParts.push(`<rect data-aivm-id=\"${id}\" x=\"${x|0}\" y=\"${y|0}\" width=\"${w|0}\" height=\"${h|0}\" fill=\"${xmlEscape(color)}\"/>`);\n"
            "  return 0;\n"
            "};\n";
        const char* main_js_mid =
            "globalThis.__aivmUiDrawText = (windowId, x, y, text, color, size) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  const id = `n${win.nextElementId++}`;\n"
            "  const textEscaped = xmlEscape(text);\n"
            "  win.frameParts.push(`<text data-aivm-id=\"${id}\" x=\"${x|0}\" y=\"${y|0}\" fill=\"${xmlEscape(color)}\" font-size=\"${size|0}\">${textEscaped}</text>`);\n"
            "  return 0;\n"
            "};\n"
            "globalThis.__aivmUiDrawLine = (windowId, x1, y1, x2, y2, color, width) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  const id = `n${win.nextElementId++}`;\n"
            "  win.frameParts.push(`<line data-aivm-id=\"${id}\" x1=\"${x1|0}\" y1=\"${y1|0}\" x2=\"${x2|0}\" y2=\"${y2|0}\" stroke=\"${xmlEscape(color)}\" stroke-width=\"${width|0}\"/>`);\n"
            "  return 0;\n"
            "};\n"
            "globalThis.__aivmUiDrawEllipse = (windowId, x, y, w, h, color) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  const id = `n${win.nextElementId++}`;\n"
            "  const rx = (w|0) / 2;\n"
            "  const ry = (h|0) / 2;\n"
            "  const cx = (x|0) + rx;\n"
            "  const cy = (y|0) + ry;\n"
            "  win.frameParts.push(`<ellipse data-aivm-id=\"${id}\" cx=\"${cx}\" cy=\"${cy}\" rx=\"${rx}\" ry=\"${ry}\" fill=\"${xmlEscape(color)}\"/>`);\n"
            "  return 0;\n"
            "};\n"
            "globalThis.__aivmUiDrawPath = (windowId, path, color, strokeWidth) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  const id = `n${win.nextElementId++}`;\n"
            "  const sw = strokeWidth|0;\n"
            "  const safePath = xmlEscape(path);\n"
            "  if (sw > 0) {\n"
            "    win.frameParts.push(`<path data-aivm-id=\"${id}\" d=\"${safePath}\" fill=\"none\" stroke=\"${xmlEscape(color)}\" stroke-width=\"${sw}\"/>`);\n"
            "  } else {\n"
            "    win.frameParts.push(`<path data-aivm-id=\"${id}\" d=\"${safePath}\" fill=\"${xmlEscape(color)}\"/>`);\n"
            "  }\n"
            "  return 0;\n"
            "};\n"
            "globalThis.__aivmUiDrawImage = (windowId, x, y, w, h, src) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  const id = `n${win.nextElementId++}`;\n"
            "  win.frameParts.push(`<image data-aivm-id=\"${id}\" x=\"${x|0}\" y=\"${y|0}\" width=\"${w|0}\" height=\"${h|0}\" href=\"${xmlEscape(src)}\"/>`);\n"
            "  return 0;\n"
            "};\n"
            "globalThis.__aivmUiEndFrame = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  return (win && !win.closed) ? 0 : -1;\n"
            "};\n";
        const char* main_js_tail =
            "globalThis.__aivmUiPresent = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  win.svg.innerHTML = win.frameParts.join('');\n"
            "  return 0;\n"
            "};\n"
            "globalThis.__aivmUiCloseWindow = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win) return -1;\n"
            "  if (win.closed) return 0;\n"
            "  win.closed = true;\n"
            "  win.focusedTargetId = '';\n"
            "  if (typeof window !== 'undefined' && window?.removeEventListener && typeof win.resizeHandler === 'function') {\n"
            "    window.removeEventListener('resize', win.resizeHandler);\n"
            "    win.resizeHandler = null;\n"
            "  }\n"
            "  if (typeof win.pointerHandler === 'function') {\n"
            "    win.svg.removeEventListener('pointerdown', win.pointerHandler);\n"
            "    win.pointerHandler = null;\n"
            "  }\n"
            "  if (typeof win.clickHandler === 'function') {\n"
            "    win.svg.removeEventListener('click', win.clickHandler);\n"
            "    win.clickHandler = null;\n"
            "  }\n"
            "  if (typeof win.touchHandler === 'function') {\n"
            "    win.svg.removeEventListener('touchstart', win.touchHandler, { passive: false });\n"
            "    win.touchHandler = null;\n"
            "  }\n"
            "  if (typeof win.keyDownHandler === 'function') {\n"
            "    win.svg.removeEventListener('keydown', win.keyDownHandler);\n"
            "    win.keyDownHandler = null;\n"
            "  }\n"
            "  if (typeof win.keyUpHandler === 'function') {\n"
            "    win.svg.removeEventListener('keyup', win.keyUpHandler);\n"
            "    win.keyUpHandler = null;\n"
            "  }\n"
            "  if (typeof win.blurHandler === 'function') {\n"
            "    win.svg.removeEventListener('blur', win.blurHandler);\n"
            "    win.blurHandler = null;\n"
            "  }\n"
            "  win.frameParts = [];\n"
            "  win.lastPolledEvent = { type: 'none', targetId: '', x: -1, y: -1, key: '', text: '', modifiers: '', repeat: false };\n"
            "  win.eventQueue.push({ type: 'closed', targetId: '', x: -1, y: -1, key: '', text: '', modifiers: '', repeat: false });\n"
            "  if (win.eventQueue.length > 64) win.eventQueue.shift();\n"
            "  win.host.remove();\n"
            "  return 0;\n"
            "};\n"
            "globalThis.__aivmUiWaitFrame = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  return 0;\n"
            "};\n";
        const char* main_js_tailb =
            "globalThis.__aivmUiGetWindowWidth = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  return (win.width | 0) || (win.svg?.viewBox?.baseVal?.width | 0) || -1;\n"
            "};\n"
            "globalThis.__aivmUiGetWindowHeight = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win || win.closed) return -1;\n"
            "  return (win.height | 0) || (win.svg?.viewBox?.baseVal?.height | 0) || -1;\n"
            "};\n"
            "globalThis.__aivmUiPollEventType = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win) return -1;\n"
            "  const evt = win.eventQueue.length > 0 ? win.eventQueue.shift() : { type: 'none', targetId: '', x: -1, y: -1, key: '', text: '', modifiers: '', repeat: false };\n"
            "  win.lastPolledEvent = evt;\n"
            "  win.closeConsumed = (evt.type === 'closed');\n"
            "  if (evt.type === 'closed') return 1;\n"
            "  if (evt.type === 'click') return 2;\n"
            "  if (evt.type === 'key') return 3;\n"
            "  return 0;\n"
            "};\n"
            "globalThis.__aivmUiPollEventX = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win) return -1;\n"
            "  return (win.lastPolledEvent?.x | 0);\n"
            "};\n"
            "globalThis.__aivmUiPollEventY = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win) return -1;\n"
            "  return (win.lastPolledEvent?.y | 0);\n"
            "};\n"
            "globalThis.__aivmUiPollEventTargetId = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win) return '';\n"
            "  return String(win.lastPolledEvent?.targetId ?? '');\n"
            "};\n"
            "globalThis.__aivmUiPollEventKey = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win) return '';\n"
            "  return String(win.lastPolledEvent?.key ?? '');\n"
            "};\n"
            "globalThis.__aivmUiPollEventText = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win) return '';\n"
            "  return String(win.lastPolledEvent?.text ?? '');\n"
            "};\n"
            "globalThis.__aivmUiPollEventModifiers = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win) return '';\n"
            "  return String(win.lastPolledEvent?.modifiers ?? '');\n"
            "};\n"
            "globalThis.__aivmUiPollEventRepeat = (windowId) => {\n"
            "  const win = uiState.windows.get(windowId);\n"
            "  if (!win) return -1;\n"
            "  const repeatValue = win.lastPolledEvent?.repeat ? 1 : 0;\n"
            "  if (win.closeConsumed) {\n"
            "    uiState.windows.delete(windowId);\n"
            "  }\n"
            "  return repeatValue;\n"
            "};\n"
            "function readHostStdin() {\n"
            "  if (typeof globalThis.AIVM_HOST_STDIN_READ !== 'function') return undefined;\n"
            "  return globalThis.AIVM_HOST_STDIN_READ();\n"
            "}\n";
        const char* main_js_tail2 =
            "aiLangRoot.stdin = {\n"
            "  push(text) { stdinQueue.push(String(text ?? '')); },\n"
            "  close() { stdinClosed = true; },\n"
            "  _drain() {\n"
            "    if (stdinQueue.length > 0) return stdinQueue.shift();\n"
            "    const hostValue = readHostStdin();\n"
            "    if (hostValue === null) return null;\n"
            "    if (hostValue !== undefined) return String(hostValue ?? '');\n"
            "    return stdinClosed ? null : '';\n"
            "  }\n"
            "};\n"
            "aiLangRoot.remote = aiLangRoot.remote || {};\n"
            "let remoteCallImpl = null;\n"
            "if (remoteMode === 'js' && typeof aiLangRoot.remote.call === 'function') {\n"
            "  remoteCallImpl = (cap, op, value) => aiLangRoot.remote.call(cap, op, value);\n"
            "} else if (remoteMode === 'js') {\n"
            "  remoteCallImpl = () => Promise.reject(new Error('AIVM_REMOTE_MODE=js requires AiLang.remote.call adapter'));\n"
            "} else {\n"
            "  const remoteClient = createAivmRemoteClient({ endpoint });\n"
            "  remoteCallImpl = (cap, op, value) => remoteClient.call(cap, op, value);\n"
            "}\n"
            "if (remoteMode !== 'js' || typeof aiLangRoot.remote.call !== 'function') {\n"
            "  aiLangRoot.remote.call = remoteCallImpl;\n"
            "}\n"
            "globalThis.__aivmRemoteCall = (cap, op, value) => remoteCallImpl(cap, op, value);\n"
            "globalThis.__aivmStdinRead = () => aiLangRoot.stdin._drain();\n"
            "const runtime = await createRuntime();\n"
            "const bytes = await (await fetch('./app.aibc1')).arrayBuffer();\n"
            "runtime.FS.writeFile('/app.aibc1', new Uint8Array(bytes));\n"
            "const logs = [];\n"
            "runtime.print = (line) => { const s = String(line); logs.push(s); console.log(s); };\n"
            "runtime.printErr = (line) => { const s = String(line); logs.push(s); console.error(s); };\n"
            "runtime.callMain(['/app.aibc1']);\n"
            "if (output) output.textContent = logs.join('\\n');\n";
        if (snprintf(main_js, sizeof(main_js), "%s%s%s%s%s%s%s%s%s", main_js_head, main_js_head2, main_js_head3, main_js_head3b, main_js_head4, main_js_mid, main_js_tail, main_js_tailb, main_js_tail2) >= (int)sizeof(main_js)) {
            return 0;
        }
    }
    {
        const char* remote_client_js_head =
            "function encodeText(text) { return new TextEncoder().encode(text); }\n"
            "function decodeText(bytes) { return new TextDecoder().decode(bytes); }\n"
            "function writeU16LE(arr, off, v) { arr[off]=v&255; arr[off+1]=(v>>8)&255; }\n"
            "function writeU32LE(arr, off, v) { arr[off]=v&255; arr[off+1]=(v>>8)&255; arr[off+2]=(v>>16)&255; arr[off+3]=(v>>24)&255; }\n"
            "function readU16LE(arr, off) { return arr[off] | (arr[off+1] << 8); }\n"
            "function readU32LE(arr, off) { return (arr[off] | (arr[off+1] << 8) | (arr[off+2] << 16) | (arr[off+3] << 24)) >>> 0; }\n"
            "function encodeStr(s) { const b = encodeText(s); const out = new Uint8Array(2 + b.length); writeU16LE(out, 0, b.length); out.set(b, 2); return out; }\n"
            "function decodeStr(arr, off) { if (off + 2 > arr.length) throw new Error('bad string header'); const n = readU16LE(arr, off); const start = off + 2; if (start + n > arr.length) throw new Error('bad string length'); return { value: decodeText(arr.slice(start, start + n)), next: start + n }; }\n"
            "function join(parts) { const len = parts.reduce((a,b)=>a+b.length,0); const out = new Uint8Array(len); let i=0; for (const p of parts) { out.set(p, i); i += p.length; } return out; }\n"
            "function encodeCall(id, cap, op, value) {\n"
            "  const capB = encodeStr(cap); const opB = encodeStr(op);\n"
            "  const payload = new Uint8Array(capB.length + opB.length + 8);\n"
            "  payload.set(capB, 0); payload.set(opB, capB.length);\n"
            "  const dv = new DataView(payload.buffer); dv.setBigInt64(capB.length + opB.length, BigInt(value), true);\n"
            "  const frame = new Uint8Array(9 + payload.length); frame[0] = 0x10; writeU32LE(frame, 1, id); writeU32LE(frame, 5, payload.length); frame.set(payload, 9); return frame;\n"
            "}\n"
            "function decodeFrame(arr) {\n"
            "  if (!(arr instanceof Uint8Array) || arr.length < 9) throw new Error('short frame');\n"
            "  const payloadLen = readU32LE(arr, 5);\n"
            "  if (arr.length < 9 + payloadLen) throw new Error('truncated frame');\n"
            "  return { type: arr[0], id: readU32LE(arr,1), payload: arr.slice(9, 9 + payloadLen) };\n"
            "}\n"
            "function decodeResult(payload) { if (!(payload instanceof Uint8Array) || payload.length < 8) throw new Error('bad result payload'); const dv = new DataView(payload.buffer, payload.byteOffset, payload.byteLength); return Number(dv.getBigInt64(0, true)); }\n"
            "function decodeError(payload) { if (!(payload instanceof Uint8Array) || payload.length < 6) throw new Error('bad error payload'); const code = readU32LE(payload, 0); const msg = decodeStr(payload, 4).value; return { code, message: msg }; }\n"
            "function encodeHello(id, caps) {\n"
            "  const clientName = encodeStr('aivm-web-client');\n"
            "  const capParts = caps.map((c)=>encodeStr(c));\n"
            "  const header = new Uint8Array(2 + 4); writeU16LE(header, 0, 1); writeU32LE(header, 2, caps.length);\n"
            "  const payload = join([clientName, header, ...capParts]);\n"
            "  const frame = new Uint8Array(9 + payload.length); frame[0]=0x01; writeU32LE(frame,1,id); writeU32LE(frame,5,payload.length); frame.set(payload,9); return frame;\n"
            "}\n";
        const char* remote_client_js_tail =
            "export function createAivmRemoteClient({ endpoint }) {\n"
            "  let ws = null; let nextId = 2; const pending = new Map(); let ready = null;\n"
            "  function failPending(reason) {\n"
            "    for (const [, p] of pending) { p.reject(new Error(reason)); }\n"
            "    pending.clear();\n"
            "  }\n"
            "  function ensureSocket() {\n"
            "    if (ws && ws.readyState === WebSocket.OPEN) return Promise.resolve();\n"
            "    if (ready) return ready;\n"
            "    ready = new Promise((resolve, reject) => {\n"
            "      let settled = false;\n"
            "      const resolveReady = () => { if (settled) return; settled = true; resolve(); };\n"
            "      const rejectReady = (message) => { if (settled) return; settled = true; reject(new Error(message)); };\n"
            "      ws = new WebSocket(endpoint); ws.binaryType = 'arraybuffer';\n"
            "      ws.onopen = () => { ws.send(encodeHello(1, ['cap.remote'])); };\n"
            "      ws.onmessage = (ev) => {\n"
            "        let frame = null;\n"
            "        if (!(ev.data instanceof ArrayBuffer)) { rejectReady('remote invalid websocket frame payload'); ready = null; ws = null; failPending('remote invalid websocket frame payload'); return; }\n"
            "        try { const arr = new Uint8Array(ev.data); frame = decodeFrame(arr); } catch (_) { rejectReady('remote invalid websocket frame payload'); ready = null; ws = null; failPending('remote invalid websocket frame payload'); return; }\n"
            "        if (!settled && frame.id !== 1) { rejectReady(`remote unexpected handshake frame id ${frame.id}`); ready = null; ws = null; return; }\n"
            "        if (frame.type === 0x02 && frame.id === 1) { resolveReady(); return; }\n"
            "        if (frame.type === 0x03 && frame.id === 1) { let err = null; try { err = decodeError(frame.payload); } catch (_) { rejectReady('remote invalid websocket frame payload'); ready = null; ws = null; failPending('remote invalid websocket frame payload'); return; } rejectReady(`remote handshake denied ${err.code}: ${err.message}`); ready = null; ws = null; return; }\n"
            "        if (frame.id === 1) { rejectReady(`remote unexpected handshake frame type ${frame.type}`); ready = null; ws = null; return; }\n"
            "        const p = pending.get(frame.id); if (!p) return;\n"
            "        if (frame.type === 0x11) { let resultValue = 0; try { resultValue = decodeResult(frame.payload); } catch (_) { pending.delete(frame.id); p.reject(new Error('remote invalid websocket frame payload')); ready = null; ws = null; failPending('remote invalid websocket frame payload'); return; } pending.delete(frame.id); p.resolve(resultValue); return; }\n"
            "        if (frame.type === 0x12) { let err = null; try { err = decodeError(frame.payload); } catch (_) { pending.delete(frame.id); p.reject(new Error('remote invalid websocket frame payload')); ready = null; ws = null; failPending('remote invalid websocket frame payload'); return; } pending.delete(frame.id); p.reject(new Error(`remote ${err.code}: ${err.message}`)); return; }\n"
            "        pending.delete(frame.id); p.reject(new Error(`remote unexpected frame type ${frame.type}`));\n"
            "      };\n"
            "      ws.onerror = () => { rejectReady('remote websocket error'); ready = null; ws = null; failPending('remote websocket error'); };\n"
            "      ws.onclose = () => { rejectReady('remote websocket closed'); ready = null; ws = null; failPending('remote websocket closed'); };\n"
            "    });\n"
            "    return ready;\n"
            "  }\n"
            "  return {\n"
            "    async call(cap, op, value) {\n"
            "      await ensureSocket();\n"
            "      const id = nextId++;\n"
            "      return new Promise((resolve, reject) => { pending.set(id, { resolve, reject }); ws.send(encodeCall(id, cap, op, value)); });\n"
            "    }\n"
            "  };\n"
            "}\n";
        if (snprintf(remote_client_js, sizeof(remote_client_js), "%s%s", remote_client_js_head, remote_client_js_tail) >= (int)sizeof(remote_client_js)) {
            return 0;
        }
    }
    return write_text_file(index_path, index_html) &&
           write_text_file(main_path, main_js) &&
           write_text_file(remote_client_path, remote_client_js);
}

static int emit_wasm_fullstack_layout(const char* out_dir)
{
    char www_dir[PATH_MAX];
    char readme_path[PATH_MAX];
    char readme[1024];
    if (out_dir == NULL) {
        return 0;
    }
    if (!join_path(out_dir, "www", www_dir, sizeof(www_dir))) {
        return 0;
    }
    if (!ensure_directory(www_dir)) {
        return 0;
    }
    if (!emit_wasm_spa_files(www_dir)) {
        return 0;
    }
    if (snprintf(
            readme,
            sizeof(readme),
            "# AiLang wasm fullstack package\n\n"
            "This package contains a self-contained app binary and `www/` client assets.\n"
            "The app binary is responsible for serving `www/` and websocket endpoint `ws://<host>:8765`.\n"
            "Set `AIVM_REMOTE_WS_ENDPOINT` in browser page to override endpoint.\n") >= (int)sizeof(readme)) {
        return 0;
    }
    if (!join_path(out_dir, "README.md", readme_path, sizeof(readme_path))) {
        return 0;
    }
    if (!write_text_file(readme_path, readme)) {
        return 0;
    }
    return 1;
}

#define NATIVE_PROCESS_CAPACITY 32U
#define NATIVE_PROCESS_READ_CHUNK 4096U
#define NATIVE_WORKER_CAPACITY 64U
#define NATIVE_WORKER_TEXT_CAPACITY 512U
#define NATIVE_NET_HANDLE_CAPACITY 64U
#define NATIVE_NET_ASYNC_CAPACITY 128U
#define NATIVE_NET_BYTES_CHUNK 65536U

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

typedef struct NativeWorkerState
{
    int used;
    int status; /* 0 pending, 1 success, -1 failure, -2 canceled */
    int completion_status;
    int64_t polls_remaining;
    char result[NATIVE_WORKER_TEXT_CAPACITY];
    char error[NATIVE_WORKER_TEXT_CAPACITY];
} NativeWorkerState;

static NativeWorkerState g_native_workers[NATIVE_WORKER_CAPACITY];

typedef enum {
    NATIVE_NET_HANDLE_KIND_NONE = 0,
    NATIVE_NET_HANDLE_KIND_TCP_LISTENER = 1,
    NATIVE_NET_HANDLE_KIND_TCP_STREAM = 2,
    NATIVE_NET_HANDLE_KIND_UDP_SOCKET = 3
} NativeNetHandleKind;

typedef struct NativeNetHandleState
{
    int used;
    NativeNetHandleKind kind;
    NativeSocket socket;
} NativeNetHandleState;

typedef struct NativeNetAsyncState
{
    int used;
    int status; /* 0 pending, 1 success, -1 failure, -2 canceled */
    int64_t result_int;
    uint8_t* result_bytes;
    size_t result_bytes_len;
    char error[128];
} NativeNetAsyncState;

static NativeNetHandleState g_native_net_handles[NATIVE_NET_HANDLE_CAPACITY];
static NativeNetAsyncState g_native_net_async_ops[NATIVE_NET_ASYNC_CAPACITY];
static uint8_t g_native_net_bytes_scratch[NATIVE_NET_BYTES_CHUNK];
static char g_native_net_text_scratch[NATIVE_NET_BYTES_CHUNK];
static char g_native_net_host_scratch[64];

static void native_worker_init_slot(NativeWorkerState* worker)
{
    if (worker == NULL) {
        return;
    }
    memset(worker, 0, sizeof(*worker));
    worker->status = 0;
    worker->completion_status = 1;
}

static NativeWorkerState* native_worker_lookup(int64_t handle_value)
{
    size_t index;
    if (handle_value <= 0 || handle_value > (int64_t)NATIVE_WORKER_CAPACITY) {
        return NULL;
    }
    index = (size_t)(handle_value - 1);
    if (!g_native_workers[index].used) {
        return NULL;
    }
    return &g_native_workers[index];
}

static int64_t native_worker_allocate_slot(void)
{
    size_t index;
    for (index = 0U; index < NATIVE_WORKER_CAPACITY; index += 1U) {
        if (!g_native_workers[index].used) {
            native_worker_init_slot(&g_native_workers[index]);
            g_native_workers[index].used = 1;
            return (int64_t)(index + 1U);
        }
    }
    return -1;
}

static void native_worker_start_task(NativeWorkerState* worker, const char* task_name, const char* payload)
{
    int poll_ticks = 2;
    if (worker == NULL || task_name == NULL || payload == NULL) {
        return;
    }

    if (strcmp(task_name, "echo") == 0) {
        worker->completion_status = 1;
        (void)snprintf(worker->result, sizeof(worker->result), "%s", payload);
    } else if (strcmp(task_name, "fail") == 0) {
        worker->completion_status = -1;
        if (payload[0] != '\0') {
            (void)snprintf(worker->error, sizeof(worker->error), "%s", payload);
        } else {
            (void)snprintf(worker->error, sizeof(worker->error), "worker_failed");
        }
    } else if (strcmp(task_name, "sleep") == 0) {
        char* end = NULL;
        long parsed = strtol(payload, &end, 10);
        if (end == payload || *end != '\0' || parsed < 0L || parsed > 1000000L) {
            worker->completion_status = -1;
            (void)snprintf(worker->error, sizeof(worker->error), "invalid_sleep_ticks");
        } else {
            worker->completion_status = 1;
            poll_ticks = (int)parsed + 1;
            (void)snprintf(worker->result, sizeof(worker->result), "slept");
        }
    } else {
        worker->completion_status = -1;
        (void)snprintf(worker->error, sizeof(worker->error), "unknown_task");
    }

    worker->polls_remaining = (int64_t)poll_ticks;
}

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

static void native_net_reset(void)
{
    size_t i;
    for (i = 0U; i < NATIVE_NET_HANDLE_CAPACITY; i += 1U) {
        if (g_native_net_handles[i].used && g_native_net_handles[i].socket != NATIVE_INVALID_SOCKET) {
            native_socket_close(g_native_net_handles[i].socket);
        }
        g_native_net_handles[i].used = 0;
        g_native_net_handles[i].kind = NATIVE_NET_HANDLE_KIND_NONE;
        g_native_net_handles[i].socket = NATIVE_INVALID_SOCKET;
    }
    for (i = 0U; i < NATIVE_NET_ASYNC_CAPACITY; i += 1U) {
        if (g_native_net_async_ops[i].result_bytes != NULL) {
            free(g_native_net_async_ops[i].result_bytes);
        }
        memset(&g_native_net_async_ops[i], 0, sizeof(g_native_net_async_ops[i]));
    }
}

static int native_net_platform_init(void)
{
#ifdef _WIN32
    static int wsa_ready = 0;
    if (!wsa_ready) {
        WSADATA wsa_data;
        if (WSAStartup(MAKEWORD(2, 2), &wsa_data) != 0) {
            return 0;
        }
        wsa_ready = 1;
    }
#endif
    return 1;
}

static int native_net_parse_ipv4(const char* host, uint16_t port, struct sockaddr_in* out_addr)
{
    if (out_addr == NULL) {
        return 0;
    }
    memset(out_addr, 0, sizeof(*out_addr));
    out_addr->sin_family = AF_INET;
    out_addr->sin_port = htons(port);
    if (host == NULL || host[0] == '\0' || strcmp(host, "*") == 0 || strcmp(host, "0.0.0.0") == 0) {
        out_addr->sin_addr.s_addr = htonl(INADDR_ANY);
        return 1;
    }
    if (strcmp(host, "localhost") == 0) {
        out_addr->sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        return 1;
    }
    return inet_pton(AF_INET, host, &out_addr->sin_addr) == 1;
}

static NativeNetHandleState* native_net_handle_lookup(int64_t handle)
{
    size_t index;
    if (handle <= 0 || handle > (int64_t)NATIVE_NET_HANDLE_CAPACITY) {
        return NULL;
    }
    index = (size_t)(handle - 1);
    if (!g_native_net_handles[index].used) {
        return NULL;
    }
    return &g_native_net_handles[index];
}

static int64_t native_net_handle_allocate(NativeNetHandleKind kind, NativeSocket socket_fd)
{
    size_t i;
    if (kind == NATIVE_NET_HANDLE_KIND_NONE || socket_fd == NATIVE_INVALID_SOCKET) {
        return -1;
    }
    for (i = 0U; i < NATIVE_NET_HANDLE_CAPACITY; i += 1U) {
        if (!g_native_net_handles[i].used) {
            g_native_net_handles[i].used = 1;
            g_native_net_handles[i].kind = kind;
            g_native_net_handles[i].socket = socket_fd;
            return (int64_t)(i + 1U);
        }
    }
    return -1;
}

static int native_net_handle_close(int64_t handle)
{
    NativeNetHandleState* state = native_net_handle_lookup(handle);
    if (state == NULL) {
        return 0;
    }
    if (state->socket != NATIVE_INVALID_SOCKET) {
        native_socket_close(state->socket);
    }
    state->used = 0;
    state->kind = NATIVE_NET_HANDLE_KIND_NONE;
    state->socket = NATIVE_INVALID_SOCKET;
    return 1;
}

static NativeNetAsyncState* native_net_async_lookup(int64_t op_handle)
{
    size_t index;
    if (op_handle <= 0 || op_handle > (int64_t)NATIVE_NET_ASYNC_CAPACITY) {
        return NULL;
    }
    index = (size_t)(op_handle - 1);
    if (!g_native_net_async_ops[index].used) {
        return NULL;
    }
    return &g_native_net_async_ops[index];
}

static int64_t native_net_async_allocate(void)
{
    size_t i;
    for (i = 0U; i < NATIVE_NET_ASYNC_CAPACITY; i += 1U) {
        if (!g_native_net_async_ops[i].used) {
            memset(&g_native_net_async_ops[i], 0, sizeof(g_native_net_async_ops[i]));
            g_native_net_async_ops[i].used = 1;
            g_native_net_async_ops[i].status = 0;
            return (int64_t)(i + 1U);
        }
    }
    return -1;
}

static void native_net_async_set_success_int(NativeNetAsyncState* op, int64_t value)
{
    if (op == NULL) {
        return;
    }
    op->status = 1;
    op->result_int = value;
    op->error[0] = '\0';
}

static void native_net_async_set_success_bytes(NativeNetAsyncState* op, const uint8_t* bytes, size_t len)
{
    if (op == NULL) {
        return;
    }
    if (op->result_bytes != NULL) {
        free(op->result_bytes);
        op->result_bytes = NULL;
    }
    op->result_bytes_len = 0U;
    if (len > 0U && bytes != NULL) {
        op->result_bytes = (uint8_t*)malloc(len);
        if (op->result_bytes == NULL) {
            op->status = -1;
            (void)snprintf(op->error, sizeof(op->error), "alloc_failed");
            return;
        }
        memcpy(op->result_bytes, bytes, len);
        op->result_bytes_len = len;
    }
    op->status = 1;
    op->result_int = 0;
    op->error[0] = '\0';
}

static void native_net_async_set_failure(NativeNetAsyncState* op, const char* message)
{
    if (op == NULL) {
        return;
    }
    op->status = -1;
    op->result_int = 0;
    if (message == NULL || message[0] == '\0') {
        (void)snprintf(op->error, sizeof(op->error), "failed");
    } else {
        (void)snprintf(op->error, sizeof(op->error), "%s", message);
    }
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

static int native_syscall_console_write(
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
    fputs(args[0].string_value, stdout);
    fflush(stdout);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_console_write_line(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    return native_syscall_stdout_write_line(target, args, arg_count, result);
}

static int native_syscall_console_write_err_line(
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
    fprintf(stderr, "%s\n", args[0].string_value);
    fflush(stderr);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_console_read_line(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    static char line[4096];
    size_t len;
    (void)target;
    (void)args;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 0U) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (fgets(line, (int)sizeof(line), stdin) == NULL) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    len = strlen(line);
    while (len > 0U && (line[len - 1U] == '\n' || line[len - 1U] == '\r')) {
        line[len - 1U] = '\0';
        len -= 1U;
    }
    *result = aivm_value_string(line);
    return AIVM_SYSCALL_OK;
}

static char* g_native_stdin_all_scratch = NULL;
static size_t g_native_stdin_all_scratch_capacity = 0U;

static int native_syscall_console_read_all_stdin(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t used = 0U;
    int ch;
    (void)target;
    (void)args;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 0U) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (g_native_stdin_all_scratch == NULL) {
        g_native_stdin_all_scratch_capacity = 4096U;
        g_native_stdin_all_scratch = (char*)malloc(g_native_stdin_all_scratch_capacity);
        if (g_native_stdin_all_scratch == NULL) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
    }
    while ((ch = fgetc(stdin)) != EOF) {
        if (used + 2U > g_native_stdin_all_scratch_capacity) {
            char* grown;
            size_t new_capacity = g_native_stdin_all_scratch_capacity * 2U;
            if (new_capacity < used + 2U) {
                new_capacity = used + 2U;
            }
            grown = (char*)realloc(g_native_stdin_all_scratch, new_capacity);
            if (grown == NULL) {
                result->type = AIVM_VAL_VOID;
                return AIVM_SYSCALL_ERR_INVALID;
            }
            g_native_stdin_all_scratch = grown;
            g_native_stdin_all_scratch_capacity = new_capacity;
        }
        g_native_stdin_all_scratch[used++] = (char)ch;
    }
    g_native_stdin_all_scratch[used] = '\0';
    *result = aivm_value_string(g_native_stdin_all_scratch);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_identity_0(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    const char* value = "";
    (void)args;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (target == NULL || arg_count != 0U) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (strcmp(target, "sys.platform") == 0) {
#ifdef _WIN32
        value = "windows";
#elif defined(__APPLE__)
        value = "osx";
#else
        value = "linux";
#endif
    } else if (strcmp(target, "sys.arch") == 0) {
#if defined(_M_ARM64) || defined(__aarch64__) || defined(__arm64__)
        value = "arm64";
#else
        value = "x64";
#endif
    } else if (strcmp(target, "sys.runtime") == 0) {
        value = "airun-native-c";
    } else if (strcmp(target, "sys.os.version") == 0) {
#ifdef _WIN32
        value = "windows";
#elif defined(__APPLE__)
        value = "macos";
#else
        value = "linux";
#endif
    } else {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    *result = aivm_value_string(value);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_time_now_unix_ms(
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
#ifdef _WIN32
    {
        FILETIME file_time;
        ULARGE_INTEGER ticks;
        GetSystemTimeAsFileTime(&file_time);
        ticks.LowPart = file_time.dwLowDateTime;
        ticks.HighPart = file_time.dwHighDateTime;
        *result = aivm_value_int((int64_t)((ticks.QuadPart - 116444736000000000ULL) / 10000ULL));
    }
#else
    {
        struct timeval tv;
        if (gettimeofday(&tv, NULL) != 0) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        *result = aivm_value_int((int64_t)tv.tv_sec * 1000LL + (int64_t)(tv.tv_usec / 1000));
    }
#endif
    return AIVM_SYSCALL_OK;
}

static int native_syscall_time_monotonic_ms(
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
#ifdef _WIN32
    *result = aivm_value_int((int64_t)GetTickCount64());
#else
    {
        struct timespec ts;
        if (clock_gettime(CLOCK_MONOTONIC, &ts) != 0) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        *result = aivm_value_int((int64_t)ts.tv_sec * 1000LL + (int64_t)(ts.tv_nsec / 1000000L));
    }
#endif
    return AIVM_SYSCALL_OK;
}

static int native_syscall_time_sleep_ms(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    int64_t ms;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    ms = args[0].int_value;
    if (ms < 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
#ifdef _WIN32
    Sleep((DWORD)ms);
#else
    {
        struct timespec ts;
        ts.tv_sec = (time_t)(ms / 1000);
        ts.tv_nsec = (long)((ms % 1000) * 1000000L);
        while (nanosleep(&ts, &ts) != 0 && errno == EINTR) {
        }
    }
#endif
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_process_cwd(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    static char cwd[PATH_MAX];
    (void)target;
    (void)args;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 0U) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
#ifdef _WIN32
    if (_getcwd(cwd, (int)sizeof(cwd)) == NULL) {
#else
    if (getcwd(cwd, sizeof(cwd)) == NULL) {
#endif
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_string(cwd);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_process_env_get(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    const char* value;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    value = getenv(args[0].string_value);
    *result = aivm_value_string((value == NULL) ? "" : value);
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

static int native_syscall_fs_file_exists(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    *result = aivm_value_bool(file_exists(args[0].string_value) ? 1 : 0);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_fs_path_exists(
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
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    *result = aivm_value_bool(stat(args[0].string_value, &st) == 0 ? 1 : 0);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_fs_dir_create(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!ensure_directory_recursive(args[0].string_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static unsigned char* g_native_file_bytes_scratch = NULL;
static size_t g_native_file_bytes_scratch_capacity = 0U;

static int native_ensure_file_bytes_scratch(size_t needed)
{
    unsigned char* grown;
    size_t new_capacity;
    if (needed <= g_native_file_bytes_scratch_capacity) {
        return 1;
    }
    new_capacity = g_native_file_bytes_scratch_capacity == 0U ? 4096U : g_native_file_bytes_scratch_capacity;
    while (new_capacity < needed) {
        if (new_capacity > (SIZE_MAX / 2U)) {
            return 0;
        }
        new_capacity *= 2U;
    }
    grown = (unsigned char*)realloc(g_native_file_bytes_scratch, new_capacity);
    if (grown == NULL) {
        return 0;
    }
    g_native_file_bytes_scratch = grown;
    g_native_file_bytes_scratch_capacity = new_capacity;
    return 1;
}

static int native_syscall_fs_file_read(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    unsigned char* bytes = NULL;
    size_t byte_count = 0U;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!read_binary_file(args[0].string_value, &bytes, &byte_count)) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
    if (!native_ensure_file_bytes_scratch(byte_count)) {
        free(bytes);
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (byte_count > 0U) {
        memcpy(g_native_file_bytes_scratch, bytes, byte_count);
    }
    free(bytes);
    *result = aivm_value_bytes(g_native_file_bytes_scratch, byte_count);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_fs_file_write(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    FILE* fp;
    size_t wrote = 0U;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_BYTES) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    fp = fopen(args[0].string_value, "wb");
    if (fp == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (args[1].bytes_value.length > 0U && args[1].bytes_value.data != NULL) {
        wrote = fwrite(args[1].bytes_value.data, 1U, args[1].bytes_value.length, fp);
    }
    if (fclose(fp) != 0 || wrote != args[1].bytes_value.length) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_void();
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
            DWORD process_exit_code;
            process->finished = 1;
            if (GetExitCodeProcess(process->process_handle, &process_exit_code) != 0) {
                process->exit_code = (int)process_exit_code;
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

static int native_syscall_process_exit(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    int code;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    code = (int)args[0].int_value;
    *result = aivm_value_void();
    fflush(stdout);
    fflush(stderr);
    exit(code);
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

static AivmRemoteServerConfig g_remote_server_config;
static AivmRemoteServerSession g_remote_server_session;
static uint32_t g_remote_next_request_id = 1U;
static int g_remote_session_ready = 0;

static int remote_parse_caps_csv(
    const char* csv,
    char out_caps[AIVM_REMOTE_MAX_CAPS][AIVM_REMOTE_MAX_TEXT + 1],
    uint32_t* out_count)
{
    const char* cursor;
    uint32_t count = 0U;
    if (out_caps == NULL || out_count == NULL) {
        return 0;
    }
    if (csv == NULL || *csv == '\0') {
        *out_count = 0U;
        return 1;
    }
    cursor = csv;
    while (*cursor != '\0') {
        const char* end = cursor;
        size_t len;
        if (count >= AIVM_REMOTE_MAX_CAPS) {
            return 0;
        }
        while (*end != '\0' && *end != ',') {
            end += 1;
        }
        len = (size_t)(end - cursor);
        if (len > AIVM_REMOTE_MAX_TEXT) {
            return 0;
        }
        if (len > 0U) {
            memcpy(out_caps[count], cursor, len);
            out_caps[count][len] = '\0';
            count += 1U;
        }
        cursor = (*end == ',') ? (end + 1) : end;
    }
    *out_count = count;
    return 1;
}

static int remote_token_authorized(void)
{
    const char* expected = getenv("AIVM_REMOTE_EXPECTED_TOKEN");
    const char* provided = getenv("AIVM_REMOTE_SESSION_TOKEN");
    size_t expected_len;
    size_t provided_len;
    if (expected == NULL || provided == NULL) {
        return 0;
    }
    expected_len = strlen(expected);
    provided_len = strlen(provided);
    if (expected_len == 0U || provided_len == 0U) {
        return 0;
    }
    if (expected_len > 256U || provided_len > 256U) {
        return 0;
    }
    return strcmp(expected, provided) == 0;
}

static int remote_session_ensure_ready(void)
{
    uint8_t request_bytes[1024];
    uint8_t response_bytes[1024];
    size_t request_len = 0U;
    size_t response_len = 0U;
    AivmRemoteHello hello;
    AivmRemoteWelcome welcome;
    uint32_t response_id = 0U;
    AivmRemoteCodecStatus codec_status;
    AivmRemoteSessionStatus session_status;
    uint32_t i;
    const char* caps_env;

    if (g_remote_session_ready) {
        return 1;
    }

    memset(&g_remote_server_config, 0, sizeof(g_remote_server_config));
    g_remote_server_config.proto_version = 1U;
    caps_env = getenv("AIVM_REMOTE_CAPS");
    if (!remote_parse_caps_csv(
            caps_env,
            g_remote_server_config.allowed_caps,
            &g_remote_server_config.allowed_caps_count)) {
        return 0;
    }

    aivm_remote_server_session_init(&g_remote_server_session);
    memset(&hello, 0, sizeof(hello));
    hello.proto_version = 1U;
    (void)snprintf(hello.client_name, sizeof(hello.client_name), "%s", "airun-native");
    hello.requested_caps_count = g_remote_server_config.allowed_caps_count;
    for (i = 0U; i < hello.requested_caps_count; i += 1U) {
        (void)snprintf(
            hello.requested_caps[i],
            sizeof(hello.requested_caps[i]),
            "%s",
            g_remote_server_config.allowed_caps[i]);
    }

    codec_status = aivm_remote_encode_hello(
        1U,
        &hello,
        request_bytes,
        sizeof(request_bytes),
        &request_len);
    if (codec_status != AIVM_REMOTE_CODEC_OK) {
        return 0;
    }
    session_status = aivm_remote_server_process_frame(
        &g_remote_server_config,
        &g_remote_server_session,
        request_bytes,
        request_len,
        response_bytes,
        sizeof(response_bytes),
        &response_len);
    if (session_status != AIVM_REMOTE_SESSION_OK) {
        return 0;
    }
    memset(&welcome, 0, sizeof(welcome));
    codec_status = aivm_remote_decode_welcome(
        response_bytes,
        response_len,
        &response_id,
        &welcome);
    if (codec_status != AIVM_REMOTE_CODEC_OK || response_id != 1U) {
        return 0;
    }
    g_remote_session_ready = 1;
    return 1;
}

static int remote_session_invoke_call(
    const char* cap,
    const char* op,
    int64_t value,
    AivmValue* result)
{
    uint8_t request_bytes[1024];
    uint8_t response_bytes[1024];
    size_t request_len = 0U;
    size_t response_len = 0U;
    uint32_t request_id;
    uint32_t response_id = 0U;
    AivmRemoteCall call;
    AivmRemoteResult call_result;
    AivmRemoteError call_error;
    AivmRemoteCodecStatus codec_status;
    AivmRemoteSessionStatus session_status;

    if (cap == NULL || op == NULL || result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (!remote_session_ensure_ready()) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }

    memset(&call, 0, sizeof(call));
    (void)snprintf(call.cap, sizeof(call.cap), "%s", cap);
    (void)snprintf(call.op, sizeof(call.op), "%s", op);
    call.value = value;
    request_id = g_remote_next_request_id++;
    if (g_remote_next_request_id == 0U) {
        g_remote_next_request_id = 1U;
    }

    codec_status = aivm_remote_encode_call(
        request_id,
        &call,
        request_bytes,
        sizeof(request_bytes),
        &request_len);
    if (codec_status != AIVM_REMOTE_CODEC_OK) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    session_status = aivm_remote_server_process_frame(
        &g_remote_server_config,
        &g_remote_server_session,
        request_bytes,
        request_len,
        response_bytes,
        sizeof(response_bytes),
        &response_len);
    if (session_status != AIVM_REMOTE_SESSION_OK) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }

    codec_status = aivm_remote_decode_result(
        response_bytes,
        response_len,
        &response_id,
        &call_result);
    if (codec_status == AIVM_REMOTE_CODEC_OK && response_id == request_id) {
        *result = aivm_value_int(call_result.value);
        return AIVM_SYSCALL_OK;
    }
    codec_status = aivm_remote_decode_error(
        response_bytes,
        response_len,
        &response_id,
        &call_error);
    if (codec_status == AIVM_REMOTE_CODEC_OK && response_id == request_id) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }

    result->type = AIVM_VAL_VOID;
    return AIVM_SYSCALL_ERR_INVALID;
}

static int native_syscall_remote_call(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    const char* cap;
    const char* op;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 3U ||
        args == NULL ||
        args[0].type != AIVM_VAL_STRING ||
        args[1].type != AIVM_VAL_STRING ||
        args[2].type != AIVM_VAL_INT ||
        args[0].string_value == NULL ||
        args[1].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    cap = args[0].string_value;
    op = args[1].string_value;
    if (strlen(cap) > 64U || strlen(op) > 64U) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!remote_token_authorized()) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    return remote_session_invoke_call(cap, op, args[2].int_value, result);
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

static int native_syscall_net_tcp_listen(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    struct sockaddr_in addr;
    NativeSocket socket_fd;
    int64_t handle;
    int reuse = 1;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_net_platform_init() ||
        args[1].int_value <= 0 || args[1].int_value > 65535 ||
        !native_net_parse_ipv4(args[0].string_value, (uint16_t)args[1].int_value, &addr)) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    socket_fd = socket(AF_INET, SOCK_STREAM, 0);
    if (socket_fd == NATIVE_INVALID_SOCKET) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    (void)setsockopt(socket_fd, SOL_SOCKET, SO_REUSEADDR, (const char*)&reuse, sizeof(reuse));
    if (bind(socket_fd, (struct sockaddr*)&addr, sizeof(addr)) != 0 || listen(socket_fd, 16) != 0) {
        native_socket_close(socket_fd);
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    handle = native_net_handle_allocate(NATIVE_NET_HANDLE_KIND_TCP_LISTENER, socket_fd);
    if (handle < 0) {
        native_socket_close(socket_fd);
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_int(handle);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_tcp_accept(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetHandleState* listener;
    NativeSocket accepted;
    int64_t handle;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    listener = native_net_handle_lookup(args[0].int_value);
    if (listener == NULL || listener->kind != NATIVE_NET_HANDLE_KIND_TCP_LISTENER) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    accepted = accept(listener->socket, NULL, NULL);
    if (accepted == NATIVE_INVALID_SOCKET) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    handle = native_net_handle_allocate(NATIVE_NET_HANDLE_KIND_TCP_STREAM, accepted);
    if (handle < 0) {
        native_socket_close(accepted);
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_int(handle);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_tcp_connect(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    struct sockaddr_in addr;
    NativeSocket socket_fd;
    int64_t handle;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_net_platform_init() ||
        args[1].int_value <= 0 || args[1].int_value > 65535 ||
        !native_net_parse_ipv4(args[0].string_value, (uint16_t)args[1].int_value, &addr)) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    socket_fd = socket(AF_INET, SOCK_STREAM, 0);
    if (socket_fd == NATIVE_INVALID_SOCKET) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    if (connect(socket_fd, (struct sockaddr*)&addr, sizeof(addr)) != 0) {
        native_socket_close(socket_fd);
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    handle = native_net_handle_allocate(NATIVE_NET_HANDLE_KIND_TCP_STREAM, socket_fd);
    if (handle < 0) {
        native_socket_close(socket_fd);
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_int(handle);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_tcp_connect_tls(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    result->type = AIVM_VAL_VOID;
    return AIVM_SYSCALL_ERR_INVALID;
}

static int native_syscall_net_tcp_listen_tls(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 4U ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_INT ||
        args[2].type != AIVM_VAL_STRING || args[2].string_value == NULL ||
        args[3].type != AIVM_VAL_STRING || args[3].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    result->type = AIVM_VAL_VOID;
    return AIVM_SYSCALL_ERR_INVALID;
}

static int native_syscall_net_tcp_connect_tls_start(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    result->type = AIVM_VAL_VOID;
    return AIVM_SYSCALL_ERR_INVALID;
}

static int native_syscall_net_tcp_read(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetHandleState* state;
    int read_count;
    int max_bytes;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U || args[0].type != AIVM_VAL_INT || args[1].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    state = native_net_handle_lookup(args[0].int_value);
    if (state == NULL || state->kind != NATIVE_NET_HANDLE_KIND_TCP_STREAM) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
    max_bytes = (int)args[1].int_value;
    if (max_bytes <= 0) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
    if ((size_t)max_bytes > NATIVE_NET_BYTES_CHUNK) {
        max_bytes = (int)NATIVE_NET_BYTES_CHUNK;
    }
#ifdef _WIN32
    read_count = recv(state->socket, (char*)g_native_net_bytes_scratch, max_bytes, 0);
#else
    read_count = (int)recv(state->socket, g_native_net_bytes_scratch, (size_t)max_bytes, 0);
#endif
    if (read_count <= 0) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_bytes(g_native_net_bytes_scratch, (size_t)read_count);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_tcp_write(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetHandleState* state;
    int wrote;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U ||
        args[0].type != AIVM_VAL_INT || args[1].type != AIVM_VAL_BYTES) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    state = native_net_handle_lookup(args[0].int_value);
    if (state == NULL || state->kind != NATIVE_NET_HANDLE_KIND_TCP_STREAM) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    if (args[1].bytes_value.length == 0U || args[1].bytes_value.data == NULL) {
        *result = aivm_value_int(0);
        return AIVM_SYSCALL_OK;
    }
#ifdef _WIN32
    wrote = send(state->socket, (const char*)args[1].bytes_value.data, (int)args[1].bytes_value.length, 0);
#else
    wrote = (int)send(state->socket, args[1].bytes_value.data, args[1].bytes_value.length, 0);
#endif
    *result = aivm_value_int((wrote < 0) ? -1 : (int64_t)wrote);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_tcp_close(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    (void)native_net_handle_close(args[0].int_value);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_udp_bind(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    struct sockaddr_in addr;
    NativeSocket socket_fd;
    int64_t handle;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_net_platform_init() ||
        args[1].int_value <= 0 || args[1].int_value > 65535 ||
        !native_net_parse_ipv4(args[0].string_value, (uint16_t)args[1].int_value, &addr)) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    socket_fd = socket(AF_INET, SOCK_DGRAM, 0);
    if (socket_fd == NATIVE_INVALID_SOCKET) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    if (bind(socket_fd, (struct sockaddr*)&addr, sizeof(addr)) != 0) {
        native_socket_close(socket_fd);
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    handle = native_net_handle_allocate(NATIVE_NET_HANDLE_KIND_UDP_SOCKET, socket_fd);
    if (handle < 0) {
        native_socket_close(socket_fd);
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_int(handle);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_udp_send(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetHandleState* state;
    struct sockaddr_in addr;
    int wrote;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 4U ||
        args[0].type != AIVM_VAL_INT ||
        args[1].type != AIVM_VAL_STRING || args[1].string_value == NULL ||
        args[2].type != AIVM_VAL_INT ||
        args[3].type != AIVM_VAL_BYTES) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    state = native_net_handle_lookup(args[0].int_value);
    if (state == NULL || state->kind != NATIVE_NET_HANDLE_KIND_UDP_SOCKET ||
        args[2].int_value <= 0 || args[2].int_value > 65535 ||
        !native_net_parse_ipv4(args[1].string_value, (uint16_t)args[2].int_value, &addr)) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
#ifdef _WIN32
    wrote = sendto(
        state->socket,
        (const char*)args[3].bytes_value.data,
        (int)args[3].bytes_value.length,
        0,
        (struct sockaddr*)&addr,
        (int)sizeof(addr));
#else
    wrote = (int)sendto(
        state->socket,
        args[3].bytes_value.data,
        args[3].bytes_value.length,
        0,
        (struct sockaddr*)&addr,
        sizeof(addr));
#endif
    *result = aivm_value_int((wrote < 0) ? -1 : (int64_t)wrote);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_udp_recv(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetHandleState* state;
    struct sockaddr_in peer_addr;
    socklen_t peer_len = (socklen_t)sizeof(peer_addr);
    int read_count;
    int max_bytes;
    int64_t node_handle;
    AivmNodeAttr attrs[4];
    uint32_t ip;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U || args[0].type != AIVM_VAL_INT || args[1].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (g_native_active_vm == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    state = native_net_handle_lookup(args[0].int_value);
    if (state == NULL || state->kind != NATIVE_NET_HANDLE_KIND_UDP_SOCKET) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    max_bytes = (int)args[1].int_value;
    if (max_bytes <= 0) {
        max_bytes = 1;
    }
    if ((size_t)max_bytes > NATIVE_NET_BYTES_CHUNK) {
        max_bytes = (int)NATIVE_NET_BYTES_CHUNK;
    }
    memset(&peer_addr, 0, sizeof(peer_addr));
#ifdef _WIN32
    read_count = recvfrom(
        state->socket,
        (char*)g_native_net_bytes_scratch,
        max_bytes,
        0,
        (struct sockaddr*)&peer_addr,
        (int*)&peer_len);
#else
    read_count = (int)recvfrom(
        state->socket,
        g_native_net_bytes_scratch,
        (size_t)max_bytes,
        0,
        (struct sockaddr*)&peer_addr,
        &peer_len);
#endif
    if (read_count < 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    ip = ntohl(peer_addr.sin_addr.s_addr);
    (void)snprintf(
        g_native_net_host_scratch,
        sizeof(g_native_net_host_scratch),
        "%u.%u.%u.%u",
        (unsigned)((ip >> 24) & 0xFFU),
        (unsigned)((ip >> 16) & 0xFFU),
        (unsigned)((ip >> 8) & 0xFFU),
        (unsigned)(ip & 0xFFU));
    (void)snprintf(
        g_native_net_text_scratch,
        sizeof(g_native_net_text_scratch),
        "%u.%u.%u.%u",
        (unsigned)((ip >> 24) & 0xFFU),
        (unsigned)((ip >> 16) & 0xFFU),
        (unsigned)((ip >> 8) & 0xFFU),
        (unsigned)(ip & 0xFFU));
    attrs[0].key = "host";
    attrs[0].kind = AIVM_NODE_ATTR_STRING;
    attrs[0].string_value = g_native_net_host_scratch;
    attrs[1].key = "port";
    attrs[1].kind = AIVM_NODE_ATTR_INT;
    attrs[1].int_value = (int64_t)ntohs(peer_addr.sin_port);
    attrs[2].key = "length";
    attrs[2].kind = AIVM_NODE_ATTR_INT;
    attrs[2].int_value = (int64_t)read_count;
    if (!native_bytes_to_base64(g_native_net_bytes_scratch, (size_t)read_count, g_native_net_text_scratch, sizeof(g_native_net_text_scratch))) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    attrs[3].key = "dataBase64";
    attrs[3].kind = AIVM_NODE_ATTR_STRING;
    attrs[3].string_value = g_native_net_text_scratch;
    if (!native_vm_append_host_node(g_native_active_vm, "Map", "net_udp_recv", attrs, 4U, &node_handle)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_node(node_handle);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_async_poll(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetAsyncState* op;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    op = native_net_async_lookup(args[0].int_value);
    *result = aivm_value_int((op == NULL) ? -3 : (int64_t)op->status);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_async_cancel(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetAsyncState* op;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    op = native_net_async_lookup(args[0].int_value);
    if (op == NULL || op->status != 0) {
        *result = aivm_value_bool(0);
        return AIVM_SYSCALL_OK;
    }
    op->status = -2;
    *result = aivm_value_bool(1);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_async_await(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetAsyncState* op;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    op = native_net_async_lookup(args[0].int_value);
    if (op == NULL) {
        *result = aivm_value_int(-3);
        return AIVM_SYSCALL_OK;
    }
    while (op->status == 0) {
#ifdef _WIN32
        Sleep(1);
#else
        usleep(1000);
#endif
    }
    *result = aivm_value_int((int64_t)op->status);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_async_result_int(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetAsyncState* op;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    op = native_net_async_lookup(args[0].int_value);
    *result = aivm_value_int((op == NULL || op->status != 1) ? 0 : op->result_int);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_async_result_bytes(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetAsyncState* op;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    op = native_net_async_lookup(args[0].int_value);
    if (op == NULL || op->status != 1 || op->result_bytes == NULL || op->result_bytes_len == 0U) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_bytes(op->result_bytes, op->result_bytes_len);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_async_error(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeNetAsyncState* op;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    op = native_net_async_lookup(args[0].int_value);
    *result = aivm_value_string((op == NULL || op->status != -1) ? "" : op->error);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_net_start_op(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    int is_supported_target;
    int64_t op_handle;
    NativeNetAsyncState* op;
    AivmValue inner_result = aivm_value_void();
    int rc = AIVM_SYSCALL_ERR_INVALID;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    is_supported_target =
        strcmp(target, "sys.net.tcp.connectStart") == 0 ||
        strcmp(target, "sys.net.tcp.readStart") == 0 ||
        strcmp(target, "sys.net.tcp.writeStart") == 0;
    if (!is_supported_target) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    op_handle = native_net_async_allocate();
    if (op_handle < 0) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    op = native_net_async_lookup(op_handle);
    if (op == NULL) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    if (strcmp(target, "sys.net.tcp.connectStart") == 0) {
        rc = native_syscall_net_tcp_connect("sys.net.tcp.connect", args, arg_count, &inner_result);
        if (rc == AIVM_SYSCALL_OK && inner_result.type == AIVM_VAL_INT && inner_result.int_value >= 0) {
            native_net_async_set_success_int(op, inner_result.int_value);
        } else {
            native_net_async_set_failure(op, "connect_failed");
        }
    } else if (strcmp(target, "sys.net.tcp.readStart") == 0) {
        rc = native_syscall_net_tcp_read("sys.net.tcp.read", args, arg_count, &inner_result);
        if (rc == AIVM_SYSCALL_OK && inner_result.type == AIVM_VAL_BYTES) {
            native_net_async_set_success_bytes(op, inner_result.bytes_value.data, inner_result.bytes_value.length);
        } else {
            native_net_async_set_failure(op, "read_failed");
        }
    } else if (strcmp(target, "sys.net.tcp.writeStart") == 0) {
        rc = native_syscall_net_tcp_write("sys.net.tcp.write", args, arg_count, &inner_result);
        if (rc == AIVM_SYSCALL_OK && inner_result.type == AIVM_VAL_INT && inner_result.int_value >= 0) {
            native_net_async_set_success_int(op, inner_result.int_value);
        } else {
            native_net_async_set_failure(op, "write_failed");
        }
    }
    *result = aivm_value_int(op_handle);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_worker_start(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    int64_t handle;
    NativeWorkerState* worker;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 2U || args == NULL ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_STRING || args[1].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    handle = native_worker_allocate_slot();
    if (handle < 0) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    worker = native_worker_lookup(handle);
    if (worker == NULL) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    native_worker_start_task(worker, args[0].string_value, args[1].string_value);
    *result = aivm_value_int(handle);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_worker_poll(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeWorkerState* worker;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    worker = native_worker_lookup(args[0].int_value);
    if (worker == NULL) {
        *result = aivm_value_int(-3);
        return AIVM_SYSCALL_OK;
    }
    if (worker->status == 0) {
        if (worker->polls_remaining > 0) {
            worker->polls_remaining -= 1;
        }
        if (worker->polls_remaining <= 0) {
            worker->status = worker->completion_status;
        }
    }
    *result = aivm_value_int((int64_t)worker->status);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_worker_result(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeWorkerState* worker;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    worker = native_worker_lookup(args[0].int_value);
    if (worker == NULL || worker->status != 1) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_string(worker->result);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_worker_error(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeWorkerState* worker;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    worker = native_worker_lookup(args[0].int_value);
    if (worker == NULL) {
        *result = aivm_value_string("unknown_worker");
        return AIVM_SYSCALL_OK;
    }
    if (worker->status == -1 || worker->status == -2) {
        *result = aivm_value_string(worker->error);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_string("");
    return AIVM_SYSCALL_OK;
}

static int native_syscall_worker_cancel(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeWorkerState* worker;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    worker = native_worker_lookup(args[0].int_value);
    if (worker == NULL || worker->status != 0) {
        *result = aivm_value_bool(0);
        return AIVM_SYSCALL_OK;
    }
    worker->status = -2;
    worker->polls_remaining = 0;
    worker->completion_status = -2;
    (void)snprintf(worker->error, sizeof(worker->error), "canceled");
    *result = aivm_value_bool(1);
    return AIVM_SYSCALL_OK;
}

static AivmVm* g_native_active_vm = NULL;
static char g_native_ui_event_type[16] = "none";
static char g_native_ui_event_key[48] = "";
static char g_native_ui_event_text[128] = "";
static char g_native_ui_event_target_id[48] = "";
static int64_t g_native_ui_active_window_handles[8];
static const char* g_native_debug_mode = "off";
static int g_native_scene_capture_enabled = 0;
static FILE* g_native_scene_capture_file = NULL;
static int64_t g_native_scene_frame_id = 0;
static int64_t g_native_scene_frame_node_count = 0;
static int g_native_scene_frame_open = 0;

static void native_scene_capture_close(void)
{
    if (g_native_scene_capture_file != NULL) {
        fclose(g_native_scene_capture_file);
        g_native_scene_capture_file = NULL;
    }
}

static void native_scene_capture_reset(void)
{
    g_native_scene_capture_enabled = 0;
    g_native_scene_frame_id = 0;
    g_native_scene_frame_node_count = 0;
    g_native_scene_frame_open = 0;
    native_scene_capture_close();
}

static int native_scene_capture_escape(const char* text, char* out, size_t out_capacity)
{
    size_t i = 0U;
    size_t j = 0U;
    if (out == NULL || out_capacity == 0U) {
        return 0;
    }
    if (text == NULL) {
        out[0] = '\0';
        return 1;
    }
    while (text[i] != '\0' && j + 1U < out_capacity) {
        char ch = text[i];
        if ((ch == '\\' || ch == '"') && j + 2U < out_capacity) {
            out[j++] = '\\';
            out[j++] = ch;
        } else if (ch == '\n' && j + 2U < out_capacity) {
            out[j++] = '\\';
            out[j++] = 'n';
        } else if (ch == '\r' && j + 2U < out_capacity) {
            out[j++] = '\\';
            out[j++] = 'r';
        } else {
            out[j++] = ch;
        }
        i += 1U;
    }
    out[j] = '\0';
    return 1;
}

static void native_scene_capture_emit_line(const char* line)
{
    if (line == NULL || line[0] == '\0') {
        return;
    }
    fprintf(stdout, "%s\n", line);
    fflush(stdout);
    if (g_native_scene_capture_file != NULL) {
        fprintf(g_native_scene_capture_file, "%s\n", line);
        fflush(g_native_scene_capture_file);
    }
}

static void native_scene_capture_begin_frame(int64_t handle, int width, int height)
{
    char line[256];
    if (!g_native_scene_capture_enabled) {
        return;
    }
    if (width < 64) {
        width = 800;
    }
    if (height < 64) {
        height = 600;
    }
    g_native_scene_frame_id += 1;
    g_native_scene_frame_node_count = 0;
    g_native_scene_frame_open = 1;
    (void)snprintf(
        line,
        sizeof(line),
        "aivectra.frame = { id=%lld, width=%d, height=%d, hash=\"native-%lld-%lld\" }",
        (long long)g_native_scene_frame_id,
        width,
        height,
        (long long)handle,
        (long long)g_native_scene_frame_id);
    native_scene_capture_emit_line(line);
}

static void native_scene_capture_end_frame(void)
{
    char line[128];
    if (!g_native_scene_capture_enabled || !g_native_scene_frame_open) {
        return;
    }
    (void)snprintf(
        line,
        sizeof(line),
        "aivectra.frame_end = { id=%lld, node_count=%lld }",
        (long long)g_native_scene_frame_id,
        (long long)g_native_scene_frame_node_count);
    native_scene_capture_emit_line(line);
    g_native_scene_frame_open = 0;
}

static void native_scene_capture_emit_node(
    const char* kind,
    const char* fill,
    const char* stroke,
    int stroke_width,
    const char* text_value,
    const char* path_value,
    int font_size,
    int x,
    int y,
    int w,
    int h)
{
    char line[1536];
    char id[64];
    char fill_escaped[128];
    char stroke_escaped[128];
    char text_escaped[512];
    char path_escaped[512];
    if (!g_native_scene_capture_enabled || !g_native_scene_frame_open || kind == NULL) {
        return;
    }
    g_native_scene_frame_node_count += 1;
    (void)snprintf(
        id,
        sizeof(id),
        "n%lld_%lld",
        (long long)g_native_scene_frame_id,
        (long long)g_native_scene_frame_node_count);
    (void)native_scene_capture_escape(fill == NULL ? "" : fill, fill_escaped, sizeof(fill_escaped));
    (void)native_scene_capture_escape(stroke == NULL ? "" : stroke, stroke_escaped, sizeof(stroke_escaped));
    (void)native_scene_capture_escape(text_value == NULL ? "" : text_value, text_escaped, sizeof(text_escaped));
    (void)native_scene_capture_escape(path_value == NULL ? "" : path_value, path_escaped, sizeof(path_escaped));
    (void)snprintf(
        line,
        sizeof(line),
        "aivectra.node = { frame=%lld, order=%lld, id=\"%s\", parent=\"\", kind=\"%s\", transform=\"matrix(1,0,0,1,0,0)\", fill=\"%s\", stroke=\"%s\", stroke_width=%d, text=\"%s\", path=\"%s\", font_size=%d, x=%d, y=%d, w=%d, h=%d }",
        (long long)g_native_scene_frame_id,
        (long long)g_native_scene_frame_node_count,
        id,
        kind,
        fill_escaped,
        stroke_escaped,
        stroke_width,
        text_escaped,
        path_escaped,
        font_size,
        x,
        y,
        w,
        h);
    native_scene_capture_emit_line(line);
}

static void native_scene_capture_configure(const char* debug_mode, const char* out_dir)
{
    char scene_log_path[PATH_MAX];
    native_scene_capture_reset();
    g_native_debug_mode = "off";
    if (debug_mode == NULL || debug_mode[0] == '\0') {
        return;
    }
    g_native_debug_mode = debug_mode;
    if (!starts_with(debug_mode, "scene")) {
        return;
    }
    g_native_scene_capture_enabled = 1;
    if (out_dir == NULL || out_dir[0] == '\0') {
        return;
    }
    if (!ensure_directory_recursive(out_dir)) {
        g_native_scene_capture_enabled = 0;
        return;
    }
    if (!join_path(out_dir, "scene.log", scene_log_path, sizeof(scene_log_path))) {
        g_native_scene_capture_enabled = 0;
        return;
    }
    g_native_scene_capture_file = fopen(scene_log_path, "wb");
    if (g_native_scene_capture_file == NULL) {
        g_native_scene_capture_enabled = 0;
    }
}

enum {
    NATIVE_UI_EVENT_ATTR_TYPE = 0,
    NATIVE_UI_EVENT_ATTR_X = 1,
    NATIVE_UI_EVENT_ATTR_Y = 2,
    NATIVE_UI_EVENT_ATTR_KEY = 3,
    NATIVE_UI_EVENT_ATTR_TEXT = 4,
    NATIVE_UI_EVENT_ATTR_TARGET_ID = 5,
    NATIVE_UI_EVENT_ATTR_MODIFIERS = 6,
    NATIVE_UI_EVENT_ATTR_REPEAT = 7
};

static int native_vm_append_host_node(
    AivmVm* vm,
    const char* kind,
    const char* id,
    const AivmNodeAttr* attrs,
    size_t attr_count,
    int64_t* out_handle)
{
    AivmNodeRecord* node;
    size_t i;
    size_t node_index;
    if (vm == NULL || kind == NULL || id == NULL || out_handle == NULL) {
        return 0;
    }
    if (vm->node_count >= AIVM_VM_NODE_CAPACITY || vm->node_attr_count + attr_count > AIVM_VM_NODE_ATTR_CAPACITY) {
        return 0;
    }
    node_index = vm->node_count;
    node = &vm->nodes[node_index];
    node->kind = kind;
    node->id = id;
    node->attr_start = vm->node_attr_count;
    node->attr_count = attr_count;
    node->child_start = vm->node_child_count;
    node->child_count = 0U;
    for (i = 0U; i < attr_count; i += 1U) {
        vm->node_attrs[vm->node_attr_count + i] = attrs[i];
    }
    vm->node_count += 1U;
    vm->node_attr_count += attr_count;
    if (vm->node_count > vm->node_high_water) {
        vm->node_high_water = vm->node_count;
    }
    if (vm->node_attr_count > vm->node_attr_high_water) {
        vm->node_attr_high_water = vm->node_attr_count;
    }
    *out_handle = (int64_t)vm->node_count;
    return 1;
}

static int native_fs_dir_count_entries(const char* path, int64_t* out_count)
{
    int64_t count = 0;
    if (path == NULL || out_count == NULL) {
        return 0;
    }
#ifdef _WIN32
    {
        char pattern[PATH_MAX];
        WIN32_FIND_DATAA entry;
        HANDLE find_handle;
        if (!join_path(path, "*", pattern, sizeof(pattern))) {
            return 0;
        }
        find_handle = FindFirstFileA(pattern, &entry);
        if (find_handle == INVALID_HANDLE_VALUE) {
            return 0;
        }
        do {
            if (strcmp(entry.cFileName, ".") == 0 || strcmp(entry.cFileName, "..") == 0) {
                continue;
            }
            count += 1;
        } while (FindNextFileA(find_handle, &entry) != 0);
        FindClose(find_handle);
    }
#else
    {
        DIR* dir;
        struct dirent* entry;
        dir = opendir(path);
        if (dir == NULL) {
            return 0;
        }
        while ((entry = readdir(dir)) != NULL) {
            if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0) {
                continue;
            }
            count += 1;
        }
        (void)closedir(dir);
    }
#endif
    *out_count = count;
    return 1;
}

static int native_syscall_fs_dir_list(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    AivmNodeAttr attrs[3];
    int64_t node_handle;
    int64_t count = 0;
    int exists = 0;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (g_native_active_vm == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (directory_exists(args[0].string_value) && native_fs_dir_count_entries(args[0].string_value, &count)) {
        exists = 1;
    }
    attrs[0].key = "path";
    attrs[0].kind = AIVM_NODE_ATTR_STRING;
    attrs[0].string_value = args[0].string_value;
    attrs[1].key = "exists";
    attrs[1].kind = AIVM_NODE_ATTR_BOOL;
    attrs[1].bool_value = exists;
    attrs[2].key = "count";
    attrs[2].kind = AIVM_NODE_ATTR_INT;
    attrs[2].int_value = count;
    if (!native_vm_append_host_node(g_native_active_vm, "Map", "fs_dir_list", attrs, 3U, &node_handle)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_node(node_handle);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_fs_path_stat(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    struct stat st;
    AivmNodeAttr attrs[5];
    int64_t node_handle;
    int exists = 0;
    int is_dir = 0;
    int64_t size = 0;
    int64_t mtime_unix_ms = 0;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (g_native_active_vm == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (stat(args[0].string_value, &st) == 0) {
        exists = 1;
#ifdef _WIN32
        is_dir = ((st.st_mode & _S_IFDIR) != 0) ? 1 : 0;
#else
        is_dir = S_ISDIR(st.st_mode) ? 1 : 0;
#endif
        size = (int64_t)st.st_size;
        mtime_unix_ms = (int64_t)st.st_mtime * 1000LL;
    }
    attrs[0].key = "path";
    attrs[0].kind = AIVM_NODE_ATTR_STRING;
    attrs[0].string_value = args[0].string_value;
    attrs[1].key = "exists";
    attrs[1].kind = AIVM_NODE_ATTR_BOOL;
    attrs[1].bool_value = exists;
    attrs[2].key = "isDir";
    attrs[2].kind = AIVM_NODE_ATTR_BOOL;
    attrs[2].bool_value = is_dir;
    attrs[3].key = "size";
    attrs[3].kind = AIVM_NODE_ATTR_INT;
    attrs[3].int_value = size;
    attrs[4].key = "mtimeUnixMs";
    attrs[4].kind = AIVM_NODE_ATTR_INT;
    attrs[4].int_value = mtime_unix_ms;
    if (!native_vm_append_host_node(g_native_active_vm, "Map", "fs_path_stat", attrs, 5U, &node_handle)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_node(node_handle);
    return AIVM_SYSCALL_OK;
}

static void native_ui_runtime_reset_handles(void)
{
    memset(g_native_ui_active_window_handles, 0, sizeof(g_native_ui_active_window_handles));
}

static int native_ui_runtime_is_active_handle(int64_t handle)
{
    size_t i;
    if (handle <= 0) {
        return 0;
    }
    for (i = 0U; i < sizeof(g_native_ui_active_window_handles) / sizeof(g_native_ui_active_window_handles[0]); i += 1U) {
        if (g_native_ui_active_window_handles[i] == handle) {
            return 1;
        }
    }
    return 0;
}

static int native_ui_runtime_register_handle(int64_t handle)
{
    size_t i;
    if (handle <= 0) {
        return 0;
    }
    for (i = 0U; i < sizeof(g_native_ui_active_window_handles) / sizeof(g_native_ui_active_window_handles[0]); i += 1U) {
        if (g_native_ui_active_window_handles[i] == handle) {
            return 1;
        }
    }
    for (i = 0U; i < sizeof(g_native_ui_active_window_handles) / sizeof(g_native_ui_active_window_handles[0]); i += 1U) {
        if (g_native_ui_active_window_handles[i] == 0) {
            g_native_ui_active_window_handles[i] = handle;
            return 1;
        }
    }
    return 0;
}

static void native_ui_runtime_unregister_handle(int64_t handle)
{
    size_t i;
    if (handle <= 0) {
        return;
    }
    for (i = 0U; i < sizeof(g_native_ui_active_window_handles) / sizeof(g_native_ui_active_window_handles[0]); i += 1U) {
        if (g_native_ui_active_window_handles[i] == handle) {
            g_native_ui_active_window_handles[i] = 0;
            return;
        }
    }
}

static int native_ui_ensure_event_node(AivmVm* vm, int64_t* out_handle)
{
    AivmNodeAttr attrs[8];
    if (out_handle == NULL) {
        return 0;
    }
    if (vm->ui_empty_event_node_handle > 0 &&
        vm->ui_empty_event_node_handle <= (int64_t)vm->node_count) {
        *out_handle = vm->ui_empty_event_node_handle;
        return 1;
    }
    attrs[0].key = "type";
    attrs[0].kind = AIVM_NODE_ATTR_STRING;
    attrs[0].string_value = g_native_ui_event_type;
    attrs[1].key = "x";
    attrs[1].kind = AIVM_NODE_ATTR_INT;
    attrs[1].int_value = 0;
    attrs[2].key = "y";
    attrs[2].kind = AIVM_NODE_ATTR_INT;
    attrs[2].int_value = 0;
    attrs[3].key = "key";
    attrs[3].kind = AIVM_NODE_ATTR_STRING;
    attrs[3].string_value = g_native_ui_event_key;
    attrs[4].key = "text";
    attrs[4].kind = AIVM_NODE_ATTR_STRING;
    attrs[4].string_value = g_native_ui_event_text;
    attrs[5].key = "targetId";
    attrs[5].kind = AIVM_NODE_ATTR_STRING;
    attrs[5].string_value = g_native_ui_event_target_id;
    attrs[6].key = "modifiers";
    attrs[6].kind = AIVM_NODE_ATTR_INT;
    attrs[6].int_value = 0;
    attrs[7].key = "repeat";
    attrs[7].kind = AIVM_NODE_ATTR_BOOL;
    attrs[7].bool_value = 0;
    if (!native_vm_append_host_node(vm, "Map", "ui_event", attrs, 8U, &vm->ui_empty_event_node_handle)) {
        return 0;
    }
    *out_handle = vm->ui_empty_event_node_handle;
    return 1;
}

static int native_ui_ensure_size_node(AivmVm* vm, int64_t* out_handle)
{
    AivmNodeAttr attrs[2];
    if (out_handle == NULL) {
        return 0;
    }
    if (vm->ui_default_window_size_node_handle > 0 &&
        vm->ui_default_window_size_node_handle <= (int64_t)vm->node_count) {
        *out_handle = vm->ui_default_window_size_node_handle;
        return 1;
    }
    attrs[0].key = "width";
    attrs[0].kind = AIVM_NODE_ATTR_INT;
    attrs[0].int_value = 0;
    attrs[1].key = "height";
    attrs[1].kind = AIVM_NODE_ATTR_INT;
    attrs[1].int_value = 0;
    if (!native_vm_append_host_node(vm, "Map", "ui_size", attrs, 2U, &vm->ui_default_window_size_node_handle)) {
        return 0;
    }
    *out_handle = vm->ui_default_window_size_node_handle;
    return 1;
}

static void native_ui_update_size_node(AivmVm* vm, int width, int height)
{
    const AivmNodeRecord* node;
    if (vm == NULL || vm->ui_default_window_size_node_handle <= 0 ||
        vm->ui_default_window_size_node_handle > (int64_t)vm->node_count) {
        return;
    }
    node = &vm->nodes[(size_t)(vm->ui_default_window_size_node_handle - 1)];
    if (node->attr_count < 2U || node->attr_start + 1U >= vm->node_attr_count) {
        return;
    }
    vm->node_attrs[node->attr_start].int_value = width;
    vm->node_attrs[node->attr_start + 1U].int_value = height;
}

static void native_ui_update_event_node(AivmVm* vm, const NativeHostUiEvent* event)
{
    const AivmNodeRecord* node;
    size_t base;
    if (vm == NULL || event == NULL ||
        vm->ui_empty_event_node_handle <= 0 ||
        vm->ui_empty_event_node_handle > (int64_t)vm->node_count) {
        return;
    }
    (void)snprintf(g_native_ui_event_type, sizeof(g_native_ui_event_type), "%s", event->type);
    (void)snprintf(g_native_ui_event_key, sizeof(g_native_ui_event_key), "%s", event->key);
    (void)snprintf(g_native_ui_event_text, sizeof(g_native_ui_event_text), "%s", event->text);
    node = &vm->nodes[(size_t)(vm->ui_empty_event_node_handle - 1)];
    if (node->attr_count < 8U || node->attr_start + 7U >= vm->node_attr_count) {
        return;
    }
    base = node->attr_start;
    vm->node_attrs[base + NATIVE_UI_EVENT_ATTR_TYPE].string_value = g_native_ui_event_type;
    vm->node_attrs[base + NATIVE_UI_EVENT_ATTR_X].int_value = event->x;
    vm->node_attrs[base + NATIVE_UI_EVENT_ATTR_Y].int_value = event->y;
    vm->node_attrs[base + NATIVE_UI_EVENT_ATTR_KEY].string_value = g_native_ui_event_key;
    vm->node_attrs[base + NATIVE_UI_EVENT_ATTR_TEXT].string_value = g_native_ui_event_text;
    vm->node_attrs[base + NATIVE_UI_EVENT_ATTR_TARGET_ID].string_value = g_native_ui_event_target_id;
    vm->node_attrs[base + NATIVE_UI_EVENT_ATTR_MODIFIERS].int_value = event->modifiers;
    vm->node_attrs[base + NATIVE_UI_EVENT_ATTR_REPEAT].bool_value = event->repeat ? 1 : 0;
}

static int native_syscall_ui_create_window(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 3U ||
        args[0].type != AIVM_VAL_STRING ||
        args[1].type != AIVM_VAL_INT ||
        args[2].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    {
        int64_t handle = 0;
        if (!native_host_ui_create_window(args[0].string_value, (int)args[1].int_value, (int)args[2].int_value, &handle)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        if (!native_ui_runtime_register_handle(handle)) {
            (void)native_host_ui_close_window(handle);
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        *result = aivm_value_int(handle);
    }
    return AIVM_SYSCALL_OK;
}

static int native_syscall_ui_void_1(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_ui_runtime_is_active_handle(args[0].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (strcmp(target, "sys.ui.closeWindow") == 0) {
        if (!native_host_ui_close_window(args[0].int_value)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        native_ui_runtime_unregister_handle(args[0].int_value);
    } else if (strcmp(target, "sys.ui.waitFrame") == 0) {
        if (!native_host_ui_wait_frame(args[0].int_value)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
    } else if (strcmp(target, "sys.ui.beginFrame") == 0) {
        if (!native_host_ui_begin_frame(args[0].int_value)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        {
            int width = 0;
            int height = 0;
            if (native_host_ui_get_window_size(args[0].int_value, &width, &height)) {
                native_scene_capture_begin_frame(args[0].int_value, width, height);
            } else {
                native_scene_capture_begin_frame(args[0].int_value, 800, 600);
            }
        }
    } else if (strcmp(target, "sys.ui.endFrame") == 0) {
        if (!native_host_ui_end_frame(args[0].int_value)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        native_scene_capture_end_frame();
    } else if (strcmp(target, "sys.ui.present") == 0) {
        if (!native_host_ui_present(args[0].int_value)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
    } else {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_ui_draw_rect(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (target == NULL || strcmp(target, "sys.ui.drawRect") != 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    if (args == NULL || arg_count != 6U ||
        args[0].type != AIVM_VAL_INT || args[1].type != AIVM_VAL_INT || args[2].type != AIVM_VAL_INT ||
        args[3].type != AIVM_VAL_INT || args[4].type != AIVM_VAL_INT || args[5].type != AIVM_VAL_STRING) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_ui_runtime_is_active_handle(args[0].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_host_ui_draw_rect(
            args[0].int_value,
            (int)args[1].int_value,
            (int)args[2].int_value,
            (int)args[3].int_value,
            (int)args[4].int_value,
            args[5].string_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    native_scene_capture_emit_node(
        "Rect",
        args[5].string_value,
        "",
        0,
        "",
        "",
        0,
        (int)args[1].int_value,
        (int)args[2].int_value,
        (int)args[3].int_value,
        (int)args[4].int_value);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_ui_draw_ellipse(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (target == NULL || strcmp(target, "sys.ui.drawEllipse") != 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    if (args == NULL || arg_count != 6U ||
        args[0].type != AIVM_VAL_INT || args[1].type != AIVM_VAL_INT || args[2].type != AIVM_VAL_INT ||
        args[3].type != AIVM_VAL_INT || args[4].type != AIVM_VAL_INT || args[5].type != AIVM_VAL_STRING) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_ui_runtime_is_active_handle(args[0].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_host_ui_draw_ellipse(
            args[0].int_value,
            (int)args[1].int_value,
            (int)args[2].int_value,
            (int)args[3].int_value,
            (int)args[4].int_value,
            args[5].string_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    native_scene_capture_emit_node(
        "Ellipse",
        args[5].string_value,
        "",
        0,
        "",
        "",
        0,
        (int)args[1].int_value,
        (int)args[2].int_value,
        (int)args[3].int_value,
        (int)args[4].int_value);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_ui_draw_image(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    uint8_t* rgba = NULL;
    size_t rgba_length = 0U;
    size_t expected_length = 0U;
    int width;
    int height;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (target == NULL || strcmp(target, "sys.ui.drawImage") != 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    if (args == NULL || arg_count != 6U ||
        args[0].type != AIVM_VAL_INT || args[1].type != AIVM_VAL_INT || args[2].type != AIVM_VAL_INT ||
        args[3].type != AIVM_VAL_INT || args[4].type != AIVM_VAL_INT || args[5].type != AIVM_VAL_STRING) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_ui_runtime_is_active_handle(args[0].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    width = (int)args[3].int_value;
    height = (int)args[4].int_value;
    if (width <= 0 || height <= 0) {
        native_scene_capture_emit_node(
            "Image",
            "",
            "",
            0,
            "",
            "",
            0,
            (int)args[1].int_value,
            (int)args[2].int_value,
            width,
            height);
        *result = aivm_value_void();
        return AIVM_SYSCALL_OK;
    }
    if ((size_t)width > (SIZE_MAX / (size_t)height) ||
        ((size_t)width * (size_t)height) > (SIZE_MAX / 4U)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    expected_length = (size_t)width * (size_t)height * 4U;
    if (!native_bytes_from_base64(args[5].string_value, NULL, 0U, &rgba_length) ||
        rgba_length != expected_length) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    rgba = (uint8_t*)malloc(rgba_length > 0U ? rgba_length : 1U);
    if (rgba == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_bytes_from_base64(args[5].string_value, rgba, rgba_length, &rgba_length) ||
        rgba_length != expected_length ||
        !native_host_ui_draw_image(
            args[0].int_value,
            (int)args[1].int_value,
            (int)args[2].int_value,
            width,
            height,
            rgba,
            rgba_length)) {
        free(rgba);
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    free(rgba);
    native_scene_capture_emit_node(
        "Image",
        "",
        "",
        0,
        "",
        "",
        0,
        (int)args[1].int_value,
        (int)args[2].int_value,
        width,
        height);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_ui_draw_text(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (target == NULL || strcmp(target, "sys.ui.drawText") != 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    if (args == NULL || arg_count != 6U ||
        args[0].type != AIVM_VAL_INT || args[1].type != AIVM_VAL_INT || args[2].type != AIVM_VAL_INT ||
        args[3].type != AIVM_VAL_STRING || args[4].type != AIVM_VAL_STRING || args[5].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_ui_runtime_is_active_handle(args[0].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_host_ui_draw_text(
            args[0].int_value,
            (int)args[1].int_value,
            (int)args[2].int_value,
            args[3].string_value,
            args[4].string_value,
            (int)args[5].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    native_scene_capture_emit_node(
        "Text",
        args[4].string_value,
        "",
        0,
        args[3].string_value,
        "",
        (int)args[5].int_value,
        (int)args[1].int_value,
        (int)args[2].int_value,
        0,
        0);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_ui_draw_line(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (target == NULL || strcmp(target, "sys.ui.drawLine") != 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    if (args == NULL || arg_count != 7U ||
        args[0].type != AIVM_VAL_INT || args[1].type != AIVM_VAL_INT || args[2].type != AIVM_VAL_INT ||
        args[3].type != AIVM_VAL_INT || args[4].type != AIVM_VAL_INT || args[5].type != AIVM_VAL_STRING ||
        args[6].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_ui_runtime_is_active_handle(args[0].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_host_ui_draw_line(
            args[0].int_value,
            (int)args[1].int_value,
            (int)args[2].int_value,
            (int)args[3].int_value,
            (int)args[4].int_value,
            args[5].string_value,
            (int)args[6].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    {
        char path_text[128];
        (void)snprintf(
            path_text,
            sizeof(path_text),
            "M%d,%d L%d,%d",
            (int)args[1].int_value,
            (int)args[2].int_value,
            (int)args[3].int_value,
            (int)args[4].int_value);
        native_scene_capture_emit_node(
            "Path",
            "",
            args[5].string_value,
            (int)args[6].int_value,
            "",
            path_text,
            0,
            0,
            0,
            0,
            0);
    }
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_ui_draw_path(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (target == NULL || strcmp(target, "sys.ui.drawPath") != 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    if (args == NULL || arg_count != 4U ||
        args[0].type != AIVM_VAL_INT || args[1].type != AIVM_VAL_STRING ||
        args[2].type != AIVM_VAL_STRING || args[3].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_ui_runtime_is_active_handle(args[0].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_host_ui_draw_path(
            args[0].int_value,
            args[1].string_value,
            args[2].string_value,
            (int)args[3].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    native_scene_capture_emit_node(
        "Path",
        "",
        args[2].string_value,
        (int)args[3].int_value,
        "",
        args[1].string_value,
        0,
        0,
        0,
        0,
        0);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_ui_poll_event(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (target == NULL || strcmp(target, "sys.ui.pollEvent") != 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_ui_runtime_is_active_handle(args[0].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    {
        NativeHostUiEvent event;
        int64_t node_handle;
        if (g_native_active_vm == NULL || !native_ui_ensure_event_node(g_native_active_vm, &node_handle)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        memset(&event, 0, sizeof(event));
        (void)snprintf(event.type, sizeof(event.type), "none");
        if (!native_host_ui_poll_event(args[0].int_value, &event)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        native_ui_update_event_node(g_native_active_vm, &event);
        *result = aivm_value_node(node_handle);
    }
    return AIVM_SYSCALL_OK;
}

static int native_syscall_ui_get_window_size(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (target == NULL || strcmp(target, "sys.ui.getWindowSize") != 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_NOT_FOUND;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_ui_runtime_is_active_handle(args[0].int_value)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (g_native_active_vm == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    {
        int width = 0;
        int height = 0;
        int64_t node_handle;
        if (!native_ui_ensure_size_node(g_native_active_vm, &node_handle)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        if (!native_host_ui_get_window_size(args[0].int_value, &width, &height)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        native_ui_update_size_node(g_native_active_vm, width, height);
        *result = aivm_value_node(node_handle);
    }
    return AIVM_SYSCALL_OK;
}

static int native_syscall_debug_mode(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args != NULL && arg_count != 0U) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    *result = aivm_value_string((g_native_debug_mode == NULL) ? "off" : g_native_debug_mode);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_debug_capture_frame_begin(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 3U ||
        args[0].type != AIVM_VAL_INT ||
        args[1].type != AIVM_VAL_INT ||
        args[2].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    native_scene_capture_begin_frame(args[0].int_value, (int)args[1].int_value, (int)args[2].int_value);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_debug_capture_draw(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 2U ||
        args[0].type != AIVM_VAL_STRING ||
        args[1].type != AIVM_VAL_STRING ||
        args[0].string_value == NULL ||
        args[1].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    native_scene_capture_emit_node(
        "Debug",
        "",
        "",
        0,
        args[0].string_value,
        args[1].string_value,
        0,
        0,
        0,
        0,
        0);
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
}

static int native_syscall_debug_capture_frame_end(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_INT) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    native_scene_capture_end_frame();
    *result = aivm_value_void();
    return AIVM_SYSCALL_OK;
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
static char* g_native_string_scratch = NULL;
static size_t g_native_string_scratch_capacity = 0U;

static int native_string_scratch_ensure_capacity(size_t required_capacity)
{
    char* grown;
    size_t new_capacity;
    if (required_capacity == 0U) {
        required_capacity = 1U;
    }
    if (required_capacity <= g_native_string_scratch_capacity && g_native_string_scratch != NULL) {
        return 1;
    }
    new_capacity = (g_native_string_scratch_capacity == 0U) ? 256U : g_native_string_scratch_capacity;
    while (new_capacity < required_capacity) {
        if (new_capacity > (SIZE_MAX / 2U)) {
            new_capacity = required_capacity;
            break;
        }
        new_capacity *= 2U;
    }
    grown = (char*)realloc(g_native_string_scratch, new_capacity);
    if (grown == NULL) {
        return 0;
    }
    g_native_string_scratch = grown;
    g_native_string_scratch_capacity = new_capacity;
    return 1;
}

static size_t native_utf8_next_index(const char* text, size_t index)
{
    unsigned char first;
    if (text == NULL) {
        return index;
    }
    first = (unsigned char)text[index];
    if (first == '\0') {
        return index;
    }
    if ((first & 0x80U) == 0U) {
        return index + 1U;
    }
    if ((first & 0xE0U) == 0xC0U &&
        text[index + 1U] != '\0' &&
        (((unsigned char)text[index + 1U]) & 0xC0U) == 0x80U) {
        return index + 2U;
    }
    if ((first & 0xF0U) == 0xE0U &&
        text[index + 1U] != '\0' &&
        text[index + 2U] != '\0' &&
        (((unsigned char)text[index + 1U]) & 0xC0U) == 0x80U &&
        (((unsigned char)text[index + 2U]) & 0xC0U) == 0x80U) {
        return index + 3U;
    }
    if ((first & 0xF8U) == 0xF0U &&
        text[index + 1U] != '\0' &&
        text[index + 2U] != '\0' &&
        text[index + 3U] != '\0' &&
        (((unsigned char)text[index + 1U]) & 0xC0U) == 0x80U &&
        (((unsigned char)text[index + 2U]) & 0xC0U) == 0x80U &&
        (((unsigned char)text[index + 3U]) & 0xC0U) == 0x80U) {
        return index + 4U;
    }
    return index + 1U;
}

static size_t native_utf8_rune_count(const char* text)
{
    size_t byte_index = 0U;
    size_t count = 0U;
    if (text == NULL) {
        return 0U;
    }
    while (text[byte_index] != '\0') {
        byte_index = native_utf8_next_index(text, byte_index);
        count += 1U;
    }
    return count;
}

static size_t native_utf8_byte_offset_for_rune(const char* text, size_t rune_index)
{
    size_t byte_index = 0U;
    size_t current_rune = 0U;
    if (text == NULL) {
        return 0U;
    }
    while (text[byte_index] != '\0' && current_rune < rune_index) {
        byte_index = native_utf8_next_index(text, byte_index);
        current_rune += 1U;
    }
    return byte_index;
}

static size_t native_clamp_rune_index(int64_t value, size_t max_value)
{
    if (value <= 0) {
        return 0U;
    }
    if ((uint64_t)value >= (uint64_t)max_value) {
        return max_value;
    }
    return (size_t)value;
}

static int64_t native_safe_add_i64(int64_t a, int64_t b)
{
    if (b > 0 && a > (INT64_MAX - b)) {
        return INT64_MAX;
    }
    if (b < 0 && a < (INT64_MIN - b)) {
        return INT64_MIN;
    }
    return a + b;
}

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

typedef struct {
    uint32_t state[5];
    uint64_t bit_count;
    uint8_t buffer[64];
    size_t buffer_used;
} NativeSha1Ctx;

typedef struct {
    uint32_t state[8];
    uint64_t bit_count;
    uint8_t buffer[64];
    size_t buffer_used;
} NativeSha256Ctx;

static uint32_t native_rotl32(uint32_t v, uint32_t n) { return (v << n) | (v >> (32U - n)); }
static uint32_t native_rotr32(uint32_t v, uint32_t n) { return (v >> n) | (v << (32U - n)); }

static uint32_t native_read_be32(const uint8_t* p)
{
    return ((uint32_t)p[0] << 24U) | ((uint32_t)p[1] << 16U) | ((uint32_t)p[2] << 8U) | (uint32_t)p[3];
}

static void native_write_be32(uint8_t* p, uint32_t v)
{
    p[0] = (uint8_t)((v >> 24U) & 0xFFU);
    p[1] = (uint8_t)((v >> 16U) & 0xFFU);
    p[2] = (uint8_t)((v >> 8U) & 0xFFU);
    p[3] = (uint8_t)(v & 0xFFU);
}

static void native_write_be64(uint8_t* p, uint64_t v)
{
    p[0] = (uint8_t)((v >> 56U) & 0xFFU);
    p[1] = (uint8_t)((v >> 48U) & 0xFFU);
    p[2] = (uint8_t)((v >> 40U) & 0xFFU);
    p[3] = (uint8_t)((v >> 32U) & 0xFFU);
    p[4] = (uint8_t)((v >> 24U) & 0xFFU);
    p[5] = (uint8_t)((v >> 16U) & 0xFFU);
    p[6] = (uint8_t)((v >> 8U) & 0xFFU);
    p[7] = (uint8_t)(v & 0xFFU);
}

static void native_sha1_transform(NativeSha1Ctx* ctx, const uint8_t block[64])
{
    uint32_t w[80];
    uint32_t a, b, c, d, e;
    size_t t;
    for (t = 0U; t < 16U; t += 1U) {
        w[t] = native_read_be32(block + t * 4U);
    }
    for (; t < 80U; t += 1U) {
        w[t] = native_rotl32(w[t - 3U] ^ w[t - 8U] ^ w[t - 14U] ^ w[t - 16U], 1U);
    }
    a = ctx->state[0];
    b = ctx->state[1];
    c = ctx->state[2];
    d = ctx->state[3];
    e = ctx->state[4];
    for (t = 0U; t < 80U; t += 1U) {
        uint32_t f;
        uint32_t k;
        uint32_t temp;
        if (t < 20U) {
            f = (b & c) | ((~b) & d);
            k = 0x5A827999U;
        } else if (t < 40U) {
            f = b ^ c ^ d;
            k = 0x6ED9EBA1U;
        } else if (t < 60U) {
            f = (b & c) | (b & d) | (c & d);
            k = 0x8F1BBCDCU;
        } else {
            f = b ^ c ^ d;
            k = 0xCA62C1D6U;
        }
        temp = native_rotl32(a, 5U) + f + e + k + w[t];
        e = d;
        d = c;
        c = native_rotl32(b, 30U);
        b = a;
        a = temp;
    }
    ctx->state[0] += a;
    ctx->state[1] += b;
    ctx->state[2] += c;
    ctx->state[3] += d;
    ctx->state[4] += e;
}

static void native_sha1_init(NativeSha1Ctx* ctx)
{
    memset(ctx, 0, sizeof(*ctx));
    ctx->state[0] = 0x67452301U;
    ctx->state[1] = 0xEFCDAB89U;
    ctx->state[2] = 0x98BADCFEU;
    ctx->state[3] = 0x10325476U;
    ctx->state[4] = 0xC3D2E1F0U;
}

static void native_sha1_update(NativeSha1Ctx* ctx, const uint8_t* data, size_t len)
{
    size_t i = 0U;
    if (ctx == NULL || (data == NULL && len > 0U)) {
        return;
    }
    ctx->bit_count += (uint64_t)len * 8ULL;
    while (i < len) {
        size_t to_copy = 64U - ctx->buffer_used;
        if (to_copy > len - i) {
            to_copy = len - i;
        }
        memcpy(ctx->buffer + ctx->buffer_used, data + i, to_copy);
        ctx->buffer_used += to_copy;
        i += to_copy;
        if (ctx->buffer_used == 64U) {
            native_sha1_transform(ctx, ctx->buffer);
            ctx->buffer_used = 0U;
        }
    }
}

static void native_sha1_final(NativeSha1Ctx* ctx, uint8_t out[20])
{
    uint8_t len_buf[8];
    size_t i;
    if (ctx == NULL || out == NULL) {
        return;
    }
    native_write_be64(len_buf, ctx->bit_count);
    {
        uint8_t pad = 0x80U;
        native_sha1_update(ctx, &pad, 1U);
    }
    while (ctx->buffer_used != 56U) {
        uint8_t zero = 0U;
        native_sha1_update(ctx, &zero, 1U);
    }
    native_sha1_update(ctx, len_buf, sizeof(len_buf));
    for (i = 0U; i < 5U; i += 1U) {
        native_write_be32(out + i * 4U, ctx->state[i]);
    }
}

static void native_sha256_transform(NativeSha256Ctx* ctx, const uint8_t block[64])
{
    static const uint32_t k[64] = {
        0x428a2f98U,0x71374491U,0xb5c0fbcfU,0xe9b5dba5U,0x3956c25bU,0x59f111f1U,0x923f82a4U,0xab1c5ed5U,
        0xd807aa98U,0x12835b01U,0x243185beU,0x550c7dc3U,0x72be5d74U,0x80deb1feU,0x9bdc06a7U,0xc19bf174U,
        0xe49b69c1U,0xefbe4786U,0x0fc19dc6U,0x240ca1ccU,0x2de92c6fU,0x4a7484aaU,0x5cb0a9dcU,0x76f988daU,
        0x983e5152U,0xa831c66dU,0xb00327c8U,0xbf597fc7U,0xc6e00bf3U,0xd5a79147U,0x06ca6351U,0x14292967U,
        0x27b70a85U,0x2e1b2138U,0x4d2c6dfcU,0x53380d13U,0x650a7354U,0x766a0abbU,0x81c2c92eU,0x92722c85U,
        0xa2bfe8a1U,0xa81a664bU,0xc24b8b70U,0xc76c51a3U,0xd192e819U,0xd6990624U,0xf40e3585U,0x106aa070U,
        0x19a4c116U,0x1e376c08U,0x2748774cU,0x34b0bcb5U,0x391c0cb3U,0x4ed8aa4aU,0x5b9cca4fU,0x682e6ff3U,
        0x748f82eeU,0x78a5636fU,0x84c87814U,0x8cc70208U,0x90befffaU,0xa4506cebU,0xbef9a3f7U,0xc67178f2U
    };
    uint32_t w[64];
    uint32_t a,b,c,d,e,f,g,h;
    size_t t;
    for (t = 0U; t < 16U; t += 1U) {
        w[t] = native_read_be32(block + t * 4U);
    }
    for (; t < 64U; t += 1U) {
        uint32_t s0 = native_rotr32(w[t - 15U], 7U) ^ native_rotr32(w[t - 15U], 18U) ^ (w[t - 15U] >> 3U);
        uint32_t s1 = native_rotr32(w[t - 2U], 17U) ^ native_rotr32(w[t - 2U], 19U) ^ (w[t - 2U] >> 10U);
        w[t] = w[t - 16U] + s0 + w[t - 7U] + s1;
    }
    a = ctx->state[0]; b = ctx->state[1]; c = ctx->state[2]; d = ctx->state[3];
    e = ctx->state[4]; f = ctx->state[5]; g = ctx->state[6]; h = ctx->state[7];
    for (t = 0U; t < 64U; t += 1U) {
        uint32_t s1 = native_rotr32(e, 6U) ^ native_rotr32(e, 11U) ^ native_rotr32(e, 25U);
        uint32_t ch = (e & f) ^ ((~e) & g);
        uint32_t temp1 = h + s1 + ch + k[t] + w[t];
        uint32_t s0 = native_rotr32(a, 2U) ^ native_rotr32(a, 13U) ^ native_rotr32(a, 22U);
        uint32_t maj = (a & b) ^ (a & c) ^ (b & c);
        uint32_t temp2 = s0 + maj;
        h = g; g = f; f = e; e = d + temp1;
        d = c; c = b; b = a; a = temp1 + temp2;
    }
    ctx->state[0] += a; ctx->state[1] += b; ctx->state[2] += c; ctx->state[3] += d;
    ctx->state[4] += e; ctx->state[5] += f; ctx->state[6] += g; ctx->state[7] += h;
}

static void native_sha256_init(NativeSha256Ctx* ctx)
{
    memset(ctx, 0, sizeof(*ctx));
    ctx->state[0] = 0x6a09e667U; ctx->state[1] = 0xbb67ae85U; ctx->state[2] = 0x3c6ef372U; ctx->state[3] = 0xa54ff53aU;
    ctx->state[4] = 0x510e527fU; ctx->state[5] = 0x9b05688cU; ctx->state[6] = 0x1f83d9abU; ctx->state[7] = 0x5be0cd19U;
}

static void native_sha256_update(NativeSha256Ctx* ctx, const uint8_t* data, size_t len)
{
    size_t i = 0U;
    if (ctx == NULL || (data == NULL && len > 0U)) {
        return;
    }
    ctx->bit_count += (uint64_t)len * 8ULL;
    while (i < len) {
        size_t to_copy = 64U - ctx->buffer_used;
        if (to_copy > len - i) {
            to_copy = len - i;
        }
        memcpy(ctx->buffer + ctx->buffer_used, data + i, to_copy);
        ctx->buffer_used += to_copy;
        i += to_copy;
        if (ctx->buffer_used == 64U) {
            native_sha256_transform(ctx, ctx->buffer);
            ctx->buffer_used = 0U;
        }
    }
}

static void native_sha256_final(NativeSha256Ctx* ctx, uint8_t out[32])
{
    uint8_t len_buf[8];
    size_t i;
    if (ctx == NULL || out == NULL) {
        return;
    }
    native_write_be64(len_buf, ctx->bit_count);
    {
        uint8_t pad = 0x80U;
        native_sha256_update(ctx, &pad, 1U);
    }
    while (ctx->buffer_used != 56U) {
        uint8_t zero = 0U;
        native_sha256_update(ctx, &zero, 1U);
    }
    native_sha256_update(ctx, len_buf, sizeof(len_buf));
    for (i = 0U; i < 8U; i += 1U) {
        native_write_be32(out + i * 4U, ctx->state[i]);
    }
}

static void native_hex_encode(const uint8_t* bytes, size_t len, char* out, size_t out_cap)
{
    static const char* hex = "0123456789abcdef";
    size_t i;
    if (out == NULL || out_cap == 0U) {
        return;
    }
    if (bytes == NULL || out_cap < len * 2U + 1U) {
        out[0] = '\0';
        return;
    }
    for (i = 0U; i < len; i += 1U) {
        out[i * 2U] = hex[(bytes[i] >> 4U) & 0x0FU];
        out[i * 2U + 1U] = hex[bytes[i] & 0x0FU];
    }
    out[len * 2U] = '\0';
}

static int native_syscall_crypto_string_base64_encode(
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
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    in_len = strlen(args[0].string_value);
    out_len = ((in_len + 2U) / 3U) * 4U;
    if (out_len + 1U > NATIVE_BYTES_SCRATCH_CAPACITY ||
        !native_bytes_to_base64((const uint8_t*)args[0].string_value, in_len, g_native_base64_scratch, out_len + 1U)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    *result = aivm_value_string(g_native_base64_scratch);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_crypto_string_base64_decode(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t out_len = 0U;
    size_t i;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    if (!native_bytes_from_base64(args[0].string_value, NULL, 0U, &out_len) ||
        out_len + 1U > NATIVE_BYTES_SCRATCH_CAPACITY ||
        !native_bytes_from_base64(args[0].string_value, g_native_bytes_scratch, out_len, &out_len)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (!native_is_valid_utf8_without_nul(g_native_bytes_scratch, out_len)) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    for (i = 0U; i < out_len; i += 1U) {
        g_native_base64_scratch[i] = (char)g_native_bytes_scratch[i];
    }
    g_native_base64_scratch[out_len] = '\0';
    *result = aivm_value_string(g_native_base64_scratch);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_crypto_sha1(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeSha1Ctx ctx;
    uint8_t digest[20];
    static char hex_out[41];
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    native_sha1_init(&ctx);
    native_sha1_update(&ctx, (const uint8_t*)args[0].string_value, strlen(args[0].string_value));
    native_sha1_final(&ctx, digest);
    native_hex_encode(digest, sizeof(digest), hex_out, sizeof(hex_out));
    *result = aivm_value_string(hex_out);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_crypto_sha256(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    NativeSha256Ctx ctx;
    uint8_t digest[32];
    static char hex_out[65];
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    native_sha256_init(&ctx);
    native_sha256_update(&ctx, (const uint8_t*)args[0].string_value, strlen(args[0].string_value));
    native_sha256_final(&ctx, digest);
    native_hex_encode(digest, sizeof(digest), hex_out, sizeof(hex_out));
    *result = aivm_value_string(hex_out);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_crypto_hmac_sha256(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    uint8_t key_block[64];
    uint8_t k_ipad[64];
    uint8_t k_opad[64];
    uint8_t inner_hash[32];
    size_t i;
    size_t key_len;
    const uint8_t* msg;
    size_t msg_len;
    NativeSha256Ctx ctx;
    static char hex_out[65];
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 2U || args == NULL ||
        args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL ||
        args[1].type != AIVM_VAL_STRING || args[1].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    memset(key_block, 0, sizeof(key_block));
    key_len = strlen(args[0].string_value);
    if (key_len > 64U) {
        uint8_t kh[32];
        native_sha256_init(&ctx);
        native_sha256_update(&ctx, (const uint8_t*)args[0].string_value, key_len);
        native_sha256_final(&ctx, kh);
        memcpy(key_block, kh, sizeof(kh));
    } else {
        memcpy(key_block, args[0].string_value, key_len);
    }
    for (i = 0U; i < 64U; i += 1U) {
        k_ipad[i] = (uint8_t)(key_block[i] ^ 0x36U);
        k_opad[i] = (uint8_t)(key_block[i] ^ 0x5cU);
    }
    msg = (const uint8_t*)args[1].string_value;
    msg_len = strlen(args[1].string_value);
    native_sha256_init(&ctx);
    native_sha256_update(&ctx, k_ipad, sizeof(k_ipad));
    native_sha256_update(&ctx, msg, msg_len);
    native_sha256_final(&ctx, inner_hash);
    native_sha256_init(&ctx);
    native_sha256_update(&ctx, k_opad, sizeof(k_opad));
    native_sha256_update(&ctx, inner_hash, sizeof(inner_hash));
    native_sha256_final(&ctx, inner_hash);
    native_hex_encode(inner_hash, sizeof(inner_hash), hex_out, sizeof(hex_out));
    *result = aivm_value_string(hex_out);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_crypto_random_bytes(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t len;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 1U || args == NULL || args[0].type != AIVM_VAL_INT || args[0].int_value < 0) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    len = (size_t)args[0].int_value;
    if (len == 0U) {
        *result = aivm_value_bytes(NULL, 0U);
        return AIVM_SYSCALL_OK;
    }
    if (len > NATIVE_BYTES_SCRATCH_CAPACITY) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
#ifdef _WIN32
    {
        size_t i;
        for (i = 0U; i < len; i += 1U) {
            unsigned int v = 0U;
            if (rand_s(&v) != 0) {
                result->type = AIVM_VAL_VOID;
                return AIVM_SYSCALL_ERR_INVALID;
            }
            g_native_bytes_scratch[i] = (uint8_t)(v & 0xFFU);
        }
    }
#else
    {
        int fd = open("/dev/urandom", O_RDONLY);
        size_t read_total = 0U;
        if (fd < 0) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        while (read_total < len) {
            ssize_t got = read(fd, g_native_bytes_scratch + read_total, len - read_total);
            if (got <= 0) {
                close(fd);
                result->type = AIVM_VAL_VOID;
                return AIVM_SYSCALL_ERR_INVALID;
            }
            read_total += (size_t)got;
        }
        close(fd);
    }
#endif
    *result = aivm_value_bytes(g_native_bytes_scratch, len);
    return AIVM_SYSCALL_OK;
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

static int native_syscall_str_utf8_byte_count(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (args == NULL || arg_count != 1U || args[0].type != AIVM_VAL_STRING || args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    *result = aivm_value_int((int64_t)strlen(args[0].string_value));
    return AIVM_SYSCALL_OK;
}

static int native_syscall_str_substring(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    const char* text;
    size_t rune_count;
    size_t start_rune;
    size_t end_rune;
    size_t start_byte;
    size_t end_byte;
    size_t copy_length;
    int64_t end_index;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 3U ||
        args == NULL ||
        args[0].type != AIVM_VAL_STRING ||
        args[1].type != AIVM_VAL_INT ||
        args[2].type != AIVM_VAL_INT ||
        args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    text = args[0].string_value;
    if (args[2].int_value <= 0) {
        *result = aivm_value_string("");
        return AIVM_SYSCALL_OK;
    }
    rune_count = native_utf8_rune_count(text);
    start_rune = native_clamp_rune_index(args[1].int_value, rune_count);
    end_index = native_safe_add_i64(args[1].int_value, args[2].int_value);
    end_rune = native_clamp_rune_index(end_index, rune_count);
    if (end_rune < start_rune) {
        end_rune = start_rune;
    }
    start_byte = native_utf8_byte_offset_for_rune(text, start_rune);
    end_byte = native_utf8_byte_offset_for_rune(text, end_rune);
    copy_length = end_byte - start_byte;
    if (!native_string_scratch_ensure_capacity(copy_length + 1U)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (copy_length > 0U) {
        memcpy(g_native_string_scratch, text + start_byte, copy_length);
    }
    g_native_string_scratch[copy_length] = '\0';
    *result = aivm_value_string(g_native_string_scratch);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_str_find(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    const char* text;
    const char* pattern;
    size_t text_runes;
    size_t pattern_runes;
    size_t start_rune;
    size_t candidate_rune;
    size_t candidate_byte;
    size_t pattern_bytes;
    size_t haystack_bytes;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 3U ||
        args == NULL ||
        args[0].type != AIVM_VAL_STRING ||
        args[1].type != AIVM_VAL_STRING ||
        args[2].type != AIVM_VAL_INT ||
        args[0].string_value == NULL ||
        args[1].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    text = args[0].string_value;
    pattern = args[1].string_value;
    text_runes = native_utf8_rune_count(text);
    pattern_runes = native_utf8_rune_count(pattern);
    start_rune = native_clamp_rune_index(args[2].int_value, text_runes);
    if (pattern_runes == 0U) {
        *result = aivm_value_int((int64_t)start_rune);
        return AIVM_SYSCALL_OK;
    }
    if (pattern_runes > text_runes || start_rune > text_runes - pattern_runes) {
        *result = aivm_value_int(-1);
        return AIVM_SYSCALL_OK;
    }
    pattern_bytes = strlen(pattern);
    haystack_bytes = strlen(text);
    candidate_byte = native_utf8_byte_offset_for_rune(text, start_rune);
    for (candidate_rune = start_rune; candidate_byte + pattern_bytes <= haystack_bytes; candidate_rune += 1U) {
        if (memcmp(text + candidate_byte, pattern, pattern_bytes) == 0) {
            *result = aivm_value_int((int64_t)candidate_rune);
            return AIVM_SYSCALL_OK;
        }
        if (candidate_rune >= text_runes - pattern_runes) {
            break;
        }
        candidate_byte = native_utf8_byte_offset_for_rune(text, candidate_rune + 1U);
    }
    *result = aivm_value_int(-1);
    return AIVM_SYSCALL_OK;
}

static int native_syscall_str_remove(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    const char* text;
    size_t input_length;
    size_t rune_count;
    size_t start_rune;
    size_t end_rune;
    size_t start_byte;
    size_t end_byte;
    size_t output_length;
    int64_t end_index;
    (void)target;
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }
    if (arg_count != 3U ||
        args == NULL ||
        args[0].type != AIVM_VAL_STRING ||
        args[1].type != AIVM_VAL_INT ||
        args[2].type != AIVM_VAL_INT ||
        args[0].string_value == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_CONTRACT;
    }
    text = args[0].string_value;
    input_length = strlen(text);
    if (args[2].int_value <= 0) {
        if (!native_string_scratch_ensure_capacity(input_length + 1U)) {
            result->type = AIVM_VAL_VOID;
            return AIVM_SYSCALL_ERR_INVALID;
        }
        if (input_length > 0U) {
            memcpy(g_native_string_scratch, text, input_length);
        }
        g_native_string_scratch[input_length] = '\0';
        *result = aivm_value_string(g_native_string_scratch);
        return AIVM_SYSCALL_OK;
    }
    rune_count = native_utf8_rune_count(text);
    start_rune = native_clamp_rune_index(args[1].int_value, rune_count);
    end_index = native_safe_add_i64(args[1].int_value, args[2].int_value);
    end_rune = native_clamp_rune_index(end_index, rune_count);
    if (end_rune < start_rune) {
        end_rune = start_rune;
    }
    start_byte = native_utf8_byte_offset_for_rune(text, start_rune);
    end_byte = native_utf8_byte_offset_for_rune(text, end_rune);
    output_length = input_length - (end_byte - start_byte);
    if (!native_string_scratch_ensure_capacity(output_length + 1U)) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }
    if (start_byte > 0U) {
        memcpy(g_native_string_scratch, text, start_byte);
    }
    if (end_byte < input_length) {
        memcpy(g_native_string_scratch + start_byte, text + end_byte, input_length - end_byte);
    }
    g_native_string_scratch[output_length] = '\0';
    *result = aivm_value_string(g_native_string_scratch);
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

typedef struct {
    int emit_bundle;
    const char* out_dir;
    const char* input_path;
    const char* debug_mode;
} NativeDebugOptions;

static const char* aivm_opcode_name(AivmOpcode opcode)
{
    switch (opcode) {
        case AIVM_OP_NOP: return "NOP";
        case AIVM_OP_HALT: return "HALT";
        case AIVM_OP_STUB: return "STUB";
        case AIVM_OP_PUSH_INT: return "PUSH_INT";
        case AIVM_OP_POP: return "POP";
        case AIVM_OP_STORE_LOCAL: return "STORE_LOCAL";
        case AIVM_OP_LOAD_LOCAL: return "LOAD_LOCAL";
        case AIVM_OP_ADD_INT: return "ADD_INT";
        case AIVM_OP_JUMP: return "JUMP";
        case AIVM_OP_JUMP_IF_FALSE: return "JUMP_IF_FALSE";
        case AIVM_OP_PUSH_BOOL: return "PUSH_BOOL";
        case AIVM_OP_CALL: return "CALL";
        case AIVM_OP_RET: return "RET";
        case AIVM_OP_EQ_INT: return "EQ_INT";
        case AIVM_OP_EQ: return "EQ";
        case AIVM_OP_CONST: return "CONST";
        case AIVM_OP_STR_CONCAT: return "STR_CONCAT";
        case AIVM_OP_TO_STRING: return "TO_STRING";
        case AIVM_OP_STR_ESCAPE: return "STR_ESCAPE";
        case AIVM_OP_RETURN: return "RETURN";
        case AIVM_OP_STR_SUBSTRING: return "STR_SUBSTRING";
        case AIVM_OP_STR_REMOVE: return "STR_REMOVE";
        case AIVM_OP_CALL_SYS: return "CALL_SYS";
        case AIVM_OP_ASYNC_CALL: return "ASYNC_CALL";
        case AIVM_OP_ASYNC_CALL_SYS: return "ASYNC_CALL_SYS";
        case AIVM_OP_AWAIT: return "AWAIT";
        case AIVM_OP_PAR_BEGIN: return "PAR_BEGIN";
        case AIVM_OP_PAR_FORK: return "PAR_FORK";
        case AIVM_OP_PAR_JOIN: return "PAR_JOIN";
        case AIVM_OP_PAR_CANCEL: return "PAR_CANCEL";
        case AIVM_OP_STR_UTF8_BYTE_COUNT: return "STR_UTF8_BYTE_COUNT";
        case AIVM_OP_NODE_KIND: return "NODE_KIND";
        case AIVM_OP_NODE_ID: return "NODE_ID";
        case AIVM_OP_ATTR_COUNT: return "ATTR_COUNT";
        case AIVM_OP_ATTR_KEY: return "ATTR_KEY";
        case AIVM_OP_ATTR_VALUE_KIND: return "ATTR_VALUE_KIND";
        case AIVM_OP_ATTR_VALUE_STRING: return "ATTR_VALUE_STRING";
        case AIVM_OP_ATTR_VALUE_INT: return "ATTR_VALUE_INT";
        case AIVM_OP_ATTR_VALUE_BOOL: return "ATTR_VALUE_BOOL";
        case AIVM_OP_CHILD_COUNT: return "CHILD_COUNT";
        case AIVM_OP_CHILD_AT: return "CHILD_AT";
        case AIVM_OP_MAKE_BLOCK: return "MAKE_BLOCK";
        case AIVM_OP_APPEND_CHILD: return "APPEND_CHILD";
        case AIVM_OP_MAKE_ERR: return "MAKE_ERR";
        case AIVM_OP_MAKE_LIT_STRING: return "MAKE_LIT_STRING";
        case AIVM_OP_MAKE_LIT_INT: return "MAKE_LIT_INT";
        case AIVM_OP_MAKE_NODE: return "MAKE_NODE";
        case AIVM_OP_MAKE_FIELD_STRING: return "MAKE_FIELD_STRING";
        case AIVM_OP_MAKE_MAP: return "MAKE_MAP";
        default: return "UNKNOWN";
    }
}

static const char* infer_call_target(const AivmProgram* program, const AivmVm* vm)
{
    size_t pc;
    const AivmInstruction* inst;
    if (program == NULL || vm == NULL || program->instructions == NULL) {
        return NULL;
    }
    pc = vm->instruction_pointer;
    if (pc >= program->instruction_count) {
        return NULL;
    }
    inst = &program->instructions[pc];
    if (inst->opcode != AIVM_OP_CALL_SYS && inst->opcode != AIVM_OP_ASYNC_CALL_SYS) {
        return NULL;
    }
    if (pc == 0U) {
        return NULL;
    }
    {
        const AivmInstruction* prev = &program->instructions[pc - 1U];
        if (prev->opcode == AIVM_OP_CONST &&
            prev->operand_int >= 0 &&
            (size_t)prev->operand_int < program->constant_count) {
            AivmValue target_value = program->constants[prev->operand_int];
            if (target_value.type == AIVM_VAL_STRING && target_value.string_value != NULL) {
                return target_value.string_value;
            }
        }
    }
    return NULL;
}

static int write_native_debug_bundle(
    const NativeDebugOptions* options,
    const AivmProgram* program,
    const AivmVm* vm,
    int exit_code,
    int has_exit_code,
    const char* diagnostics_line)
{
    char path[PATH_MAX];
    FILE* f;
    if (options == NULL || !options->emit_bundle || options->out_dir == NULL) {
        return 1;
    }
    if (!ensure_directory_recursive(options->out_dir)) {
        return 0;
    }

    if (!join_path(options->out_dir, "config.toml", path, sizeof(path))) {
        return 0;
    }
    f = fopen(path, "wb");
    if (f == NULL) {
        return 0;
    }
    fprintf(f, "mode = \"debug-run-native\"\n");
    fprintf(f, "debug_mode = \"%s\"\n", (options->debug_mode == NULL) ? "off" : options->debug_mode);
    fprintf(f, "app_path = \"%s\"\n", (options->input_path == NULL) ? "" : options->input_path);
    fprintf(f, "status = \"%s\"\n", (vm != NULL && vm->status == AIVM_VM_STATUS_ERROR) ? "error" : "ok");
    fprintf(f, "exit_code = %d\n", has_exit_code ? exit_code : 0);
    fprintf(f, "node_gc_interval_allocations = %d\n", AIVM_VM_NODE_GC_INTERVAL_ALLOCATIONS);
    fprintf(f, "node_gc_pressure_threshold_nodes = %d\n", AIVM_VM_NODE_GC_PRESSURE_THRESHOLD);
    fclose(f);

    if (!join_path(options->out_dir, "vm_trace.toml", path, sizeof(path))) {
        return 0;
    }
    f = fopen(path, "wb");
    if (f == NULL) {
        return 0;
    }
    fprintf(f, "trace = []\n");
    if (vm != NULL && program != NULL && vm->instruction_pointer < program->instruction_count && program->instructions != NULL) {
        const AivmInstruction* inst = &program->instructions[vm->instruction_pointer];
        fprintf(f, "last = { function = \"main\", pc = %llu, op = \"%s\", node_id = \"unknown\" }\n",
            (unsigned long long)vm->instruction_pointer,
            aivm_opcode_name(inst->opcode));
    }
    fclose(f);

    if (!join_path(options->out_dir, "state_snapshots.toml", path, sizeof(path))) {
        return 0;
    }
    f = fopen(path, "wb");
    if (f == NULL) {
        return 0;
    }
    fprintf(f, "stack_count = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->stack_count));
    fprintf(f, "locals_count = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->locals_count));
    fprintf(f, "string_arena_used = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->string_arena_used));
    fprintf(f, "string_arena_high_water = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->string_arena_high_water));
    fprintf(f, "bytes_arena_used = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->bytes_arena_used));
    fprintf(f, "bytes_arena_high_water = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->bytes_arena_high_water));
    fprintf(f, "node_count = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_count));
    fprintf(f, "node_high_water = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_high_water));
    fprintf(f, "node_attr_count = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_attr_count));
    fprintf(f, "node_attr_high_water = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_attr_high_water));
    fprintf(f, "node_child_count = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_child_count));
    fprintf(f, "node_child_high_water = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_child_high_water));
    fprintf(f, "node_gc_compactions = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_compaction_count));
    fprintf(f, "node_gc_attempts = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_attempt_count));
    fprintf(f, "node_gc_reclaimed_nodes = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_reclaimed_nodes));
    fprintf(f, "node_gc_reclaimed_attrs = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_reclaimed_attrs));
    fprintf(f, "node_gc_reclaimed_children = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_reclaimed_children));
    fprintf(f, "node_gc_allocations_since_gc = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_allocations_since_gc));
    fprintf(f, "node_gc_interval_allocations = %d\n", AIVM_VM_NODE_GC_INTERVAL_ALLOCATIONS);
    fprintf(f, "node_gc_pressure_threshold_nodes = %d\n", AIVM_VM_NODE_GC_PRESSURE_THRESHOLD);
    fprintf(f, "string_arena_pressure_count = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->string_arena_pressure_count));
    fprintf(f, "bytes_arena_pressure_count = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->bytes_arena_pressure_count));
    fprintf(f, "node_arena_pressure_count = %llu\n", (unsigned long long)((vm == NULL) ? 0U : vm->node_arena_pressure_count));
    fclose(f);

    if (!join_path(options->out_dir, "syscalls.toml", path, sizeof(path))) {
        return 0;
    }
    f = fopen(path, "wb");
    if (f == NULL) {
        return 0;
    }
    fprintf(f, "syscalls = []\n");
    fclose(f);

    if (!join_path(options->out_dir, "events.toml", path, sizeof(path))) {
        return 0;
    }
    f = fopen(path, "wb");
    if (f == NULL) {
        return 0;
    }
    fprintf(f, "events = []\n");
    fclose(f);

    if (!join_path(options->out_dir, "diagnostics.toml", path, sizeof(path))) {
        return 0;
    }
    f = fopen(path, "wb");
    if (f == NULL) {
        return 0;
    }
    if (diagnostics_line != NULL && diagnostics_line[0] != '\0') {
        fprintf(f, "diagnostics = [\"%s\"]\n", diagnostics_line);
    } else {
        fprintf(f, "diagnostics = []\n");
    }
    fprintf(f, "memory = { string_arena_used = %llu, string_arena_high_water = %llu, bytes_arena_used = %llu, bytes_arena_high_water = %llu, node_count = %llu, node_high_water = %llu, node_gc_compactions = %llu, node_gc_attempts = %llu, node_gc_reclaimed_nodes = %llu, node_gc_allocations_since_gc = %llu, node_gc_interval_allocations = %d, node_gc_pressure_threshold_nodes = %d, string_arena_pressure_count = %llu, bytes_arena_pressure_count = %llu, node_arena_pressure_count = %llu }\n",
        (unsigned long long)((vm == NULL) ? 0U : vm->string_arena_used),
        (unsigned long long)((vm == NULL) ? 0U : vm->string_arena_high_water),
        (unsigned long long)((vm == NULL) ? 0U : vm->bytes_arena_used),
        (unsigned long long)((vm == NULL) ? 0U : vm->bytes_arena_high_water),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_high_water),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_compaction_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_attempt_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_reclaimed_nodes),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_allocations_since_gc),
        AIVM_VM_NODE_GC_INTERVAL_ALLOCATIONS,
        AIVM_VM_NODE_GC_PRESSURE_THRESHOLD,
        (unsigned long long)((vm == NULL) ? 0U : vm->string_arena_pressure_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->bytes_arena_pressure_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_arena_pressure_count));
    fclose(f);
    return 1;
}

static int emit_vm_error_with_context(const AivmProgram* program, const AivmVm* vm, const char* vm_error_message)
{
    const char* phase = "exec";
    const char* function_name = "main";
    const char* node_id = "unknown";
    const char* opcode_name = "UNKNOWN";
    const char* call_target = NULL;
    const char* vm_code = "VM000";
    const char* vm_msg = "Unknown VM error.";
    const char* detail = NULL;
    size_t pc = 0U;
    size_t display_pc = 0U;
    const AivmInstruction* inst = NULL;
    char typed_code[32];
    const char* detail_colon;
    const char* detail_slash;
    size_t code_len;
    if (vm == NULL || program == NULL) {
        fprintf(stderr, "Err#err1(code=RUN001 message=\"%s phase=exec function=main pc=0 nodeId=unknown opcode=UNKNOWN\" nodeId=vm)\n",
            (vm_error_message == NULL) ? "VM execution failed." : vm_error_message);
        return 3;
    }
    detail = aivm_vm_error_detail(vm);
    vm_code = aivm_vm_error_code(vm->error);
    vm_msg = aivm_vm_error_message(vm->error);
    pc = vm->instruction_pointer;
    display_pc = pc;
    if (program->instruction_count > 0U && display_pc >= program->instruction_count) {
        display_pc = program->instruction_count - 1U;
    }
    if (vm->error == AIVM_VM_ERR_SYSCALL) {
        phase = "syscall";
    }
    if (program->instructions != NULL && display_pc < program->instruction_count) {
        inst = &program->instructions[display_pc];
        opcode_name = aivm_opcode_name(inst->opcode);
        {
            AivmVm vm_for_target = *vm;
            vm_for_target.instruction_pointer = display_pc;
            call_target = infer_call_target(program, &vm_for_target);
        }
    }
    if (vm->error == AIVM_VM_ERR_SYSCALL && program->instructions != NULL && program->instruction_count > 0U) {
        size_t cursor = (pc == 0U) ? 0U : (pc - 1U);
        if (cursor >= program->instruction_count) {
            cursor = program->instruction_count - 1U;
        }
        while (1) {
            const AivmInstruction* probe = &program->instructions[cursor];
            if (probe->opcode == AIVM_OP_CALL_SYS || probe->opcode == AIVM_OP_ASYNC_CALL_SYS) {
                display_pc = cursor;
                opcode_name = aivm_opcode_name(probe->opcode);
                if (probe->operand_int >= 0) {
                    size_t arg_count = (size_t)probe->operand_int;
                    if (cursor > arg_count) {
                        size_t target_inst_index = cursor - arg_count - 1U;
                        const AivmInstruction* target_inst = &program->instructions[target_inst_index];
                        if (target_inst->opcode == AIVM_OP_CONST &&
                            target_inst->operand_int >= 0 &&
                            (size_t)target_inst->operand_int < program->constant_count) {
                            AivmValue target_value = program->constants[target_inst->operand_int];
                            if (target_value.type == AIVM_VAL_STRING && target_value.string_value != NULL) {
                                call_target = target_value.string_value;
                            }
                        }
                    }
                }
                break;
            }
            if (cursor == 0U) {
                break;
            }
            cursor -= 1U;
        }
    }

    if (vm->error == AIVM_VM_ERR_SYSCALL && detail != NULL && detail[0] != '\0') {
        memset(typed_code, 0, sizeof(typed_code));
        detail_colon = strchr(detail, ':');
        detail_slash = strchr(detail, '/');
        if (detail_colon == NULL) {
            detail_colon = detail + strlen(detail);
        }
        if (detail_slash != NULL && detail_slash < detail_colon) {
            detail_colon = detail_slash;
        }
        code_len = (size_t)(detail_colon - detail);
        if (code_len > 0U && code_len < sizeof(typed_code)) {
            memcpy(typed_code, detail, code_len);
            typed_code[code_len] = '\0';
            fprintf(stderr,
                "Err#err1(code=%s message=\"phase=%s function=%s pc=%llu nodeId=%s opcode=%s callTarget=%s vmCode=%s vmMessage=%s detail=%s\" nodeId=vm)\n",
                typed_code,
                phase,
                function_name,
                (unsigned long long)display_pc,
                node_id,
                opcode_name,
                (call_target == NULL) ? "unknown" : call_target,
                vm_code,
                vm_msg,
                detail);
            return 3;
        }
    }

    fprintf(stderr,
        "Err#err1(code=RUN001 message=\"%s phase=%s function=%s pc=%llu nodeId=%s opcode=%s callTarget=%s vmCode=%s vmMessage=%s detail=%s\" nodeId=vm)\n",
        (vm_error_message == NULL) ? "VM execution failed." : vm_error_message,
        phase,
        function_name,
        (unsigned long long)display_pc,
        node_id,
        opcode_name,
        (call_target == NULL) ? "unknown" : call_target,
        vm_code,
        vm_msg,
        (detail == NULL) ? "none" : detail);
    return 3;
}

static int run_native_compiled_program(
    const AivmProgram* program,
    const char* vm_error_message,
    const char* const* process_argv,
    size_t process_argv_count,
    const NativeDebugOptions* debug_options)
{
    AivmSyscallBinding bindings[101];
    AivmVm vm;
    int ok;
    int exit_code = 0;
    int has_exit_code = 0;
    char diagnostics_line[768];

    if (program == NULL) {
        (void)write_native_debug_bundle(debug_options, NULL, NULL, 0, 0, "program-null");
        return 2;
    }
    g_native_active_vm = &vm;
    (void)snprintf(g_native_ui_event_type, sizeof(g_native_ui_event_type), "none");
    g_native_ui_event_key[0] = '\0';
    g_native_ui_event_text[0] = '\0';
    g_native_ui_event_target_id[0] = '\0';
    native_scene_capture_configure(
        (debug_options == NULL) ? "off" : debug_options->debug_mode,
        (debug_options == NULL) ? NULL : debug_options->out_dir);
    native_ui_runtime_reset_handles();
    native_host_ui_reset();
    native_net_reset();

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
    bindings[22].target = "sys.str.substring";
    bindings[22].handler = native_syscall_str_substring;
    bindings[23].target = "sys.str.find";
    bindings[23].handler = native_syscall_str_find;
    bindings[24].target = "sys.str.remove";
    bindings[24].handler = native_syscall_str_remove;
    bindings[25].target = "sys.ui.createWindow";
    bindings[25].handler = native_syscall_ui_create_window;
    bindings[26].target = "sys.ui.beginFrame";
    bindings[26].handler = native_syscall_ui_void_1;
    bindings[27].target = "sys.ui.drawRect";
    bindings[27].handler = native_syscall_ui_draw_rect;
    bindings[28].target = "sys.ui.drawText";
    bindings[28].handler = native_syscall_ui_draw_text;
    bindings[29].target = "sys.ui.endFrame";
    bindings[29].handler = native_syscall_ui_void_1;
    bindings[30].target = "sys.ui.pollEvent";
    bindings[30].handler = native_syscall_ui_poll_event;
    bindings[31].target = "sys.ui.present";
    bindings[31].handler = native_syscall_ui_void_1;
    bindings[32].target = "sys.ui.closeWindow";
    bindings[32].handler = native_syscall_ui_void_1;
    bindings[33].target = "sys.ui.drawLine";
    bindings[33].handler = native_syscall_ui_draw_line;
    bindings[34].target = "sys.ui.drawEllipse";
    bindings[34].handler = native_syscall_ui_draw_ellipse;
    bindings[35].target = "sys.ui.drawPath";
    bindings[35].handler = native_syscall_ui_draw_path;
    bindings[36].target = "sys.ui.drawImage";
    bindings[36].handler = native_syscall_ui_draw_image;
    bindings[37].target = "sys.ui.getWindowSize";
    bindings[37].handler = native_syscall_ui_get_window_size;
    bindings[38].target = "sys.ui.waitFrame";
    bindings[38].handler = native_syscall_ui_void_1;
    bindings[39].target = "sys.worker.start";
    bindings[39].handler = native_syscall_worker_start;
    bindings[40].target = "sys.worker.poll";
    bindings[40].handler = native_syscall_worker_poll;
    bindings[41].target = "sys.worker.result";
    bindings[41].handler = native_syscall_worker_result;
    bindings[42].target = "sys.worker.error";
    bindings[42].handler = native_syscall_worker_error;
    bindings[43].target = "sys.worker.cancel";
    bindings[43].handler = native_syscall_worker_cancel;
    bindings[44].target = "sys.remote.call";
    bindings[44].handler = native_syscall_remote_call;
    /* Legacy aliases retained for pre-release AiBC1 samples still using underscore style names. */
    bindings[45].target = "sys.stdout_writeLine";
    bindings[45].handler = native_syscall_stdout_write_line;
    bindings[46].target = "sys.process_argv";
    bindings[46].handler = native_syscall_process_argv;
    bindings[47].target = "sys.process.cwd";
    bindings[47].handler = native_syscall_process_cwd;
    bindings[48].target = "sys.process.env.get";
    bindings[48].handler = native_syscall_process_env_get;
    bindings[49].target = "sys.platform";
    bindings[49].handler = native_syscall_identity_0;
    bindings[50].target = "sys.arch";
    bindings[50].handler = native_syscall_identity_0;
    bindings[51].target = "sys.os.version";
    bindings[51].handler = native_syscall_identity_0;
    bindings[52].target = "sys.runtime";
    bindings[52].handler = native_syscall_identity_0;
    bindings[53].target = "sys.time.nowUnixMs";
    bindings[53].handler = native_syscall_time_now_unix_ms;
    bindings[54].target = "sys.time.monotonicMs";
    bindings[54].handler = native_syscall_time_monotonic_ms;
    bindings[55].target = "sys.time.sleepMs";
    bindings[55].handler = native_syscall_time_sleep_ms;
    bindings[56].target = "sys.fs.file.read";
    bindings[56].handler = native_syscall_fs_file_read;
    bindings[57].target = "sys.fs.file.exists";
    bindings[57].handler = native_syscall_fs_file_exists;
    bindings[58].target = "sys.fs.path.exists";
    bindings[58].handler = native_syscall_fs_path_exists;
    bindings[59].target = "sys.fs.file.write";
    bindings[59].handler = native_syscall_fs_file_write;
    bindings[60].target = "sys.fs.dir.create";
    bindings[60].handler = native_syscall_fs_dir_create;
    bindings[61].target = "sys.str.utf8ByteCount";
    bindings[61].handler = native_syscall_str_utf8_byte_count;
    bindings[62].target = "sys.console.write";
    bindings[62].handler = native_syscall_console_write;
    bindings[63].target = "sys.console.writeLine";
    bindings[63].handler = native_syscall_console_write_line;
    bindings[64].target = "sys.console.writeErrLine";
    bindings[64].handler = native_syscall_console_write_err_line;
    bindings[65].target = "sys.console.readLine";
    bindings[65].handler = native_syscall_console_read_line;
    bindings[66].target = "sys.console.readAllStdin";
    bindings[66].handler = native_syscall_console_read_all_stdin;
    bindings[67].target = "sys.process.exit";
    bindings[67].handler = native_syscall_process_exit;
    bindings[68].target = "sys.fs.dir.list";
    bindings[68].handler = native_syscall_fs_dir_list;
    bindings[69].target = "sys.fs.path.stat";
    bindings[69].handler = native_syscall_fs_path_stat;
    bindings[70].target = "sys.net.tcp.close";
    bindings[70].handler = native_syscall_net_tcp_close;
    bindings[71].target = "sys.net.tcp.connect";
    bindings[71].handler = native_syscall_net_tcp_connect;
    bindings[72].target = "sys.net.tcp.listen";
    bindings[72].handler = native_syscall_net_tcp_listen;
    bindings[73].target = "sys.net.tcp.listenTls";
    bindings[73].handler = native_syscall_net_tcp_listen_tls;
    bindings[74].target = "sys.net.tcp.accept";
    bindings[74].handler = native_syscall_net_tcp_accept;
    bindings[75].target = "sys.net.tcp.read";
    bindings[75].handler = native_syscall_net_tcp_read;
    bindings[76].target = "sys.net.tcp.write";
    bindings[76].handler = native_syscall_net_tcp_write;
    bindings[77].target = "sys.net.tcp.connectTls";
    bindings[77].handler = native_syscall_net_tcp_connect_tls;
    bindings[78].target = "sys.net.tcp.connectStart";
    bindings[78].handler = native_syscall_net_start_op;
    bindings[79].target = "sys.net.tcp.connectTlsStart";
    bindings[79].handler = native_syscall_net_tcp_connect_tls_start;
    bindings[80].target = "sys.net.tcp.readStart";
    bindings[80].handler = native_syscall_net_start_op;
    bindings[81].target = "sys.net.tcp.writeStart";
    bindings[81].handler = native_syscall_net_start_op;
    bindings[82].target = "sys.net.async.poll";
    bindings[82].handler = native_syscall_net_async_poll;
    bindings[83].target = "sys.net.async.cancel";
    bindings[83].handler = native_syscall_net_async_cancel;
    bindings[84].target = "sys.net.async.await";
    bindings[84].handler = native_syscall_net_async_await;
    bindings[85].target = "sys.net.async.resultInt";
    bindings[85].handler = native_syscall_net_async_result_int;
    bindings[86].target = "sys.net.async.resultBytes";
    bindings[86].handler = native_syscall_net_async_result_bytes;
    bindings[87].target = "sys.net.async.error";
    bindings[87].handler = native_syscall_net_async_error;
    bindings[88].target = "sys.net.udp.bind";
    bindings[88].handler = native_syscall_net_udp_bind;
    bindings[89].target = "sys.net.udp.recv";
    bindings[89].handler = native_syscall_net_udp_recv;
    bindings[90].target = "sys.net.udp.send";
    bindings[90].handler = native_syscall_net_udp_send;
    bindings[91].target = "sys.crypto.base64Encode";
    bindings[91].handler = native_syscall_crypto_string_base64_encode;
    bindings[92].target = "sys.crypto.base64Decode";
    bindings[92].handler = native_syscall_crypto_string_base64_decode;
    bindings[93].target = "sys.crypto.sha1";
    bindings[93].handler = native_syscall_crypto_sha1;
    bindings[94].target = "sys.crypto.sha256";
    bindings[94].handler = native_syscall_crypto_sha256;
    bindings[95].target = "sys.crypto.hmacSha256";
    bindings[95].handler = native_syscall_crypto_hmac_sha256;
    bindings[96].target = "sys.crypto.randomBytes";
    bindings[96].handler = native_syscall_crypto_random_bytes;
    bindings[97].target = "sys.debug.mode";
    bindings[97].handler = native_syscall_debug_mode;
    bindings[98].target = "sys.debug.captureFrameBegin";
    bindings[98].handler = native_syscall_debug_capture_frame_begin;
    bindings[99].target = "sys.debug.captureDraw";
    bindings[99].handler = native_syscall_debug_capture_draw;
    bindings[100].target = "sys.debug.captureFrameEnd";
    bindings[100].handler = native_syscall_debug_capture_frame_end;
    ok = aivm_execute_program_with_syscalls_and_argv(
        program,
        bindings,
        101U,
        process_argv,
        process_argv_count,
        &vm);
    if (!ok || vm.status == AIVM_VM_STATUS_ERROR) {
        const char* detail = aivm_vm_error_detail(&vm);
        (void)snprintf(
            diagnostics_line,
            sizeof(diagnostics_line),
            "status=error vm_code=%s detail=%s pc=%llu",
            aivm_vm_error_code(vm.error),
            (detail == NULL) ? "none" : detail,
            (unsigned long long)vm.instruction_pointer);
        (void)write_native_debug_bundle(debug_options, program, &vm, 0, 0, diagnostics_line);
        native_net_reset();
        native_host_ui_shutdown();
        native_scene_capture_reset();
        g_native_active_vm = NULL;
        return emit_vm_error_with_context(program, &vm, vm_error_message);
    }
    if (vm.status == AIVM_VM_STATUS_HALTED && vm.stack_count > 0U) {
        const AivmValue* top = &vm.stack[vm.stack_count - 1U];
        if (top->type == AIVM_VAL_INT) {
            has_exit_code = 1;
            exit_code = (int)top->int_value;
        }
    }
    (void)snprintf(
        diagnostics_line,
        sizeof(diagnostics_line),
        "status=ok vm_code=%s pc=%llu",
        aivm_vm_error_code(vm.error),
        (unsigned long long)vm.instruction_pointer);
    (void)write_native_debug_bundle(debug_options, program, &vm, exit_code, has_exit_code, diagnostics_line);
    native_net_reset();
    native_host_ui_shutdown();
    native_scene_capture_reset();
    g_native_active_vm = NULL;
    if (has_exit_code) {
        printf("Ok#ok1(type=int value=%d)\n", exit_code);
        return exit_code;
    }
    return 0;
}

static int run_native_aibc1(
    const char* path,
    const char* const* process_argv,
    size_t process_argv_count,
    const NativeDebugOptions* debug_options)
{
    unsigned char* bytes = NULL;
    size_t byte_count = 0U;
    AivmProgram program;
    AivmProgramLoadResult load_result;

    if (!read_binary_file(path, &bytes, &byte_count)) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to read AiBC1 file. phase=load function=main pc=0 nodeId=unknown opcode=UNKNOWN callTarget=unknown\" nodeId=program)\n");
        return 2;
    }

    load_result = aivm_program_load_aibc1(bytes, byte_count, &program);
    free(bytes);

    if (load_result.status != AIVM_PROGRAM_OK) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to load AiBC1 program. phase=load function=main pc=%llu nodeId=unknown opcode=UNKNOWN callTarget=unknown loadCode=%s\" nodeId=program)\n",
            (unsigned long long)load_result.error_offset,
            aivm_program_status_code(load_result.status));
        return 2;
    }
    return run_native_compiled_program(
        &program,
        "AiBC1 execution failed.",
        process_argv,
        process_argv_count,
        debug_options);
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
        int escaped = 0;
        vstart += 1;
        vend = vstart;
        while (*vend != '\0') {
            if (!escaped && *vend == '"') {
                break;
            }
            if (!escaped && *vend == '\\') {
                escaped = 1;
            } else {
                escaped = 0;
            }
            vend += 1;
        }
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
    char needle[64];
    if (attrs == NULL || key == NULL) {
        return 0;
    }
    if (snprintf(needle, sizeof(needle), "%s=", key) >= (int)sizeof(needle)) {
        return 0;
    }
    return strstr(attrs, needle) != NULL;
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
    MAP_OP(MAKE_FIELD_STRING)
    MAP_OP(MAKE_MAP)
#undef MAP_OP
    return 0;
}

static int bytecode_add_string_const(AivmProgram* program, const char* value, int64_t* out_idx)
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
            *out_idx = (int64_t)i;
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
    *out_idx = (int64_t)program->constant_count;
    program->constant_count += 1U;
    return 1;
}

static int parse_bytecode_aos_to_program_text(
    const char* source,
    AivmProgram* out_program,
    int allow_legacy_zero_b)
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
        } else if (strcmp(kind, "node") == 0) {
            /* Legacy bytecode AOS may carry opaque node constants (for example value="Node#n1").
               The native publish path preserves the value family as a deterministic node handle. */
            out_program->constant_storage[out_program->constant_count] =
                aivm_value_node((int64_t)(out_program->constant_count + 1U));
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
        char syscall_target[512];
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
        if (parse_attr_span(attrs, "s", syscall_target, sizeof(syscall_target))) {
            int64_t target_const_idx = 0;
            if (!allow_legacy_zero_b ||
                (opcode != AIVM_OP_CALL_SYS && opcode != AIVM_OP_ASYNC_CALL_SYS) ||
                !bytecode_add_string_const(out_program, syscall_target, &target_const_idx) ||
                out_program->instruction_count >= AIVM_PROGRAM_MAX_INSTRUCTIONS) {
                return 0;
            }
            out_program->instruction_storage[out_program->instruction_count].opcode = AIVM_OP_CONST;
            out_program->instruction_storage[out_program->instruction_count].operand_int = target_const_idx;
            out_program->instruction_count += 1U;
        }
        if (has_attr_key(attrs, "b")) {
            int64_t b = 0;
            if (!allow_legacy_zero_b || !parse_attr_int64(attrs, "b", &b) || b != 0) {
                return 0;
            }
        }
        (void)parse_attr_int64(attrs, "a", &a);

        out_program->instruction_storage[out_program->instruction_count].opcode = opcode;
        out_program->instruction_storage[out_program->instruction_count].operand_int = a;
        out_program->instruction_count += 1U;
        p = rparen + 1;
    }

    return out_program->instruction_count > 0U;
}

static int parse_bytecode_aos_to_program_file(
    const char* aos_path,
    AivmProgram* out_program,
    int allow_legacy_zero_b)
{
    char source[131072];
    if (aos_path == NULL || out_program == NULL) {
        return simple_fail("graph compile: invalid args");
    }
    if (!read_text_file(aos_path, source, sizeof(source))) {
        return 0;
    }
    return parse_bytecode_aos_to_program_text(source, out_program, allow_legacy_zero_b);
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

static char g_simple_last_error[256];

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

static int simple_find_matching_paren(const char* open_paren, const char* end, const char** out_close)
{
    int depth = 0;
    int in_string = 0;
    const char* q;
    if (open_paren == NULL || end == NULL || out_close == NULL || *open_paren != '(') {
        return 0;
    }
    q = open_paren;
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
        if (c == '(') {
            depth += 1;
        } else if (c == ')') {
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
    const char* kdelim;
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
    kdelim = kstart;
    while (kdelim < end && *kdelim != '\0' &&
           !isspace((unsigned char)*kdelim) &&
           *kdelim != '(' && *kdelim != '{' && *kdelim != '#') {
        kdelim += 1;
    }
    if (kdelim <= kstart || kdelim >= end) {
        return 0;
    }
    kind_len = (size_t)(kdelim - kstart);
    if (kind_len == 0U || kind_len + 1U > sizeof(out_node->kind)) {
        return 0;
    }
    memcpy(out_node->kind, kstart, kind_len);
    out_node->kind[kind_len] = '\0';

    kid_end = kdelim;
    if (*kid_end == '#') {
        kid_end += 1;
        while (kid_end < end && *kid_end != '\0' &&
               !isspace((unsigned char)*kid_end) &&
               *kid_end != '(' && *kid_end != '{') {
            kid_end += 1;
        }
    }

    lparen = NULL;
    if (kid_end < end && *kid_end == '(') {
        lparen = kid_end;
    }
    if (lparen != NULL) {
        if (!simple_find_matching_paren(lparen, end, &rparen) || rparen == NULL || rparen >= end) {
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
    if (message == NULL) {
        message = "unknown";
    }
    (void)snprintf(g_simple_last_error, sizeof(g_simple_last_error), "%s", message);
    return 0;
}

static int simple_failf(const char* fmt, ...)
{
    va_list args;
    if (fmt == NULL) {
        return simple_fail("unknown");
    }
    va_start(args, fmt);
    (void)vsnprintf(g_simple_last_error, sizeof(g_simple_last_error), fmt, args);
    va_end(args);
    return 0;
}

static const char* simple_last_error(void)
{
    if (g_simple_last_error[0] == '\0') {
        return "unknown";
    }
    return g_simple_last_error;
}

static int simple_emit_instruction(AivmProgram* program, AivmOpcode opcode, int64_t operand_int)
{
    if (program == NULL) {
        simple_fail("emit instruction: null program");
        return 0;
    }
    if (program->instruction_count >= AIVM_PROGRAM_MAX_INSTRUCTIONS) {
        simple_fail("emit instruction: instruction capacity exceeded");
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
        simple_fail("add string const: invalid args");
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
        simple_fail("add string const: constant capacity exceeded");
        return 0;
    }
    len = strlen(value);
    base = program->string_storage_used;
    if (base + len + 1U > AIVM_PROGRAM_MAX_STRING_BYTES) {
        simple_fail("add string const: string storage exceeded");
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
            return simple_fail("lit missing value");
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
                return simple_failf("string literal decode failed: raw=%s attrs=%s", value, node->attrs);
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
            return simple_fail("var missing name");
        }
        if (!simple_lookup_local(locals, local_count, name, &idx, 0)) {
            return simple_failf("unknown local variable: %s", name);
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
    return simple_failf("unsupported expr kind: %s", node->kind);
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

    g_simple_last_error[0] = '\0';

    if (source == NULL || out_program == NULL) {
        return simple_fail("missing source/program");
    }
    program_pos = strstr(source, "Program#");
    if (program_pos == NULL) {
        program_pos = strstr(source, "Program");
    }
    if (program_pos == NULL || strstr(source, "Bytecode#") != NULL || strstr(source, "Bytecode(") != NULL) {
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
        if (strcmp(node.kind, "Import") == 0) {
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
                return simple_failf("let expression compile failed for '%s' (kind=%s)", name, expr.kind);
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
            const char* c = NULL;
            size_t arg_count = 0U;
            SimpleNodeView arg;
            if (!parse_attr_span(node.attrs, "target", target, sizeof(target))) {
                return simple_fail("call missing target");
            }
            if (strcmp(target, "io.print") == 0 || strcmp(target, "io.write") == 0 || strcmp(target, "sys.stdout.writeLine") == 0) {
                mapped = "sys.stdout.writeLine";
            } else if (starts_with(target, "sys.")) {
                mapped = target;
            } else {
                return simple_fail("call unsupported target");
            }
            if (!simple_add_string_const(out_program, mapped, &target_idx)) {
                return simple_fail("call target const add failed");
            }
            if (!simple_emit_instruction(out_program, AIVM_OP_CONST, (int64_t)target_idx)) {
                return simple_fail("call target const emit failed");
            }
            c = node.body_start;
            while (simple_parse_next_node(c, node.body_end, &arg)) {
                if (!simple_compile_expr_node(&arg, out_program, locals, &local_count)) {
                    return simple_fail("call argument compile failed");
                }
                arg_count += 1U;
                c = arg.next;
            }
            if (arg_count > AIVM_VM_MAX_SYSCALL_ARGS) {
                return simple_fail("call has too many arguments");
            }
            if (!simple_emit_instruction(out_program, AIVM_OP_CALL_SYS, (int64_t)arg_count) ||
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

static int parse_simple_program_graph_to_program_file(const char* aos_path, AivmProgram* out_program);

static int parse_simple_program_aos_to_program_file(const char* aos_path, AivmProgram* out_program)
{
    if (aos_path == NULL || out_program == NULL) {
        return 0;
    }
    return parse_simple_program_graph_to_program_file(aos_path, out_program);
}

#define SIMPLE_MAX_SOURCES 64
#define SIMPLE_MAX_FUNCS 512
#define SIMPLE_MAX_FIXUPS 2048
#define SIMPLE_MAX_LOCALS 1024
#define SIMPLE_MAX_LOOP_DEPTH 128
#define SIMPLE_MAX_LOOP_FIXUPS 1024

typedef struct {
    char path[PATH_MAX];
    char* text;
} SimpleSource;

typedef struct {
    char name[64];
    char params_raw[256];
    const char* body_start;
    const char* body_end;
    int source_index;
    int compiled;
    int compiling;
    size_t entry_ip;
} SimpleFnDef;

typedef struct {
    size_t instruction_index;
    char target[64];
} SimpleCallFixup;

typedef struct {
    size_t start_ip;
    size_t break_sites[SIMPLE_MAX_LOOP_FIXUPS];
    size_t break_count;
    size_t continue_sites[SIMPLE_MAX_LOOP_FIXUPS];
    size_t continue_count;
} SimpleLoopFrame;

typedef struct {
    AivmProgram* program;
    SimpleSource sources[SIMPLE_MAX_SOURCES];
    size_t source_count;
    SimpleFnDef funcs[SIMPLE_MAX_FUNCS];
    size_t func_count;
    SimpleCallFixup fixups[SIMPLE_MAX_FIXUPS];
    size_t fixup_count;
    SimpleLoopFrame loop_frames[SIMPLE_MAX_LOOP_DEPTH];
    size_t loop_depth;
    size_t next_local_slot;
    char entry_export[64];
} SimpleCompileContext;

typedef struct {
    char names[SIMPLE_MAX_LOCALS][64];
    size_t slots[SIMPLE_MAX_LOCALS];
    size_t count;
    SimpleCompileContext* ctx;
} SimpleLocals;

static int simple_locals_lookup(SimpleLocals* locals, const char* name, size_t* out_slot, int create_if_missing);
static int simple_compile_expr_ext(
    const SimpleNodeView* node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx);
static int simple_compile_stmt_ext(
    const SimpleNodeView* node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx,
    int* out_did_return);
static int simple_compile_block_ext(
    const SimpleNodeView* block_node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx,
    int* out_did_return);

static int simple_emit_empty_string_const(AivmProgram* program)
{
    size_t empty_const_idx = 0U;
    if (program == NULL) {
        return 0;
    }
    if (!simple_add_string_const(program, "", &empty_const_idx)) {
        return simple_fail("failed adding empty string const");
    }
    return simple_emit_instruction(program, AIVM_OP_CONST, (int64_t)empty_const_idx);
}
static int simple_compile_fn_by_index(SimpleCompileContext* ctx, size_t fn_index);

static int simple_add_source(SimpleCompileContext* ctx, const char* path, const char* text, int* out_index)
{
    size_t i;
    size_t text_len;
    char* copy;
    if (ctx == NULL || path == NULL || text == NULL || out_index == NULL) {
        return 0;
    }
    for (i = 0U; i < ctx->source_count; i += 1U) {
        if (strcmp(ctx->sources[i].path, path) == 0) {
            *out_index = (int)i;
            return 1;
        }
    }
    if (ctx->source_count >= SIMPLE_MAX_SOURCES) {
        return 0;
    }
    text_len = strlen(text);
    copy = (char*)malloc(text_len + 1U);
    if (copy == NULL) {
        return 0;
    }
    memcpy(copy, text, text_len + 1U);
    if (snprintf(ctx->sources[ctx->source_count].path, sizeof(ctx->sources[ctx->source_count].path), "%s", path) >=
        (int)sizeof(ctx->sources[ctx->source_count].path)) {
        free(copy);
        return 0;
    }
    ctx->sources[ctx->source_count].text = copy;
    *out_index = (int)ctx->source_count;
    ctx->source_count += 1U;
    return 1;
}

static int simple_find_func(SimpleCompileContext* ctx, const char* name, size_t* out_index)
{
    size_t i;
    if (ctx == NULL || name == NULL || out_index == NULL) {
        return 0;
    }
    for (i = 0U; i < ctx->func_count; i += 1U) {
        if (strcmp(ctx->funcs[i].name, name) == 0) {
            *out_index = i;
            return 1;
        }
    }
    return 0;
}

static int simple_add_func(
    SimpleCompileContext* ctx,
    const char* name,
    const char* params_raw,
    const char* body_start,
    const char* body_end,
    int source_index)
{
    if (ctx == NULL || name == NULL || params_raw == NULL || body_start == NULL || body_end == NULL) {
        return 0;
    }
    if (ctx->func_count >= SIMPLE_MAX_FUNCS) {
        return 0;
    }
    if (simple_find_func(ctx, name, &(size_t){0})) {
        return 1;
    }
    if (snprintf(ctx->funcs[ctx->func_count].name, sizeof(ctx->funcs[ctx->func_count].name), "%s", name) >=
        (int)sizeof(ctx->funcs[ctx->func_count].name)) {
        return 0;
    }
    if (snprintf(
            ctx->funcs[ctx->func_count].params_raw,
            sizeof(ctx->funcs[ctx->func_count].params_raw),
            "%s",
            params_raw) >= (int)sizeof(ctx->funcs[ctx->func_count].params_raw)) {
        return 0;
    }
    ctx->funcs[ctx->func_count].body_start = body_start;
    ctx->funcs[ctx->func_count].body_end = body_end;
    ctx->funcs[ctx->func_count].source_index = source_index;
    ctx->funcs[ctx->func_count].compiled = 0;
    ctx->funcs[ctx->func_count].compiling = 0;
    ctx->funcs[ctx->func_count].entry_ip = 0U;
    ctx->func_count += 1U;
    return 1;
}

static int simple_split_params(const char* raw, char out_params[][64], size_t* out_count)
{
    const char* p;
    size_t count = 0U;
    if (raw == NULL || out_params == NULL || out_count == NULL) {
        return 0;
    }
    p = raw;
    while (*p != '\0') {
        const char* start;
        const char* end;
        while (*p == ' ' || *p == '\t' || *p == ',') {
            p += 1;
        }
        if (*p == '\0') {
            break;
        }
        start = p;
        while (*p != '\0' && *p != ',') {
            p += 1;
        }
        end = p;
        while (end > start && (end[-1] == ' ' || end[-1] == '\t')) {
            end -= 1;
        }
        if (end > start) {
            size_t n = (size_t)(end - start);
            if (count >= SIMPLE_MAX_LOCALS || n + 1U > 64U) {
                return 0;
            }
            memcpy(out_params[count], start, n);
            out_params[count][n] = '\0';
            count += 1U;
        }
        if (*p == ',') {
            p += 1;
        }
    }
    *out_count = count;
    return 1;
}

static int simple_param_count(const char* raw, size_t* out_count)
{
    char params[SIMPLE_MAX_LOCALS][64];
    return simple_split_params(raw, params, out_count);
}

static int simple_add_fixup(SimpleCompileContext* ctx, size_t instruction_index, const char* target)
{
    if (ctx == NULL || target == NULL) {
        return 0;
    }
    if (ctx->fixup_count >= SIMPLE_MAX_FIXUPS) {
        return 0;
    }
    ctx->fixups[ctx->fixup_count].instruction_index = instruction_index;
    if (snprintf(ctx->fixups[ctx->fixup_count].target, sizeof(ctx->fixups[ctx->fixup_count].target), "%s", target) >=
        (int)sizeof(ctx->fixups[ctx->fixup_count].target)) {
        return 0;
    }
    ctx->fixup_count += 1U;
    return 1;
}

static int simple_resolve_path(const char* base_file, const char* import_path, char* out_path, size_t out_path_len)
{
    char base_dir[PATH_MAX];
    if (base_file == NULL || import_path == NULL || out_path == NULL || out_path_len == 0U) {
        return 0;
    }
    if (import_path[0] == '/') {
        return snprintf(out_path, out_path_len, "%s", import_path) < (int)out_path_len;
    }
    if (!dirname_of(base_file, base_dir, sizeof(base_dir))) {
        return 0;
    }
    if (!join_path(base_dir, import_path, out_path, out_path_len)) {
        return 0;
    }
    return 1;
}

typedef struct {
    char paths[SIMPLE_MAX_SOURCES][PATH_MAX];
    size_t count;
} SourceGraphSet;

static void source_graph_hash_update_u64(uint64_t* state, const unsigned char* bytes, size_t len)
{
    size_t i;
    if (state == NULL || bytes == NULL) {
        return;
    }
    for (i = 0U; i < len; i += 1U) {
        *state ^= (uint64_t)bytes[i];
        *state *= 1099511628211ULL;
    }
}

static int source_graph_set_contains(const SourceGraphSet* set, const char* path)
{
    size_t i;
    if (set == NULL || path == NULL) {
        return 0;
    }
    for (i = 0U; i < set->count; i += 1U) {
        if (strcmp(set->paths[i], path) == 0) {
            return 1;
        }
    }
    return 0;
}

static int source_graph_set_add(SourceGraphSet* set, const char* path)
{
    if (set == NULL || path == NULL) {
        return 0;
    }
    if (source_graph_set_contains(set, path)) {
        return 1;
    }
    if (set->count >= SIMPLE_MAX_SOURCES) {
        return 0;
    }
    if (snprintf(set->paths[set->count], sizeof(set->paths[set->count]), "%s", path) >=
        (int)sizeof(set->paths[set->count])) {
        return 0;
    }
    set->count += 1U;
    return 1;
}

static void source_graph_hash_update_pair(
    uint64_t* state_a,
    uint64_t* state_b,
    const unsigned char* bytes,
    size_t len)
{
    source_graph_hash_update_u64(state_a, bytes, len);
    source_graph_hash_update_u64(state_b, bytes, len);
}

static void source_graph_hash_update_pair_text(uint64_t* state_a, uint64_t* state_b, const char* text)
{
    if (text == NULL) {
        return;
    }
    source_graph_hash_update_pair(state_a, state_b, (const unsigned char*)text, strlen(text));
}

static int source_graph_hash_file(
    const char* path,
    SourceGraphSet* visited,
    uint64_t* hash_state_a,
    uint64_t* hash_state_b)
{
    unsigned char* text_bytes = NULL;
    size_t text_len = 0U;
    char* text = NULL;
    const char* program_pos;
    const char* open_brace;
    const char* close_brace;
    const char* cursor;
    if (path == NULL || visited == NULL || hash_state_a == NULL || hash_state_b == NULL) {
        return 0;
    }
    if (source_graph_set_contains(visited, path)) {
        return 1;
    }
    if (!source_graph_set_add(visited, path)) {
        return 0;
    }
    if (!read_binary_file(path, &text_bytes, &text_len)) {
        return 0;
    }
    text = (char*)malloc(text_len + 1U);
    if (text == NULL) {
        free(text_bytes);
        return 0;
    }
    if (text_len > 0U) {
        memcpy(text, text_bytes, text_len);
    }
    text[text_len] = '\0';
    free(text_bytes);

    source_graph_hash_update_pair_text(hash_state_a, hash_state_b, "file:");
    source_graph_hash_update_pair_text(hash_state_a, hash_state_b, path);
    source_graph_hash_update_pair_text(hash_state_a, hash_state_b, "\n");
    source_graph_hash_update_pair(hash_state_a, hash_state_b, (const unsigned char*)text, text_len);
    source_graph_hash_update_pair_text(hash_state_a, hash_state_b, "\n");

    program_pos = strstr(text, "Program#");
    if (program_pos == NULL) {
        program_pos = strstr(text, "Program");
    }
    if (program_pos == NULL) {
        free(text);
        return 1;
    }
    open_brace = strchr(program_pos, '{');
    if (open_brace == NULL || !simple_find_matching_brace(open_brace, text + strlen(text), &close_brace)) {
        free(text);
        return 0;
    }
    cursor = open_brace + 1;
    while (cursor < close_brace) {
        SimpleNodeView node;
        if (!simple_parse_next_node(cursor, close_brace, &node)) {
            break;
        }
        if (strcmp(node.kind, "Import") == 0) {
            char import_path[PATH_MAX];
            char resolved[PATH_MAX];
            if (!parse_attr_span(node.attrs, "path", import_path, sizeof(import_path))) {
                free(text);
                return 0;
            }
            if (!simple_resolve_path(path, import_path, resolved, sizeof(resolved))) {
                free(text);
                return 0;
            }
            if (!source_graph_hash_file(resolved, visited, hash_state_a, hash_state_b)) {
                free(text);
                return 0;
            }
        }
        cursor = node.next;
    }
    free(text);
    return 1;
}

static int compute_source_graph_cache_key(const char* source_aos, char* out_hex, size_t out_hex_len)
{
    uint64_t hash_state_a = 1469598103934665603ULL;
    uint64_t hash_state_b = 1099511628211ULL;
    SourceGraphSet visited;
    char cap_text[64];
    if (source_aos == NULL || out_hex == NULL || out_hex_len < 33U) {
        return 0;
    }
    memset(&visited, 0, sizeof(visited));
    source_graph_hash_update_pair_text(&hash_state_a, &hash_state_b, AIRUN_NATIVE_CACHE_SCHEMA);
    source_graph_hash_update_pair_text(&hash_state_a, &hash_state_b, "|compiler=");
    source_graph_hash_update_pair_text(&hash_state_a, &hash_state_b, AIRUN_NATIVE_COMPILER_FINGERPRINT);
    source_graph_hash_update_pair_text(&hash_state_a, &hash_state_b, "|inst-cap=");
    (void)snprintf(cap_text, sizeof(cap_text), "%u", (unsigned)AIVM_PROGRAM_MAX_INSTRUCTIONS);
    source_graph_hash_update_pair_text(&hash_state_a, &hash_state_b, cap_text);
    if (!source_graph_hash_file(source_aos, &visited, &hash_state_a, &hash_state_b)) {
        return 0;
    }
    (void)snprintf(
        out_hex,
        out_hex_len,
        "%016llx%016llx",
        (unsigned long long)hash_state_a,
        (unsigned long long)hash_state_b);
    return 1;
}

static int ensure_cache_root_for_source(const char* source_aos, char* out_root, size_t out_root_len)
{
    char source_dir[PATH_MAX];
    char project_dir[PATH_MAX];
    char manifest_path[PATH_MAX];
    char parent_dir[PATH_MAX];
    char toolchain_dir[PATH_MAX];
    char cache_dir[PATH_MAX];
    if (source_aos == NULL || out_root == NULL || out_root_len == 0U) {
        return 0;
    }
    if (!dirname_of(source_aos, source_dir, sizeof(source_dir))) {
        return 0;
    }
    if (join_path(source_dir, "project.aiproj", manifest_path, sizeof(manifest_path)) &&
        file_exists(manifest_path)) {
        if (snprintf(project_dir, sizeof(project_dir), "%s", source_dir) >= (int)sizeof(project_dir)) {
            return 0;
        }
    } else if (dirname_of(source_dir, parent_dir, sizeof(parent_dir)) &&
               join_path(parent_dir, "project.aiproj", manifest_path, sizeof(manifest_path)) &&
               file_exists(manifest_path)) {
        if (snprintf(project_dir, sizeof(project_dir), "%s", parent_dir) >= (int)sizeof(project_dir)) {
            return 0;
        }
    } else {
        if (snprintf(project_dir, sizeof(project_dir), "%s", source_dir) >= (int)sizeof(project_dir)) {
            return 0;
        }
    }
    if (!join_path(project_dir, ".toolchain", toolchain_dir, sizeof(toolchain_dir)) ||
        !join_path(toolchain_dir, "cache", cache_dir, sizeof(cache_dir)) ||
        !join_path(cache_dir, "airun", out_root, out_root_len)) {
        return 0;
    }
    if (!ensure_directory(toolchain_dir) || !ensure_directory(cache_dir) || !ensure_directory(out_root)) {
        return 0;
    }
    return 1;
}

static int simple_collect_from_file(SimpleCompileContext* ctx, const char* path, int allow_entry_export)
{
    char text[131072];
    const char* program_pos;
    const char* open_brace;
    const char* close_brace;
    const char* cursor;
    int source_index = -1;

    if (ctx == NULL || path == NULL) {
        return simple_fail("collect: invalid args");
    }
    if (!read_text_file(path, text, sizeof(text))) {
        return simple_failf("collect: failed reading %s", path);
    }
    if (!simple_add_source(ctx, path, text, &source_index)) {
        return simple_failf("collect: failed adding source %s", path);
    }
    /* already collected previously */
    if (ctx->sources[(size_t)source_index].text != NULL &&
        ctx->sources[(size_t)source_index].text != text) {
        /* continue; collection idempotence is handled by func dedupe */
    }

    program_pos = strstr(ctx->sources[(size_t)source_index].text, "Program#");
    if (program_pos == NULL) {
        program_pos = strstr(ctx->sources[(size_t)source_index].text, "Program");
    }
    if (program_pos == NULL) {
        return simple_failf("collect: missing Program in %s", path);
    }
    open_brace = strchr(program_pos, '{');
    if (open_brace == NULL ||
        !simple_find_matching_brace(open_brace, ctx->sources[(size_t)source_index].text + strlen(ctx->sources[(size_t)source_index].text), &close_brace)) {
        return simple_failf("collect: malformed Program braces in %s", path);
    }
    cursor = open_brace + 1;
    while (cursor < close_brace) {
        SimpleNodeView node;
        if (!simple_parse_next_node(cursor, close_brace, &node)) {
            break;
        }
        if (strcmp(node.kind, "Import") == 0) {
            char import_path[PATH_MAX];
            char resolved[PATH_MAX];
            if (!parse_attr_span(node.attrs, "path", import_path, sizeof(import_path))) {
                return simple_failf("collect: import missing path in %s", path);
            }
            if (!simple_resolve_path(path, import_path, resolved, sizeof(resolved))) {
                return simple_failf("collect: import resolve failed for %s from %s", import_path, path);
            }
            if (!simple_collect_from_file(ctx, resolved, 0)) {
                return 0;
            }
            cursor = node.next;
            continue;
        }
        if (strcmp(node.kind, "Export") == 0) {
            char export_name[64];
            if (allow_entry_export &&
                ctx->entry_export[0] == '\0' &&
                parse_attr_span(node.attrs, "name", export_name, sizeof(export_name))) {
                (void)snprintf(ctx->entry_export, sizeof(ctx->entry_export), "%s", export_name);
            }
            cursor = node.next;
            continue;
        }
        if (strcmp(node.kind, "Let") == 0) {
            char let_name[64];
            SimpleNodeView expr;
            if (!parse_attr_span(node.attrs, "name", let_name, sizeof(let_name))) {
                return simple_failf("collect: let missing name in %s", path);
            }
            if (simple_parse_next_node(node.body_start, node.body_end, &expr) &&
                strcmp(expr.kind, "Fn") == 0) {
                char params_raw[256];
                if (!parse_attr_span(expr.attrs, "params", params_raw, sizeof(params_raw))) {
                    params_raw[0] = '\0';
                }
                if (!simple_add_func(ctx, let_name, params_raw, expr.body_start, expr.body_end, source_index)) {
                    return simple_failf("collect: failed adding function %s from %s", let_name, path);
                }
            }
            cursor = node.next;
            continue;
        }
        cursor = node.next;
    }
    return 1;
}

static int simple_locals_lookup(SimpleLocals* locals, const char* name, size_t* out_slot, int create_if_missing)
{
    size_t i;
    if (locals == NULL || name == NULL || out_slot == NULL) {
        return 0;
    }
    for (i = 0U; i < locals->count; i += 1U) {
        if (strcmp(locals->names[i], name) == 0) {
            *out_slot = locals->slots[i];
            return 1;
        }
    }
    if (!create_if_missing || locals->ctx == NULL) {
        return 0;
    }
    if (locals->count >= SIMPLE_MAX_LOCALS) {
        return simple_failf("local table capacity exceeded (name=%s max=%d)", name, SIMPLE_MAX_LOCALS);
    }
    if (locals->count >= AIVM_VM_LOCALS_CAPACITY) {
        return simple_failf(
            "vm local slot capacity exceeded (name=%s max=%d)",
            name,
            AIVM_VM_LOCALS_CAPACITY);
    }
    if (snprintf(locals->names[locals->count], sizeof(locals->names[locals->count]), "%s", name) >=
        (int)sizeof(locals->names[locals->count])) {
        return simple_failf("local name too long: %s", name);
    }
    locals->slots[locals->count] = locals->count;
    *out_slot = locals->slots[locals->count];
    locals->count += 1U;
    return 1;
}

static int simple_compile_var_expr(
    const SimpleNodeView* node,
    AivmProgram* program,
    SimpleLocals* locals)
{
    char name[64];
    size_t idx = 0U;
    if (node == NULL || program == NULL || locals == NULL) {
        return 0;
    }
    if (!parse_attr_span(node->attrs, "name", name, sizeof(name))) {
        return simple_fail("var missing name");
    }
    if (!simple_locals_lookup(locals, name, &idx, 0)) {
        return simple_failf("unknown local variable: %s", name);
    }
    return simple_emit_instruction(program, AIVM_OP_LOAD_LOCAL, (int64_t)idx);
}

static int simple_compile_call_ext(
    const SimpleNodeView* node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx,
    int pop_result)
{
    const char* c;
    SimpleNodeView arg;
    char target[128];
    int is_syscall_target;
    size_t arg_count = 0U;
    if (node == NULL || program == NULL || locals == NULL || ctx == NULL) {
        return 0;
    }
    if (!parse_attr_span(node->attrs, "target", target, sizeof(target))) {
        return simple_fail("call missing target");
    }
    is_syscall_target =
        starts_with(target, "sys.") ||
        strcmp(target, "io.print") == 0 ||
        strcmp(target, "io.write") == 0 ||
        strcmp(target, "sys.stdout.writeLine") == 0;

    if (is_syscall_target) {
        size_t target_idx = 0U;
        const char* mapped = target;
        if (strcmp(target, "io.print") == 0 || strcmp(target, "io.write") == 0) {
            mapped = "sys.stdout.writeLine";
        }
        if (!simple_add_string_const(program, mapped, &target_idx)) {
            return simple_fail("failed adding syscall target const");
        }
        if (!simple_emit_instruction(program, AIVM_OP_CONST, (int64_t)target_idx)) {
            return simple_fail("failed emitting syscall target const");
        }
        c = node->body_start;
        while (simple_parse_next_node(c, node->body_end, &arg)) {
            if (arg_count >= 32U) {
                return simple_fail("call arg count exceeds native compiler limit");
            }
            if (!simple_compile_expr_ext(&arg, program, locals, ctx)) {
                return 0;
            }
            arg_count += 1U;
            c = arg.next;
        }
        if (!simple_emit_instruction(program, AIVM_OP_CALL_SYS, (int64_t)arg_count)) {
            return simple_fail("failed emitting CALL_SYS");
        }
    } else {
        size_t fn_index;
        size_t call_inst_index;
        c = node->body_start;
        while (simple_parse_next_node(c, node->body_end, &arg)) {
            if (arg_count >= 32U) {
                return simple_fail("call arg count exceeds native compiler limit");
            }
            if (!simple_compile_expr_ext(&arg, program, locals, ctx)) {
                return 0;
            }
            arg_count += 1U;
            c = arg.next;
        }
        if (!simple_find_func(ctx, target, &fn_index)) {
            return simple_failf("unknown function target: %s", target);
        }
        {
            size_t param_count = 0U;
            if (!simple_param_count(ctx->funcs[fn_index].params_raw, &param_count)) {
                return simple_failf("failed parsing params for target: %s", target);
            }
            if (arg_count != param_count) {
                return simple_failf(
                    "call target %s expects %llu args, got %llu",
                    target,
                    (unsigned long long)param_count,
                    (unsigned long long)arg_count);
            }
        }
        call_inst_index = program->instruction_count;
        if (!simple_emit_instruction(program, AIVM_OP_CALL, 0)) {
            return simple_fail("failed emitting CALL");
        }
        if (ctx->funcs[fn_index].compiled) {
            program->instruction_storage[call_inst_index].operand_int = (int64_t)ctx->funcs[fn_index].entry_ip;
        } else {
            if (!simple_add_fixup(ctx, call_inst_index, target)) {
                return simple_fail("failed storing call fixup");
            }
        }
    }

    if (pop_result) {
        if (!simple_emit_instruction(program, AIVM_OP_POP, 0)) {
            return simple_fail("failed emitting POP");
        }
    }
    return 1;
}

static int simple_compile_if_expr_ext(
    const SimpleNodeView* node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx)
{
    SimpleNodeView cond_node;
    SimpleNodeView then_node;
    SimpleNodeView else_node;
    size_t jump_false_ip;
    size_t jump_end_ip;
    if (!simple_parse_next_node(node->body_start, node->body_end, &cond_node) ||
        !simple_parse_next_node(cond_node.next, node->body_end, &then_node) ||
        !simple_parse_next_node(then_node.next, node->body_end, &else_node)) {
        return simple_fail("if expr shape invalid");
    }
    if (!simple_compile_expr_ext(&cond_node, program, locals, ctx)) {
        return 0;
    }
    jump_false_ip = program->instruction_count;
    if (!simple_emit_instruction(program, AIVM_OP_JUMP_IF_FALSE, 0)) {
        return simple_fail("failed emitting JUMP_IF_FALSE");
    }
    if (!simple_compile_expr_ext(&then_node, program, locals, ctx)) {
        return 0;
    }
    jump_end_ip = program->instruction_count;
    if (!simple_emit_instruction(program, AIVM_OP_JUMP, 0)) {
        return simple_fail("failed emitting JUMP");
    }
    program->instruction_storage[jump_false_ip].operand_int = (int64_t)program->instruction_count;
    if (!simple_compile_expr_ext(&else_node, program, locals, ctx)) {
        return 0;
    }
    program->instruction_storage[jump_end_ip].operand_int = (int64_t)program->instruction_count;
    return 1;
}

static int simple_compile_expr_ext(
    const SimpleNodeView* node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx)
{
    if (node == NULL || program == NULL || locals == NULL || ctx == NULL) {
        return 0;
    }
    if (strcmp(node->kind, "Call") == 0) {
        return simple_compile_call_ext(node, program, locals, ctx, 0);
    }
    if (strcmp(node->kind, "Var") == 0) {
        return simple_compile_var_expr(node, program, locals);
    }
    if (strcmp(node->kind, "StrConcat") == 0) {
        const char* c = node->body_start;
        int first = 1;
        SimpleNodeView child;
        while (simple_parse_next_node(c, node->body_end, &child)) {
            if (!simple_compile_expr_ext(&child, program, locals, ctx)) {
                return 0;
            }
            if (!first && !simple_emit_instruction(program, AIVM_OP_STR_CONCAT, 0)) {
                return simple_fail("failed emitting STR_CONCAT");
            }
            first = 0;
            c = child.next;
        }
        return !first;
    }
    if (starts_with(node->kind, "Field")) {
        char key[256];
        SimpleNodeView value_node;
        size_t key_idx = 0U;
        if (!parse_attr_span(node->attrs, "key", key, sizeof(key))) {
            return simple_fail("field missing key");
        }
        if (!simple_parse_next_node(node->body_start, node->body_end, &value_node)) {
            return simple_fail("field missing value");
        }
        if (!simple_add_string_const(program, key, &key_idx) ||
            !simple_emit_instruction(program, AIVM_OP_CONST, (int64_t)key_idx) ||
            !simple_compile_expr_ext(&value_node, program, locals, ctx) ||
            !simple_emit_instruction(program, AIVM_OP_MAKE_FIELD_STRING, 0)) {
            return 0;
        }
        return 1;
    }
    if (starts_with(node->kind, "Map")) {
        const char* c = node->body_start;
        SimpleNodeView field_node;
        size_t field_count = 0U;
        while (simple_parse_next_node(c, node->body_end, &field_node)) {
            if (strcmp(field_node.kind, "Field") != 0) {
                return simple_failf("map child must be Field, got %s", field_node.kind);
            }
            if (!simple_compile_expr_ext(&field_node, program, locals, ctx)) {
                return 0;
            }
            field_count += 1U;
            c = field_node.next;
        }
        if (!simple_emit_instruction(program, AIVM_OP_PUSH_INT, (int64_t)field_count) ||
            !simple_emit_instruction(program, AIVM_OP_MAKE_MAP, 0)) {
            return 0;
        }
        return 1;
    }
    if (starts_with(node->kind, "MakeBlock")) {
        SimpleNodeView id_expr;
        if (!simple_parse_next_node(node->body_start, node->body_end, &id_expr)) {
            return simple_fail("MakeBlock missing id expression");
        }
        if (!simple_compile_expr_ext(&id_expr, program, locals, ctx) ||
            !simple_emit_instruction(program, AIVM_OP_MAKE_BLOCK, 0)) {
            return 0;
        }
        return 1;
    }
    if (starts_with(node->kind, "AppendChild")) {
        SimpleNodeView base_node;
        SimpleNodeView child_node;
        if (!simple_parse_next_node(node->body_start, node->body_end, &base_node) ||
            !simple_parse_next_node(base_node.next, node->body_end, &child_node)) {
            return simple_fail("AppendChild requires (node,child)");
        }
        if (!simple_compile_expr_ext(&base_node, program, locals, ctx) ||
            !simple_compile_expr_ext(&child_node, program, locals, ctx) ||
            !simple_emit_instruction(program, AIVM_OP_APPEND_CHILD, 0)) {
            return 0;
        }
        return 1;
    }
    if (starts_with(node->kind, "MakeLitString") || starts_with(node->kind, "MakeLitInt") ||
        starts_with(node->kind, "MakeFieldString")) {
        SimpleNodeView id_node;
        SimpleNodeView value_node;
        AivmOpcode make_opcode = AIVM_OP_MAKE_LIT_INT;
        if (starts_with(node->kind, "MakeLitString")) {
            make_opcode = AIVM_OP_MAKE_LIT_STRING;
        } else if (starts_with(node->kind, "MakeFieldString")) {
            make_opcode = AIVM_OP_MAKE_FIELD_STRING;
        }
        if (!simple_parse_next_node(node->body_start, node->body_end, &id_node) ||
            !simple_parse_next_node(id_node.next, node->body_end, &value_node)) {
            return simple_failf("%s requires (id,value)", node->kind);
        }
        if (!simple_compile_expr_ext(&id_node, program, locals, ctx) ||
            !simple_compile_expr_ext(&value_node, program, locals, ctx) ||
            !simple_emit_instruction(program, make_opcode, 0)) {
            return 0;
        }
        return 1;
    }
    if (starts_with(node->kind, "MakeErr")) {
        SimpleNodeView id_node;
        SimpleNodeView code_node;
        SimpleNodeView message_node;
        SimpleNodeView node_id_node;
        if (!simple_parse_next_node(node->body_start, node->body_end, &id_node) ||
            !simple_parse_next_node(id_node.next, node->body_end, &code_node) ||
            !simple_parse_next_node(code_node.next, node->body_end, &message_node) ||
            !simple_parse_next_node(message_node.next, node->body_end, &node_id_node)) {
            return simple_fail("MakeErr requires (id,code,message,nodeId)");
        }
        if (!simple_compile_expr_ext(&id_node, program, locals, ctx) ||
            !simple_compile_expr_ext(&code_node, program, locals, ctx) ||
            !simple_compile_expr_ext(&message_node, program, locals, ctx) ||
            !simple_compile_expr_ext(&node_id_node, program, locals, ctx) ||
            !simple_emit_instruction(program, AIVM_OP_MAKE_ERR, 0)) {
            return 0;
        }
        return 1;
    }
    if (strcmp(node->kind, "Add") == 0 || strcmp(node->kind, "Eq") == 0 || strcmp(node->kind, "ToString") == 0 ||
        strcmp(node->kind, "AttrCount") == 0 || strcmp(node->kind, "AttrKey") == 0 ||
        strcmp(node->kind, "AttrValueString") == 0 || strcmp(node->kind, "AttrValueInt") == 0 ||
        strcmp(node->kind, "AttrValueBool") == 0 || strcmp(node->kind, "AttrValueKind") == 0 ||
        strcmp(node->kind, "ChildCount") == 0 || strcmp(node->kind, "ChildAt") == 0 ||
        strcmp(node->kind, "NodeKind") == 0 || strcmp(node->kind, "NodeId") == 0) {
        const char* c = node->body_start;
        SimpleNodeView first;
        SimpleNodeView second;
        if (strcmp(node->kind, "ToString") == 0 ||
            strcmp(node->kind, "AttrCount") == 0 ||
            strcmp(node->kind, "ChildCount") == 0 ||
            strcmp(node->kind, "NodeKind") == 0 ||
            strcmp(node->kind, "NodeId") == 0) {
            if (!simple_parse_next_node(c, node->body_end, &first)) {
                return simple_failf("%s missing arg", node->kind);
            }
            if (!simple_compile_expr_ext(&first, program, locals, ctx)) {
                return 0;
            }
            if (strcmp(node->kind, "ToString") == 0) return simple_emit_instruction(program, AIVM_OP_TO_STRING, 0);
            if (strcmp(node->kind, "AttrCount") == 0) return simple_emit_instruction(program, AIVM_OP_ATTR_COUNT, 0);
            if (strcmp(node->kind, "ChildCount") == 0) return simple_emit_instruction(program, AIVM_OP_CHILD_COUNT, 0);
            if (strcmp(node->kind, "NodeKind") == 0) return simple_emit_instruction(program, AIVM_OP_NODE_KIND, 0);
            return simple_emit_instruction(program, AIVM_OP_NODE_ID, 0);
        }
        if (!simple_parse_next_node(c, node->body_end, &first) ||
            !simple_parse_next_node(first.next, node->body_end, &second)) {
            return simple_failf("%s missing args", node->kind);
        }
        if (!simple_compile_expr_ext(&first, program, locals, ctx) ||
            !simple_compile_expr_ext(&second, program, locals, ctx)) {
            return 0;
        }
        if (strcmp(node->kind, "Add") == 0) return simple_emit_instruction(program, AIVM_OP_ADD_INT, 0);
        if (strcmp(node->kind, "Eq") == 0) return simple_emit_instruction(program, AIVM_OP_EQ, 0);
        if (strcmp(node->kind, "AttrKey") == 0) return simple_emit_instruction(program, AIVM_OP_ATTR_KEY, 0);
        if (strcmp(node->kind, "AttrValueString") == 0) return simple_emit_instruction(program, AIVM_OP_ATTR_VALUE_STRING, 0);
        if (strcmp(node->kind, "AttrValueInt") == 0) return simple_emit_instruction(program, AIVM_OP_ATTR_VALUE_INT, 0);
        if (strcmp(node->kind, "AttrValueBool") == 0) return simple_emit_instruction(program, AIVM_OP_ATTR_VALUE_BOOL, 0);
        if (strcmp(node->kind, "AttrValueKind") == 0) return simple_emit_instruction(program, AIVM_OP_ATTR_VALUE_KIND, 0);
        return simple_emit_instruction(program, AIVM_OP_CHILD_AT, 0);
    }
    if (strcmp(node->kind, "If") == 0) {
        return simple_compile_if_expr_ext(node, program, locals, ctx);
    }
    if (strcmp(node->kind, "Block") == 0) {
        const char* c = node->body_start;
        SimpleNodeView child;
        int emitted_value = 0;
        while (simple_parse_next_node(c, node->body_end, &child)) {
            SimpleNodeView next_child;
            int has_next = simple_parse_next_node(child.next, node->body_end, &next_child);
            if (has_next) {
                int did_return = 0;
                if (!simple_compile_stmt_ext(&child, program, locals, ctx, &did_return)) {
                    return 0;
                }
                if (did_return) {
                    return 1;
                }
                c = child.next;
                continue;
            }
            if (strcmp(child.kind, "Let") == 0 ||
                strcmp(child.kind, "Return") == 0 ||
                strcmp(child.kind, "Break") == 0 ||
                strcmp(child.kind, "Continue") == 0 ||
                strcmp(child.kind, "Loop") == 0) {
                int did_return = 0;
                if (!simple_compile_stmt_ext(&child, program, locals, ctx, &did_return)) {
                    return 0;
                }
                if (did_return) {
                    return 1;
                }
                if (!simple_emit_empty_string_const(program)) {
                    return 0;
                }
            } else if (!simple_compile_expr_ext(&child, program, locals, ctx)) {
                return 0;
            }
            emitted_value = 1;
            c = child.next;
        }
        if (!emitted_value) {
            if (!simple_emit_empty_string_const(program)) {
                return 0;
            }
        }
        return 1;
    }
    {
        const char* trace = getenv("AIVM_NATIVE_BUILD_TRACE");
        if (trace != NULL && trace[0] != '\0') {
            fprintf(stderr, "[airun-native-compile] fallback-expr kind=%s\n", node->kind);
        }
    }
    return simple_compile_expr_node(node, program, locals->names, &locals->count);
}

static int simple_compile_if_stmt_ext(
    const SimpleNodeView* node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx,
    int* out_did_return)
{
    SimpleNodeView cond_node;
    SimpleNodeView then_node;
    SimpleNodeView else_node;
    size_t jump_false_ip;
    size_t jump_end_ip;
    int then_return = 0;
    int else_return = 0;
    if (!simple_parse_next_node(node->body_start, node->body_end, &cond_node) ||
        !simple_parse_next_node(cond_node.next, node->body_end, &then_node) ||
        !simple_parse_next_node(then_node.next, node->body_end, &else_node)) {
        return simple_fail("if stmt shape invalid");
    }
    if (!simple_compile_expr_ext(&cond_node, program, locals, ctx)) {
        return 0;
    }
    jump_false_ip = program->instruction_count;
    if (!simple_emit_instruction(program, AIVM_OP_JUMP_IF_FALSE, 0)) {
        return 0;
    }
    if (strcmp(then_node.kind, "Block") == 0) {
        if (!simple_compile_block_ext(&then_node, program, locals, ctx, &then_return)) {
            return 0;
        }
    } else if (!simple_compile_expr_ext(&then_node, program, locals, ctx) ||
               !simple_emit_instruction(program, AIVM_OP_POP, 0)) {
        return 0;
    }
    jump_end_ip = program->instruction_count;
    if (!simple_emit_instruction(program, AIVM_OP_JUMP, 0)) {
        return 0;
    }
    program->instruction_storage[jump_false_ip].operand_int = (int64_t)program->instruction_count;
    if (strcmp(else_node.kind, "Block") == 0) {
        if (!simple_compile_block_ext(&else_node, program, locals, ctx, &else_return)) {
            return 0;
        }
    } else if (!simple_compile_expr_ext(&else_node, program, locals, ctx) ||
               !simple_emit_instruction(program, AIVM_OP_POP, 0)) {
        return 0;
    }
    program->instruction_storage[jump_end_ip].operand_int = (int64_t)program->instruction_count;
    if (out_did_return != NULL) {
        *out_did_return = then_return && else_return;
    }
    return 1;
}

static int simple_compile_loop_stmt_ext(
    const SimpleNodeView* node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx,
    int* out_did_return)
{
    SimpleNodeView body_node;
    SimpleLoopFrame* loop_frame;
    size_t loop_start_ip;
    size_t i;
    int body_return = 0;
    if (out_did_return != NULL) {
        *out_did_return = 0;
    }
    if (!simple_parse_next_node(node->body_start, node->body_end, &body_node)) {
        return simple_fail("loop stmt shape invalid");
    }
    if (ctx->loop_depth >= SIMPLE_MAX_LOOP_DEPTH) {
        return simple_fail("loop nesting depth exceeded");
    }

    loop_start_ip = program->instruction_count;
    loop_frame = &ctx->loop_frames[ctx->loop_depth];
    memset(loop_frame, 0, sizeof(*loop_frame));
    loop_frame->start_ip = loop_start_ip;
    ctx->loop_depth += 1U;
    if (strcmp(body_node.kind, "Block") == 0) {
        if (!simple_compile_block_ext(&body_node, program, locals, ctx, &body_return)) {
            ctx->loop_depth -= 1U;
            return 0;
        }
    } else if (!simple_compile_expr_ext(&body_node, program, locals, ctx) ||
               !simple_emit_instruction(program, AIVM_OP_POP, 0)) {
        ctx->loop_depth -= 1U;
        return 0;
    }
    if (!simple_emit_instruction(program, AIVM_OP_JUMP, (int64_t)loop_start_ip)) {
        ctx->loop_depth -= 1U;
        return 0;
    }
    for (i = 0U; i < loop_frame->break_count; i += 1U) {
        program->instruction_storage[loop_frame->break_sites[i]].operand_int = (int64_t)program->instruction_count;
    }
    for (i = 0U; i < loop_frame->continue_count; i += 1U) {
        program->instruction_storage[loop_frame->continue_sites[i]].operand_int = (int64_t)loop_start_ip;
    }
    ctx->loop_depth -= 1U;
    if (out_did_return != NULL) {
        (void)body_return;
        *out_did_return = 0;
    }
    return 1;
}

static int simple_compile_stmt_ext(
    const SimpleNodeView* node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx,
    int* out_did_return)
{
    if (out_did_return != NULL) {
        *out_did_return = 0;
    }
    if (node == NULL || program == NULL || locals == NULL || ctx == NULL) {
        return 0;
    }
    if (strcmp(node->kind, "Let") == 0) {
        char name[64];
        size_t slot;
        SimpleNodeView expr;
        if (!parse_attr_span(node->attrs, "name", name, sizeof(name))) {
            return simple_fail("let stmt missing name");
        }
        if (!simple_locals_lookup(locals, name, &slot, 1)) {
            return simple_failf("local allocation failed for %s", name);
        }
        if (!simple_parse_next_node(node->body_start, node->body_end, &expr)) {
            return simple_fail("let stmt missing expression");
        }
        if (!simple_compile_expr_ext(&expr, program, locals, ctx)) {
            return 0;
        }
        if (!simple_emit_instruction(program, AIVM_OP_STORE_LOCAL, (int64_t)slot)) {
            return simple_fail("let stmt STORE_LOCAL failed");
        }
        return 1;
    }
    if (strcmp(node->kind, "Call") == 0) {
        return simple_compile_call_ext(node, program, locals, ctx, 1);
    }
    if (strcmp(node->kind, "Break") == 0) {
        SimpleLoopFrame* loop_frame;
        size_t jump_ip;
        if (ctx->loop_depth == 0U) {
            return simple_fail("break used outside loop");
        }
        loop_frame = &ctx->loop_frames[ctx->loop_depth - 1U];
        if (loop_frame->break_count >= SIMPLE_MAX_LOOP_FIXUPS) {
            return simple_fail("too many break statements in loop");
        }
        jump_ip = program->instruction_count;
        if (!simple_emit_instruction(program, AIVM_OP_JUMP, 0)) {
            return 0;
        }
        loop_frame->break_sites[loop_frame->break_count] = jump_ip;
        loop_frame->break_count += 1U;
        return 1;
    }
    if (strcmp(node->kind, "Continue") == 0) {
        SimpleLoopFrame* loop_frame;
        size_t jump_ip;
        if (ctx->loop_depth == 0U) {
            return simple_fail("continue used outside loop");
        }
        loop_frame = &ctx->loop_frames[ctx->loop_depth - 1U];
        if (loop_frame->continue_count >= SIMPLE_MAX_LOOP_FIXUPS) {
            return simple_fail("too many continue statements in loop");
        }
        jump_ip = program->instruction_count;
        if (!simple_emit_instruction(program, AIVM_OP_JUMP, 0)) {
            return 0;
        }
        loop_frame->continue_sites[loop_frame->continue_count] = jump_ip;
        loop_frame->continue_count += 1U;
        return 1;
    }
    if (strcmp(node->kind, "Return") == 0) {
        SimpleNodeView expr;
        if (!simple_parse_next_node(node->body_start, node->body_end, &expr)) {
            return simple_fail("return missing expression");
        }
        if (!simple_compile_expr_ext(&expr, program, locals, ctx)) {
            return 0;
        }
        if (!simple_emit_instruction(program, AIVM_OP_RETURN, 0)) {
            return simple_fail("return emit failed");
        }
        if (out_did_return != NULL) {
            *out_did_return = 1;
        }
        return 1;
    }
    if (strcmp(node->kind, "If") == 0) {
        return simple_compile_if_stmt_ext(node, program, locals, ctx, out_did_return);
    }
    if (strcmp(node->kind, "Loop") == 0) {
        return simple_compile_loop_stmt_ext(node, program, locals, ctx, out_did_return);
    }
    if (strcmp(node->kind, "Block") == 0) {
        return simple_compile_block_ext(node, program, locals, ctx, out_did_return);
    }
    if (!simple_compile_expr_ext(node, program, locals, ctx)) {
        return 0;
    }
    return simple_emit_instruction(program, AIVM_OP_POP, 0);
}

static int simple_compile_block_ext(
    const SimpleNodeView* block_node,
    AivmProgram* program,
    SimpleLocals* locals,
    SimpleCompileContext* ctx,
    int* out_did_return)
{
    const char* c;
    int did_return = 0;
    if (out_did_return != NULL) {
        *out_did_return = 0;
    }
    if (block_node == NULL || program == NULL || locals == NULL || ctx == NULL) {
        return 0;
    }
    c = block_node->body_start;
    while (simple_parse_next_node(c, block_node->body_end, &(SimpleNodeView){0})) {
        SimpleNodeView child;
        if (!simple_parse_next_node(c, block_node->body_end, &child)) {
            break;
        }
        if (!simple_compile_stmt_ext(&child, program, locals, ctx, &did_return)) {
            return 0;
        }
        if (did_return) {
            if (out_did_return != NULL) {
                *out_did_return = 1;
            }
            return 1;
        }
        c = child.next;
    }
    return 1;
}

static int simple_compile_fn_by_index(SimpleCompileContext* ctx, size_t fn_index)
{
    SimpleFnDef* fn;
    SimpleLocals locals;
    char params[SIMPLE_MAX_LOCALS][64];
    size_t param_count = 0U;
    size_t i;
    SimpleNodeView block_node;
    int did_return = 0;
    const char* trace;
    size_t before_count = 0U;
    if (ctx == NULL || fn_index >= ctx->func_count || ctx->program == NULL) {
        return 0;
    }
    fn = &ctx->funcs[fn_index];
    trace = getenv("AIVM_NATIVE_BUILD_TRACE");
    if (fn->compiled) {
        return 1;
    }
    if (fn->compiling) {
        return 1;
    }
    fn->compiling = 1;
    before_count = ctx->program->instruction_count;
    fn->entry_ip = ctx->program->instruction_count;
    if (trace != NULL && trace[0] != '\0') {
        fprintf(
            stderr,
            "[airun-native-compile] fn-start=%s inst_before=%llu\n",
            fn->name,
            (unsigned long long)before_count);
    }

    memset(&locals, 0, sizeof(locals));
    locals.ctx = ctx;
    if (!simple_split_params(fn->params_raw, params, &param_count)) {
        return simple_failf("invalid params for function %s", fn->name);
    }
    for (i = 0U; i < param_count; i += 1U) {
        size_t slot;
        if (!simple_locals_lookup(&locals, params[i], &slot, 1)) {
            return simple_failf("failed to allocate param %s", params[i]);
        }
    }
    for (i = param_count; i > 0U; i -= 1U) {
        size_t slot = 0U;
        if (!simple_locals_lookup(&locals, params[i - 1U], &slot, 0) ||
            !simple_emit_instruction(ctx->program, AIVM_OP_STORE_LOCAL, (int64_t)slot)) {
            return simple_failf("failed param prologue for %s", fn->name);
        }
    }
    if (!simple_parse_next_node(fn->body_start, fn->body_end, &block_node) ||
        strcmp(block_node.kind, "Block") != 0) {
        return simple_failf("function %s missing Block body", fn->name);
    }
    if (!simple_compile_block_ext(&block_node, ctx->program, &locals, ctx, &did_return)) {
        return 0;
    }
    if (!did_return) {
        if (!simple_emit_instruction(ctx->program, AIVM_OP_RETURN, 0)) {
            return simple_failf("failed implicit return for %s", fn->name);
        }
    }
    fn->compiled = 1;
    fn->compiling = 0;
    if (trace != NULL && trace[0] != '\0') {
        fprintf(
            stderr,
            "[airun-native-compile] fn=%s inst_before=%llu inst_after=%llu\n",
            fn->name,
            (unsigned long long)before_count,
            (unsigned long long)ctx->program->instruction_count);
    }
    return 1;
}

static int parse_simple_program_graph_to_program_file(const char* aos_path, AivmProgram* out_program)
{
    SimpleCompileContext ctx;
    size_t entry_index;
    size_t bootstrap_call_ip;
    size_t i;
    (void)snprintf(g_simple_last_error, sizeof(g_simple_last_error), "%s", "graph compile failed (unknown)");
    if (aos_path == NULL || out_program == NULL) {
        return simple_fail("graph compile: invalid args");
    }
    memset(&ctx, 0, sizeof(ctx));
    ctx.program = out_program;
    ctx.next_local_slot = 0U;

    if (!simple_collect_from_file(&ctx, aos_path, 1)) {
        return 0;
    }
    if (ctx.entry_export[0] == '\0') {
        (void)snprintf(ctx.entry_export, sizeof(ctx.entry_export), "%s", "start");
    }
    if (!simple_find_func(&ctx, ctx.entry_export, &entry_index)) {
        return simple_failf("entry export '%s' not found", ctx.entry_export);
    }

    aivm_program_clear(out_program);
    out_program->instructions = out_program->instruction_storage;
    out_program->constants = out_program->constant_storage;
    out_program->format_version = 1U;
    out_program->format_flags = 0U;

    {
        char params[SIMPLE_MAX_LOCALS][64];
        size_t param_count = 0U;
        if (!simple_split_params(ctx.funcs[entry_index].params_raw, params, &param_count)) {
            return simple_failf("graph compile: invalid entry params for %s", ctx.funcs[entry_index].name);
        }
        if (param_count > 0U) {
            size_t argv_target = 0U;
            if (!simple_add_string_const(out_program, "sys.process.args", &argv_target) ||
                !simple_emit_instruction(out_program, AIVM_OP_CONST, (int64_t)argv_target) ||
                !simple_emit_instruction(out_program, AIVM_OP_CALL_SYS, 0)) {
                return simple_fail("graph compile: failed emitting argv bootstrap");
            }
        }
    }
    bootstrap_call_ip = out_program->instruction_count;
    if (!simple_emit_instruction(out_program, AIVM_OP_CALL, 0) ||
        !simple_emit_instruction(out_program, AIVM_OP_HALT, 0)) {
        return simple_fail("graph compile: failed emitting bootstrap call/halt");
    }

    if (!simple_compile_fn_by_index(&ctx, entry_index)) {
        return 0;
    }
    out_program->instruction_storage[bootstrap_call_ip].operand_int = (int64_t)ctx.funcs[entry_index].entry_ip;

    for (i = 0U; i < ctx.fixup_count; i += 1U) {
        size_t target_index;
        if (!simple_find_func(&ctx, ctx.fixups[i].target, &target_index)) {
            return simple_failf("unresolved call target %s", ctx.fixups[i].target);
        }
        if (!ctx.funcs[target_index].compiled) {
            if (!simple_compile_fn_by_index(&ctx, target_index)) {
                return 0;
            }
        }
        if (ctx.fixups[i].instruction_index >= out_program->instruction_count) {
            return simple_fail("call fixup index out of range");
        }
        out_program->instruction_storage[ctx.fixups[i].instruction_index].operand_int =
            (int64_t)ctx.funcs[target_index].entry_ip;
    }

    for (i = 0U; i < ctx.source_count; i += 1U) {
        free(ctx.sources[i].text);
        ctx.sources[i].text = NULL;
    }
    if (out_program->instruction_count == 0U) {
        return simple_fail("graph compile: produced empty program");
    }
    return 1;
}

static int run_native_simple_program_aos(
    const char* aos_path,
    const char* const* process_argv,
    size_t process_argv_count,
    const NativeDebugOptions* debug_options)
{
    AivmProgram program;

    if (!parse_simple_program_aos_to_program_file(aos_path, &program)) {
        return -1;
    }
    return run_native_compiled_program(
        &program,
        "Native simple source execution failed.",
        process_argv,
        process_argv_count,
        debug_options);
}

static int run_native_bytecode_aos(
    const char* aos_path,
    const char* const* process_argv,
    size_t process_argv_count,
    const NativeDebugOptions* debug_options)
{
    AivmProgram program;

    if (!parse_bytecode_aos_to_program_file(aos_path, &program, 0)) {
        return -1;
    }
    return run_native_compiled_program(
        &program,
        "Native bytecode program execution failed.",
        process_argv,
        process_argv_count,
        debug_options);
}

static int run_native_bundle(
    const char* bundle_path,
    const char* const* process_argv,
    size_t process_argv_count,
    const NativeDebugOptions* debug_options)
{
    return run_native_bytecode_aos(bundle_path, process_argv, process_argv_count, debug_options);
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
    int use_cache;
} RunTarget;

static int derive_build_out_dir(const char* program_input, char* out_dir, size_t out_dir_len);
static int build_input_to_aibc1(
    const char* program_input,
    const char* out_dir,
    char* out_app_path,
    size_t out_app_path_len,
    int use_cache);

static int parse_run_target(int argc, char** argv, int start_index, RunTarget* out_target)
{
    int i;
    const char* program_path = NULL;
    int app_arg_start = -1;
    int use_cache = 1;

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
        if (strcmp(arg, "--no-cache") == 0 && app_arg_start < 0) {
            use_cache = 0;
            continue;
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
    out_target->use_cache = use_cache;
    return 0;
}

static int run_via_resolved_input(
    const char* input,
    const char* const* process_argv,
    size_t process_argv_count,
    const NativeDebugOptions* debug_options)
{
    char resolved[PATH_MAX];
    char source_aos[PATH_MAX];
    if (input != NULL && ends_with(input, ".aibundle") && file_exists(input)) {
        int rc = run_native_bundle(input, process_argv, process_argv_count, debug_options);
        if (rc >= 0) {
            return rc;
        }
        fprintf(stderr,
            "Err#err1(code=DEV008 message=\"Native bundle input uses unsupported bytecode fields. Provide supported Bytecode# shape.\" nodeId=program)\n");
        return 2;
    }
    if (resolve_input_to_aibc1(input, resolved, sizeof(resolved))) {
        return run_native_aibc1(resolved, process_argv, process_argv_count, debug_options);
    }
    if (resolve_input_to_aos(input, source_aos, sizeof(source_aos))) {
        int rc = run_native_bytecode_aos(source_aos, process_argv, process_argv_count, debug_options);
        if (rc >= 0) {
            return rc;
        }
        if (source_file_looks_like_bytecode_aos(source_aos)) {
            fprintf(stderr,
                "Err#err1(code=DEV008 message=\"Native bytecode AOS input uses unsupported fields. Build precompiled .aibc1 or use supported instruction attributes.\" nodeId=program)\n");
            return 2;
        }
        rc = run_native_simple_program_aos(source_aos, process_argv, process_argv_count, debug_options);
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
    int parse_rc = 0;
    char out_dir[PATH_MAX];
    char app_path[PATH_MAX];
    int build_rc;
    memset(&target, 0, sizeof(target));
    parse_rc = parse_run_target(argc, argv, 2, &target);
    if (parse_rc != 0) {
        return parse_rc;
    }

    if (target.program_path != NULL &&
        !ends_with(target.program_path, ".aibc1") &&
        !ends_with(target.program_path, ".aibundle")) {
        if (!derive_build_out_dir(target.program_path, out_dir, sizeof(out_dir))) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Native build failed: could not derive output directory.\" nodeId=build)\n");
            return 2;
        }
        build_rc = build_input_to_aibc1(
            target.program_path,
            out_dir,
            app_path,
            sizeof(app_path),
            target.use_cache);
        if (build_rc != 1) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Native build failed: %s\" nodeId=build)\n",
                native_build_error());
            return 2;
        }
        return run_native_aibc1(
            app_path,
            (const char* const*)&argv[target.app_arg_start],
            (size_t)target.app_arg_count,
            NULL);
    }

    return run_via_resolved_input(
        target.program_path,
        (const char* const*)&argv[target.app_arg_start],
        (size_t)target.app_arg_count,
        NULL);
}

static AIRUN_MAYBE_UNUSED int handle_serve(int argc, char** argv)
{
    (void)argc;
    (void)argv;
    fprintf(stderr,
        "Err#err1(code=DEV008 message=\"serve is not part of native airun runtime surface.\" nodeId=command)\n");
    return 2;
}

static AIRUN_MAYBE_UNUSED int handle_debug(int argc, char** argv)
{
    if (argc >= 3 && strcmp(argv[2], "run") == 0) {
        const char* program_path = NULL;
        int app_arg_start = -1;
        int i;
        const char* out_dir = NULL;
        const char* debug_mode = "off";
        NativeDebugOptions debug_options;
        int rc;
        char config_path[PATH_MAX];
        debug_options.emit_bundle = 0;
        debug_options.out_dir = NULL;
        debug_options.input_path = NULL;
        debug_options.debug_mode = "off";

        for (i = 3; i < argc; i += 1) {
            const char* arg = argv[i];
            if (strcmp(arg, "--") == 0) {
                app_arg_start = i + 1;
                break;
            }
            if (strcmp(arg, "--out") == 0 && app_arg_start < 0) {
                if ((i + 1) >= argc) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"Missing --out value.\" nodeId=argv)\n");
                    return 2;
                }
                out_dir = argv[i + 1];
                i += 1;
                continue;
            }
            if (starts_with(arg, "--out=") && app_arg_start < 0) {
                out_dir = arg + 6;
                continue;
            }
            if (strcmp(arg, "--debug-mode") == 0 && app_arg_start < 0) {
                if ((i + 1) >= argc) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"Missing --debug-mode value.\" nodeId=argv)\n");
                    return 2;
                }
                debug_mode = argv[i + 1];
                i += 1;
                continue;
            }
            if (starts_with(arg, "--debug-mode=") && app_arg_start < 0) {
                debug_mode = arg + 13;
                continue;
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
            if (program_path != NULL && app_arg_start < 0) {
                if (strcmp(arg, "--out") == 0) {
                    if ((i + 1) >= argc) {
                        fprintf(stderr,
                            "Err#err1(code=RUN001 message=\"Missing --out value.\" nodeId=argv)\n");
                        return 2;
                    }
                    out_dir = argv[i + 1];
                    i += 1;
                    continue;
                }
                if (starts_with(arg, "--out=")) {
                    out_dir = arg + 6;
                    continue;
                }
                if (strcmp(arg, "--debug-mode") == 0) {
                    if ((i + 1) >= argc) {
                        fprintf(stderr,
                            "Err#err1(code=RUN001 message=\"Missing --debug-mode value.\" nodeId=argv)\n");
                        return 2;
                    }
                    debug_mode = argv[i + 1];
                    i += 1;
                    continue;
                }
                if (starts_with(arg, "--debug-mode=")) {
                    debug_mode = arg + 13;
                    continue;
                }
                if (starts_with(arg, "--vm=")) {
                    const char* mode = arg + 5;
                    if (strcmp(mode, "c") != 0 && !is_reserved_cv_selector(mode)) {
                        return print_unsupported_vm_mode(mode);
                    }
                    continue;
                }
            }
            if (app_arg_start < 0) {
                app_arg_start = i;
            }
            break;
        }
        if (program_path == NULL) {
            program_path = ".";
        }
        if (app_arg_start < 0 || app_arg_start > argc) {
            app_arg_start = argc;
        }
        if (out_dir != NULL) {
            debug_options.emit_bundle = 1;
            debug_options.out_dir = out_dir;
            debug_options.input_path = program_path;
        }
        debug_options.debug_mode = debug_mode;
        rc = run_via_resolved_input(
            program_path,
            (const char* const*)&argv[app_arg_start],
            (size_t)(argc - app_arg_start),
            &debug_options);
        if (out_dir != NULL &&
            join_path(out_dir, "config.toml", config_path, sizeof(config_path)) &&
            !file_exists(config_path)) {
            (void)write_native_debug_bundle(&debug_options, NULL, NULL, rc, 1, "native-debug-run-pre-exec-failure");
        }
        return rc;
    }
    fprintf(stderr,
        "Err#err1(code=DEV008 message=\"Native debug supports only: debug run <program>\" nodeId=command)\n");
    return 2;
}

static AIRUN_MAYBE_UNUSED int handle_repl(void)
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
        return run_via_resolved_input(line + 4, NULL, 0U, NULL);
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

static AIRUN_MAYBE_UNUSED int handle_bench(int argc, char** argv)
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
        if (!parse_bytecode_aos_to_program_text(bytecode_bench_source, &program, 0)) {
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
        if (!parse_bytecode_aos_to_program_text(app_bundle_source, &bundle_program, 0) ||
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

static int derive_build_out_dir(const char* program_input, char* out_dir, size_t out_dir_len)
{
    if (program_input == NULL || out_dir == NULL || out_dir_len == 0U) {
        return 0;
    }
    if (directory_exists(program_input)) {
        return snprintf(out_dir, out_dir_len, "%s", program_input) < (int)out_dir_len;
    }
    if (ends_with(program_input, "project.aiproj") ||
        ends_with(program_input, ".aos") ||
        ends_with(program_input, ".aibc1")) {
        return dirname_of(program_input, out_dir, out_dir_len);
    }
    return snprintf(out_dir, out_dir_len, ".") < (int)out_dir_len;
}

static char g_native_build_error[256];

static void set_native_build_error(const char* message)
{
    if (message == NULL) {
        message = "unknown";
    }
    (void)snprintf(g_native_build_error, sizeof(g_native_build_error), "%s", message);
}

static void set_native_build_errorf(const char* fmt, ...)
{
    va_list args;
    if (fmt == NULL) {
        set_native_build_error("unknown");
        return;
    }
    va_start(args, fmt);
    (void)vsnprintf(g_native_build_error, sizeof(g_native_build_error), fmt, args);
    va_end(args);
}

static const char* native_build_error(void)
{
    if (g_native_build_error[0] == '\0') {
        return "unknown";
    }
    return g_native_build_error;
}

static int build_input_to_aibc1(
    const char* program_input,
    const char* out_dir,
    char* out_app_path,
    size_t out_app_path_len,
    int use_cache)
{
    char resolved_program[PATH_MAX];
    char source_aos[PATH_MAX];
    char cache_root[PATH_MAX];
    char cache_key[40];
    char cache_key_dir[PATH_MAX];
    char cache_app_path[PATH_MAX];
    AivmProgram program;
    int explicit_aibc1_input;

    g_native_build_error[0] = '\0';

    if (program_input == NULL || out_dir == NULL || out_app_path == NULL || out_app_path_len == 0U) {
        set_native_build_error("invalid build arguments");
        return 0;
    }
    if (!ensure_directory(out_dir)) {
        set_native_build_error("failed to create output directory");
        return 0;
    }
    if (!join_path(out_dir, "app.aibc1", out_app_path, out_app_path_len)) {
        set_native_build_error("output path overflow");
        return 0;
    }

    explicit_aibc1_input = ends_with(program_input, ".aibc1");
    if (explicit_aibc1_input) {
        if (!resolve_input_to_aibc1(program_input, resolved_program, sizeof(resolved_program))) {
            set_native_build_error("could not resolve explicit .aibc1 input");
            return 0;
        }
        if (strcmp(resolved_program, out_app_path) == 0) {
            return 1;
        }
        if (!copy_file(resolved_program, out_app_path)) {
            set_native_build_error("failed to copy resolved app.aibc1");
            return 0;
        }
        return 1;
    }

    if (!resolve_input_to_aos(program_input, source_aos, sizeof(source_aos))) {
        set_native_build_error("could not resolve source .aos from input");
        return 0;
    }

    if (use_cache &&
        compute_source_graph_cache_key(source_aos, cache_key, sizeof(cache_key)) &&
        ensure_cache_root_for_source(source_aos, cache_root, sizeof(cache_root)) &&
        join_path(cache_root, cache_key, cache_key_dir, sizeof(cache_key_dir)) &&
        ensure_directory(cache_key_dir) &&
        join_path(cache_key_dir, "app.aibc1", cache_app_path, sizeof(cache_app_path)) &&
        file_exists(cache_app_path)) {
        if (strcmp(cache_app_path, out_app_path) == 0 || copy_file(cache_app_path, out_app_path)) {
            return 1;
        }
    }

    if (parse_bytecode_aos_to_program_file(source_aos, &program, 0) ||
        parse_simple_program_aos_to_program_file(source_aos, &program)) {
        if (!write_program_as_aibc1(&program, out_app_path)) {
            set_native_build_errorf(
                "failed writing app.aibc1 (inst=%llu const=%llu)",
                (unsigned long long)program.instruction_count,
                (unsigned long long)program.constant_count);
            return 0;
        }
        if (use_cache &&
            compute_source_graph_cache_key(source_aos, cache_key, sizeof(cache_key)) &&
            ensure_cache_root_for_source(source_aos, cache_root, sizeof(cache_root)) &&
            join_path(cache_root, cache_key, cache_key_dir, sizeof(cache_key_dir)) &&
            ensure_directory(cache_key_dir) &&
            join_path(cache_key_dir, "app.aibc1", cache_app_path, sizeof(cache_app_path))) {
            (void)copy_file(out_app_path, cache_app_path);
        }
        return 1;
    }

    {
        const char* detail = simple_last_error();
        if (detail == NULL || strcmp(detail, "unknown") == 0) {
            set_native_build_errorf("native simple compile failed for %s", source_aos);
        } else {
            set_native_build_error(detail);
        }
    }
    return 0;
}

static int delete_directory_recursive_portable(const char* path)
{
    if (path == NULL) {
        return 0;
    }
    if (!directory_exists(path)) {
        return 1;
    }
#ifdef _WIN32
    return native_delete_dir_recursive_windows(path);
#else
    return native_delete_dir_recursive_posix(path);
#endif
}

static int resolve_project_dir_for_cache(const char* input, char* out_project_dir, size_t out_project_dir_len)
{
    char source_aos[PATH_MAX];
    char manifest_path[PATH_MAX];
    char source_dir[PATH_MAX];
    char parent_dir[PATH_MAX];
    if (input == NULL || out_project_dir == NULL || out_project_dir_len == 0U) {
        return 0;
    }
    if (directory_exists(input)) {
        if (join_path(input, "project.aiproj", manifest_path, sizeof(manifest_path)) &&
            file_exists(manifest_path)) {
            return snprintf(out_project_dir, out_project_dir_len, "%s", input) < (int)out_project_dir_len;
        }
        if (dirname_of(input, parent_dir, sizeof(parent_dir)) &&
            join_path(parent_dir, "project.aiproj", manifest_path, sizeof(manifest_path)) &&
            file_exists(manifest_path)) {
            return snprintf(out_project_dir, out_project_dir_len, "%s", parent_dir) < (int)out_project_dir_len;
        }
        return snprintf(out_project_dir, out_project_dir_len, "%s", input) < (int)out_project_dir_len;
    }
    if (ends_with(input, "project.aiproj") ||
        ends_with(input, ".aos") ||
        ends_with(input, ".aibc1") ||
        ends_with(input, ".aibundle")) {
        return dirname_of(input, out_project_dir, out_project_dir_len);
    }
    if (resolve_input_to_aos(input, source_aos, sizeof(source_aos))) {
        if (!dirname_of(source_aos, source_dir, sizeof(source_dir))) {
            return 0;
        }
        if (join_path(source_dir, "project.aiproj", manifest_path, sizeof(manifest_path)) &&
            file_exists(manifest_path)) {
            return snprintf(out_project_dir, out_project_dir_len, "%s", source_dir) < (int)out_project_dir_len;
        }
        if (dirname_of(source_dir, parent_dir, sizeof(parent_dir)) &&
            join_path(parent_dir, "project.aiproj", manifest_path, sizeof(manifest_path)) &&
            file_exists(manifest_path)) {
            return snprintf(out_project_dir, out_project_dir_len, "%s", parent_dir) < (int)out_project_dir_len;
        }
        return snprintf(out_project_dir, out_project_dir_len, "%s", source_dir) < (int)out_project_dir_len;
    }
    return snprintf(out_project_dir, out_project_dir_len, ".") < (int)out_project_dir_len;
}

static AIRUN_MAYBE_UNUSED int handle_clean(int argc, char** argv)
{
    const char* target = ".";
    int target_set = 0;
    char project_dir[PATH_MAX];
    char toolchain_dir[PATH_MAX];
    char cache_dir[PATH_MAX];
    char airun_cache_dir[PATH_MAX];
    int i;

    for (i = 2; i < argc; i += 1) {
        if (argv[i][0] == '-') {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Unsupported clean argument.\" nodeId=argv)\n");
            return 2;
        }
        if (!target_set) {
            target = argv[i];
            target_set = 1;
            continue;
        }
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Unsupported clean argument.\" nodeId=argv)\n");
        return 2;
    }

    if (!resolve_project_dir_for_cache(target, project_dir, sizeof(project_dir)) ||
        !join_path(project_dir, ".toolchain", toolchain_dir, sizeof(toolchain_dir)) ||
        !join_path(toolchain_dir, "cache", cache_dir, sizeof(cache_dir)) ||
        !join_path(cache_dir, "airun", airun_cache_dir, sizeof(airun_cache_dir))) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to resolve cache path.\" nodeId=clean)\n");
        return 2;
    }

    if (!directory_exists(airun_cache_dir)) {
        printf("Ok#ok1(type=bool value=true)\n");
        return 0;
    }
    if (!delete_directory_recursive_portable(airun_cache_dir)) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to delete airun cache directory.\" nodeId=clean)\n");
        return 2;
    }
    printf("Ok#ok1(type=bool value=true)\n");
    return 0;
}

static AIRUN_MAYBE_UNUSED int handle_build(int argc, char** argv)
{
    const char* program_input = NULL;
    const char* out_dir = NULL;
    char default_out[PATH_MAX];
    char app_path[PATH_MAX];
    int use_cache = 1;
    int i;
    int build_rc;

    for (i = 2; i < argc; i += 1) {
        if (strcmp(argv[i], "--out") == 0) {
            if (i + 1 >= argc) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Missing --out value.\" nodeId=argv)\n");
                return 2;
            }
            out_dir = argv[++i];
            continue;
        }
        if (strcmp(argv[i], "--no-cache") == 0) {
            use_cache = 0;
            continue;
        }
        if (program_input == NULL && argv[i][0] != '-') {
            program_input = argv[i];
            continue;
        }
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Unsupported build argument.\" nodeId=argv)\n");
        return 2;
    }

    if (program_input == NULL) {
        program_input = ".";
    }
    if (out_dir == NULL) {
        if (!derive_build_out_dir(program_input, default_out, sizeof(default_out))) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Failed to derive build output directory.\" nodeId=outDir)\n");
            return 2;
        }
        out_dir = default_out;
    }

    build_rc = build_input_to_aibc1(program_input, out_dir, app_path, sizeof(app_path), use_cache);
    if (build_rc == 1) {
        printf("Ok#ok1(type=string value=\"%s\")\n", app_path);
        return 0;
    }
    fprintf(stderr,
        "Err#err1(code=RUN001 message=\"Build failed to emit app.aibc1 (%s).\" nodeId=build)\n",
        native_build_error());
    return 2;
}

static AIRUN_MAYBE_UNUSED int handle_publish(int argc, char** argv)
{
    const char* program_input = NULL;
    const char* target = NULL;
    const char* out_dir = "dist";
    const char* wasm_profile = "spa";
    const char* wasm_fullstack_host_target_arg = NULL;
    int wasm_profile_explicit = 0;
    char resolved_program[PATH_MAX];
    char source_aos[PATH_MAX];
    char artifact_dir[PATH_MAX];
    char runtime_bin[64];
    char runtime_web_bin[64];
    char runtime_web_wasm_bin[64];
    char publish_app_name[128];
    char publish_runtime_name[160];
    char runtime_src[PATH_MAX];
    char runtime_dst[PATH_MAX];
    char runtime_web_src[PATH_MAX];
    char runtime_web_wasm_src[PATH_MAX];
    char runtime_web_dst[PATH_MAX];
    char runtime_web_wasm_dst[PATH_MAX];
    char wasm_app_dst[PATH_MAX];
    char app_dst[PATH_MAX];
    char manifest_target[64];
    char manifest_wasm_fullstack_host_target[64];
    char wasm_fullstack_host_target[64];
    unsigned char* publish_app_bytes = NULL;
    size_t publish_app_size = 0U;
    AivmProgram publish_program;
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
            wasm_profile_explicit = 1;
            continue;
        }
        if (strcmp(argv[i], "--wasm-fullstack-host-target") == 0) {
            if (i + 1 >= argc) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Missing --wasm-fullstack-host-target value.\" nodeId=argv)\n");
                return 2;
            }
            wasm_fullstack_host_target_arg = argv[++i];
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
            if (parse_bytecode_aos_to_program_file(source_aos, &program, 1)) {
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
                } else {
                    if (source_file_looks_like_bytecode_aos(source_aos)) {
                        fprintf(stderr,
                            "Err#err1(code=DEV008 message=\"Native publish cannot encode this bytecode AOS shape yet. Build precompiled .aibc1.\" nodeId=program)\n");
                        return 2;
                    }
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

    wasm_profile = wasm_profile_normalize(wasm_profile);
    if (!wasm_profile_is_valid(wasm_profile)) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Unsupported wasm profile.\" nodeId=wasmProfile)\n");
        return 2;
    }
    if (wasm_profile_explicit && strcmp(target, "wasm32") != 0) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"--wasm-profile requires --target wasm32.\" nodeId=wasmProfile)\n");
        return 2;
    }
    if (wasm_fullstack_host_target_arg != NULL && strcmp(target, "wasm32") != 0) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"--wasm-fullstack-host-target requires --target wasm32.\" nodeId=wasmFullstackHostTarget)\n");
        return 2;
    }
    if (wasm_fullstack_host_target_arg != NULL && strcmp(wasm_profile, "fullstack") != 0) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"--wasm-fullstack-host-target requires --wasm-profile fullstack.\" nodeId=wasmFullstackHostTarget)\n");
        return 2;
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

    if (!join_path(out_dir, "app.aibc1", app_dst, sizeof(app_dst))) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"App destination path overflow.\" nodeId=publish)\n");
        return 2;
    }
    if (!read_binary_file(app_dst, &publish_app_bytes, &publish_app_size) ||
        aivm_program_load_aibc1(publish_app_bytes, publish_app_size, &publish_program).status != AIVM_PROGRAM_OK) {
        free(publish_app_bytes);
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to inspect published app bytecode.\" nodeId=publish)\n");
        return 2;
    }
    free(publish_app_bytes);
    publish_app_bytes = NULL;

    if (strcmp(target, "wasm32") == 0) {
        if (!join_path(artifact_dir, runtime_bin, runtime_src, sizeof(runtime_src))) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Runtime source path overflow.\" nodeId=publish)\n");
            return 2;
        }
        if (snprintf(runtime_web_bin, sizeof(runtime_web_bin), "aivm-runtime-wasm32-web.mjs") >= (int)sizeof(runtime_web_bin)) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Wasm runtime metadata overflow.\" nodeId=publish)\n");
            return 2;
        }
        if (snprintf(runtime_web_wasm_bin, sizeof(runtime_web_wasm_bin), "aivm-runtime-wasm32-web.wasm") >= (int)sizeof(runtime_web_wasm_bin)) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Wasm runtime web binary metadata overflow.\" nodeId=publish)\n");
            return 2;
        }
        if (!join_path(artifact_dir, runtime_web_bin, runtime_web_src, sizeof(runtime_web_src))) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Wasm web runtime source path overflow.\" nodeId=publish)\n");
            return 2;
        }
        if (!join_path(artifact_dir, runtime_web_wasm_bin, runtime_web_wasm_src, sizeof(runtime_web_wasm_src))) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Wasm web runtime binary source path overflow.\" nodeId=publish)\n");
            return 2;
        }
        if (snprintf(publish_runtime_name, sizeof(publish_runtime_name), "%s.wasm", publish_app_name) >= (int)sizeof(publish_runtime_name)) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Publish wasm runtime name overflow.\" nodeId=publish)\n");
            return 2;
        }
        if (!join_path(out_dir, publish_runtime_name, runtime_dst, sizeof(runtime_dst))) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Runtime destination path overflow.\" nodeId=publish)\n");
            return 2;
        }
        if (!copy_runtime_file(runtime_src, runtime_dst)) {
            fprintf(stderr,
                "Err#err1(code=RUN001 message=\"Failed to copy runtime for target RID. Build target runtime first.\" nodeId=publish)\n");
            return 2;
        }
        emit_wasm_profile_warnings(wasm_profile, &publish_program);

        if (strcmp(wasm_profile, "cli") == 0) {
            if (!emit_wasm_cli_launchers(out_dir, publish_runtime_name, publish_app_name)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Failed to emit wasm cli launchers.\" nodeId=publish)\n");
                return 2;
            }
        } else if (strcmp(wasm_profile, "spa") == 0) {
            if (!join_path(out_dir, runtime_web_bin, runtime_web_dst, sizeof(runtime_web_dst)) ||
                !join_path(out_dir, runtime_web_wasm_bin, runtime_web_wasm_dst, sizeof(runtime_web_wasm_dst)) ||
                !copy_runtime_file(runtime_web_src, runtime_web_dst) ||
                !copy_runtime_file(runtime_web_wasm_src, runtime_web_wasm_dst) ||
                !emit_wasm_spa_files(out_dir)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Failed to emit wasm web package files.\" nodeId=publish)\n");
                return 2;
            }
        } else if (strcmp(wasm_profile, "fullstack") == 0) {
            char www_dir[PATH_MAX];
            char fullstack_host_artifact_dir[PATH_MAX];
            char fullstack_host_runtime_bin[64];
            char fullstack_host_runtime_name[160];
            char fullstack_host_runtime_src[PATH_MAX];
            char fullstack_host_runtime_root_dst[PATH_MAX];
            char legacy_run_dst[PATH_MAX];
            char legacy_run_ps1_dst[PATH_MAX];
            char legacy_client_dir[PATH_MAX];
            char legacy_server_dir[PATH_MAX];
            wasm_fullstack_host_target[0] = '\0';
            if (wasm_fullstack_host_target_arg != NULL) {
                if (snprintf(wasm_fullstack_host_target, sizeof(wasm_fullstack_host_target), "%s", wasm_fullstack_host_target_arg) >= (int)sizeof(wasm_fullstack_host_target)) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"Wasm fullstack host target RID overflow.\" nodeId=wasmFullstackHostTarget)\n");
                    return 2;
                }
            } else if (resolve_publish_wasm_fullstack_host_target_from_manifest(program_input, manifest_wasm_fullstack_host_target, sizeof(manifest_wasm_fullstack_host_target))) {
                if (snprintf(wasm_fullstack_host_target, sizeof(wasm_fullstack_host_target), "%s", manifest_wasm_fullstack_host_target) >= (int)sizeof(wasm_fullstack_host_target)) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"Project wasm fullstack host target RID overflow.\" nodeId=project)\n");
                    return 2;
                }
            } else {
                if (snprintf(wasm_fullstack_host_target, sizeof(wasm_fullstack_host_target), "%s", host_rid()) >= (int)sizeof(wasm_fullstack_host_target)) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"Host RID overflow.\" nodeId=publish)\n");
                    return 2;
                }
            }
            if (strcmp(wasm_fullstack_host_target, "wasm32") == 0 ||
                !parse_target_to_artifact(
                    wasm_fullstack_host_target,
                    fullstack_host_artifact_dir,
                    sizeof(fullstack_host_artifact_dir),
                    fullstack_host_runtime_bin,
                    sizeof(fullstack_host_runtime_bin))) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Unsupported wasm fullstack host target RID.\" nodeId=wasmFullstackHostTarget)\n");
                return 2;
            }
            if (strstr(fullstack_host_runtime_bin, ".exe") != NULL) {
                if (snprintf(fullstack_host_runtime_name, sizeof(fullstack_host_runtime_name), "%s.exe", publish_app_name) >= (int)sizeof(fullstack_host_runtime_name)) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"Wasm fullstack runtime name overflow.\" nodeId=publish)\n");
                    return 2;
                }
            } else {
                if (snprintf(fullstack_host_runtime_name, sizeof(fullstack_host_runtime_name), "%s", publish_app_name) >= (int)sizeof(fullstack_host_runtime_name)) {
                    fprintf(stderr,
                        "Err#err1(code=RUN001 message=\"Wasm fullstack runtime name overflow.\" nodeId=publish)\n");
                    return 2;
                }
            }
            if (!join_path(out_dir, "www", www_dir, sizeof(www_dir)) ||
                !ensure_directory(www_dir)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Failed to create wasm fullstack www directory.\" nodeId=publish)\n");
                return 2;
            }
            if (!join_path(fullstack_host_artifact_dir, fullstack_host_runtime_bin, fullstack_host_runtime_src, sizeof(fullstack_host_runtime_src)) ||
                !join_path(out_dir, fullstack_host_runtime_name, fullstack_host_runtime_root_dst, sizeof(fullstack_host_runtime_root_dst)) ||
                !join_path(out_dir, "run", legacy_run_dst, sizeof(legacy_run_dst)) ||
                !join_path(out_dir, "run.ps1", legacy_run_ps1_dst, sizeof(legacy_run_ps1_dst)) ||
                !join_path(out_dir, "client", legacy_client_dir, sizeof(legacy_client_dir)) ||
                !join_path(out_dir, "server", legacy_server_dir, sizeof(legacy_server_dir)) ||
                !copy_runtime_file(fullstack_host_runtime_src, fullstack_host_runtime_root_dst) ||
                !remove_file_if_exists(legacy_run_dst) ||
                !remove_file_if_exists(legacy_run_ps1_dst)
#ifdef _WIN32
                || (directory_exists(legacy_client_dir) && !native_delete_dir_recursive_windows(legacy_client_dir))
                || (directory_exists(legacy_server_dir) && !native_delete_dir_recursive_windows(legacy_server_dir))
#else
                || (directory_exists(legacy_client_dir) && !native_delete_dir_recursive_posix(legacy_client_dir))
                || (directory_exists(legacy_server_dir) && !native_delete_dir_recursive_posix(legacy_server_dir))
#endif
                ) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Failed to prepare wasm fullstack package layout.\" nodeId=publish)\n");
                return 2;
            }
            if (!join_path(www_dir, "app.aibc1", wasm_app_dst, sizeof(wasm_app_dst)) ||
                !copy_file(app_dst, wasm_app_dst) ||
                !join_path(www_dir, publish_runtime_name, runtime_dst, sizeof(runtime_dst)) ||
                !copy_runtime_file(runtime_src, runtime_dst) ||
                !join_path(www_dir, runtime_web_bin, runtime_web_dst, sizeof(runtime_web_dst)) ||
                !join_path(www_dir, runtime_web_wasm_bin, runtime_web_wasm_dst, sizeof(runtime_web_wasm_dst)) ||
                !copy_runtime_file(runtime_web_src, runtime_web_dst) ||
                !copy_runtime_file(runtime_web_wasm_src, runtime_web_wasm_dst) ||
                !emit_wasm_fullstack_layout(out_dir)) {
                fprintf(stderr,
                    "Err#err1(code=RUN001 message=\"Failed to emit wasm fullstack package files.\" nodeId=publish)\n");
                return 2;
            }
        }

        printf("Ok#ok1(type=string value=\"publish-complete\")\n");
        return 0;
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

    if (!copy_runtime_file(runtime_src, runtime_dst)) {
        fprintf(stderr,
            "Err#err1(code=RUN001 message=\"Failed to copy runtime for target RID. Build target runtime first.\" nodeId=publish)\n");
        return 2;
    }
    emit_native_target_capability_warnings(target, &publish_program);

    printf("Ok#ok1(type=string value=\"publish-complete\")\n");
    return 0;
}

int main(int argc, char** argv)
{
    char exe_path[PATH_MAX];
    char exe_dir[PATH_MAX];
    char bundled_aibc1[PATH_MAX];
    char bundled_www_dir[PATH_MAX];
    char bundled_www_index[PATH_MAX];
    const char* exe_base;

    if (argv != NULL &&
        resolve_executable_path(argv[0], exe_path, sizeof(exe_path)) &&
        dirname_of(exe_path, exe_dir, sizeof(exe_dir)) &&
        join_path(exe_dir, "app.aibc1", bundled_aibc1, sizeof(bundled_aibc1)) &&
        file_exists(bundled_aibc1)) {
        exe_base = path_basename_ptr(exe_path);
        if (exe_base != NULL && strcmp(exe_base, "airun") != 0 && strcmp(exe_base, "airun.exe") != 0) {
            if (join_path(exe_dir, "www", bundled_www_dir, sizeof(bundled_www_dir)) &&
                join_path(bundled_www_dir, "index.html", bundled_www_index, sizeof(bundled_www_index)) &&
                file_exists(bundled_www_index)) {
                return run_native_fullstack_server(bundled_www_dir);
            }
            const char* const* app_argv = (argc > 1) ? (const char* const*)&argv[1] : NULL;
            size_t app_argc = (argc > 1) ? (size_t)(argc - 1) : 0U;
            return run_native_aibc1(bundled_aibc1, app_argv, app_argc, NULL);
        }
    }

    if (argc <= 1 || argv == NULL) {
        print_usage();
        return 2;
    }

    if (strcmp(argv[1], "version") == 0 || strcmp(argv[1], "--version") == 0) {
#ifdef AIRUN_MINIMAL_RUNTIME
        printf("aivm-runtime-native-c abi=%u\n", aivm_c_abi_version());
#else
        printf("airun-native-c abi=%u\n", aivm_c_abi_version());
#endif
        return 0;
    }
    if (strcmp(argv[1], "help") == 0 || strcmp(argv[1], "--help") == 0) {
        print_usage();
        return 0;
    }
    if (strcmp(argv[1], "run") == 0) {
        return handle_run(argc, argv);
    }
#ifdef AIRUN_MINIMAL_RUNTIME
    fprintf(stderr,
        "Err#err1(code=DEV008 message=\"Command is not part of minimal aivm-runtime surface.\" nodeId=command)\n");
    return 2;
#else
    if (strcmp(argv[1], "build") == 0) {
        return handle_build(argc, argv);
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
    if (strcmp(argv[1], "clean") == 0) {
        return handle_clean(argc, argv);
    }

    fprintf(stderr, "Unknown command: %s\n", argv[1]);
    print_usage();
    return 2;
#endif
}
