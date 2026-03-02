#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

static int is_executable(const char* path)
{
    struct stat st;
    if (path == NULL) {
        return 0;
    }
    if (stat(path, &st) != 0) {
        return 0;
    }
    if (!S_ISREG(st.st_mode)) {
        return 0;
    }
    return access(path, X_OK) == 0 ? 1 : 0;
}

static int is_regular_file(const char* path)
{
    struct stat st;
    if (path == NULL) {
        return 0;
    }
    if (stat(path, &st) != 0) {
        return 0;
    }
    return S_ISREG(st.st_mode) ? 1 : 0;
}

static int read_file_line(const char* path, char* out, size_t out_len)
{
    FILE* f;
    size_t n;
    if (path == NULL || out == NULL || out_len == 0) {
        return 0;
    }
    f = fopen(path, "rb");
    if (f == NULL) {
        return 0;
    }
    if (fgets(out, (int)out_len, f) == NULL) {
        fclose(f);
        return 0;
    }
    fclose(f);
    n = strlen(out);
    while (n > 0 && (out[n - 1] == '\n' || out[n - 1] == '\r')) {
        out[--n] = '\0';
    }
    return n > 0 ? 1 : 0;
}

static int write_file_line(const char* path, const char* value)
{
    FILE* f;
    if (path == NULL || value == NULL) {
        return 0;
    }
    f = fopen(path, "wb");
    if (f == NULL) {
        return 0;
    }
    if (fprintf(f, "%s\n", value) < 0) {
        fclose(f);
        return 0;
    }
    fclose(f);
    return 1;
}

static int ensure_bridge_library(const char* repo_root, char* out_path, size_t out_len)
{
    char cache_path[PATH_MAX];
    char build_script[PATH_MAX];
    char cmd[PATH_MAX + 8];
    char line[PATH_MAX];
    char last_line[PATH_MAX];
    FILE* p;
    int rc;

    if (repo_root == NULL || out_path == NULL || out_len == 0) {
        return 0;
    }

    if (snprintf(cache_path, sizeof(cache_path), "%s/.tmp/aivm-c-bridge-lib.path", repo_root) >= (int)sizeof(cache_path)) {
        return 0;
    }
    if (read_file_line(cache_path, line, sizeof(line)) && is_regular_file(line)) {
        if (snprintf(out_path, out_len, "%s", line) >= (int)out_len) {
            return 0;
        }
        return 1;
    }

    if (snprintf(build_script, sizeof(build_script), "%s/scripts/build-aivm-c-shared.sh", repo_root) >= (int)sizeof(build_script)) {
        return 0;
    }
    if (!is_executable(build_script)) {
        return 0;
    }
    if (snprintf(cmd, sizeof(cmd), "\"%s\"", build_script) >= (int)sizeof(cmd)) {
        return 0;
    }

    p = popen(cmd, "r");
    if (p == NULL) {
        return 0;
    }
    last_line[0] = '\0';
    while (fgets(line, (int)sizeof(line), p) != NULL) {
        size_t n = strlen(line);
        while (n > 0 && (line[n - 1] == '\n' || line[n - 1] == '\r')) {
            line[--n] = '\0';
        }
        if (n > 0) {
            snprintf(last_line, sizeof(last_line), "%s", line);
        }
    }
    rc = pclose(p);
    if (rc != 0 || last_line[0] == '\0' || !is_regular_file(last_line)) {
        return 0;
    }

    if (snprintf(out_path, out_len, "%s", last_line) >= (int)out_len) {
        return 0;
    }
    write_file_line(cache_path, out_path);
    return 1;
}

static int path_dirname(const char* path, char* out, size_t out_len)
{
    const char* slash;
    size_t n;
    if (path == NULL || out == NULL || out_len == 0) {
        return 0;
    }
    slash = strrchr(path, '/');
    if (slash == NULL) {
        return 0;
    }
    n = (size_t)(slash - path);
    if (n == 0 || n + 1 > out_len) {
        return 0;
    }
    memcpy(out, path, n);
    out[n] = '\0';
    return 1;
}

static int strip_vm_c(char** argv, int argc, char*** out_argv, int* out_argc, int* had_vm_c)
{
    char** next;
    int i;
    int w;
    int passthrough;
    if (argv == NULL || out_argv == NULL || out_argc == NULL || had_vm_c == NULL) {
        return 0;
    }
    next = (char**)calloc((size_t)argc + 1U, sizeof(char*));
    if (next == NULL) {
        return 0;
    }
    *had_vm_c = 0;
    w = 0;
    passthrough = 0;
    for (i = 0; i < argc; i++) {
        if (!passthrough && strcmp(argv[i], "--") == 0) {
            passthrough = 1;
            next[w++] = argv[i];
            continue;
        }
        if (!passthrough && strcmp(argv[i], "--vm=c") == 0) {
            *had_vm_c = 1;
            continue;
        }
        next[w++] = argv[i];
    }
    next[w] = NULL;
    *out_argv = next;
    *out_argc = w;
    return 1;
}

int main(int argc, char** argv)
{
    char exe_real[PATH_MAX];
    char tools_dir[PATH_MAX];
    char repo_root[PATH_MAX];
    char backend_path[PATH_MAX];
    char bridge_lib_path[PATH_MAX];
    char** filtered_argv;
    int filtered_argc;
    int had_vm_c;
    int wants_vm_c_run;
    int use_filtered_backend_args;
    int trace_enabled;
    int i;

    trace_enabled = getenv("AICLI_NATIVE_TRACE") != NULL ? 1 : 0;

    if (argc <= 0 || argv == NULL) {
        fprintf(stderr, "airun native wrapper: invalid argv\n");
        return 2;
    }

    if (realpath(argv[0], exe_real) == NULL) {
        fprintf(stderr, "airun native wrapper: failed to resolve executable path: %s\n", strerror(errno));
        return 2;
    }

    if (!path_dirname(exe_real, tools_dir, sizeof(tools_dir))) {
        fprintf(stderr, "airun native wrapper: failed to resolve tools directory\n");
        return 2;
    }

    if (!path_dirname(tools_dir, repo_root, sizeof(repo_root))) {
        fprintf(stderr, "airun native wrapper: failed to resolve repository root\n");
        return 2;
    }

    if (snprintf(backend_path, sizeof(backend_path), "%s/tools/airun-host", repo_root) >= (int)sizeof(backend_path)) {
        fprintf(stderr, "airun native wrapper: backend path overflow\n");
        return 2;
    }

    if (!is_executable(backend_path)) {
        fprintf(stderr, "airun native wrapper: missing backend binary at %s\n", backend_path);
        return 2;
    }

    if (!strip_vm_c(argv, argc, &filtered_argv, &filtered_argc, &had_vm_c)) {
        fprintf(stderr, "airun native wrapper: failed to process arguments\n");
        return 2;
    }
    if (trace_enabled) {
        fprintf(stderr, "[airun-native] had_vm_c=%d argc=%d filtered_argc=%d cmd=%s\n",
            had_vm_c,
            argc,
            filtered_argc,
            filtered_argc >= 2 ? filtered_argv[1] : "(none)");
    }

    wants_vm_c_run = 0;
    use_filtered_backend_args = 0;
    if (had_vm_c && filtered_argc >= 2 && strcmp(filtered_argv[1], "run") == 0) {
        wants_vm_c_run = 1;
    } else if (had_vm_c) {
        use_filtered_backend_args = 1;
    }

    if (wants_vm_c_run) {
        char** exec_argv;
        if (!ensure_bridge_library(repo_root, bridge_lib_path, sizeof(bridge_lib_path))) {
            fprintf(stderr, "airun native wrapper: failed to resolve C bridge library\n");
            free(filtered_argv);
            return 2;
        }
        if (setenv("AIVM_C_BRIDGE_EXECUTE", "1", 1) != 0 || setenv("AIVM_C_BRIDGE_LIB", bridge_lib_path, 1) != 0) {
            fprintf(stderr, "airun native wrapper: failed to set bridge environment\n");
            free(filtered_argv);
            return 2;
        }

        exec_argv = (char**)calloc((size_t)filtered_argc + 2U, sizeof(char*));
        if (exec_argv == NULL) {
            free(filtered_argv);
            fprintf(stderr, "airun native wrapper: allocation failure\n");
            return 2;
        }
        exec_argv[0] = backend_path;
        for (i = 1; i < filtered_argc; i++) {
            exec_argv[i] = filtered_argv[i];
        }
        exec_argv[filtered_argc] = "--vm=c";
        exec_argv[filtered_argc + 1] = NULL;
        execv(backend_path, exec_argv);
        fprintf(stderr, "airun native wrapper: failed to exec vm-c backend (%s): %s\n", backend_path, strerror(errno));
        free(exec_argv);
        free(filtered_argv);
        return 2;
    }

    if (use_filtered_backend_args) {
        if (trace_enabled) {
            fprintf(stderr, "[airun-native] exec backend filtered path for vm=c non-run\n");
        }
        filtered_argv[0] = backend_path;
        execv(backend_path, filtered_argv);
    } else {
        if (trace_enabled) {
            fprintf(stderr, "[airun-native] exec backend raw argv path\n");
        }
        argv[0] = backend_path;
        execv(backend_path, argv);
    }
    fprintf(stderr, "airun native wrapper: failed to exec backend (%s): %s\n", backend_path, strerror(errno));
    free(filtered_argv);
    return 2;
}
