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
    if (argv == NULL || out_argv == NULL || out_argc == NULL || had_vm_c == NULL) {
        return 0;
    }
    next = (char**)calloc((size_t)argc + 1U, sizeof(char*));
    if (next == NULL) {
        return 0;
    }
    *had_vm_c = 0;
    w = 0;
    for (i = 0; i < argc; i++) {
        if (strcmp(argv[i], "--vm=c") == 0) {
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
    char vm_c_script[PATH_MAX];
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
    if (snprintf(vm_c_script, sizeof(vm_c_script), "%s/scripts/airun-vm-c.sh", repo_root) >= (int)sizeof(vm_c_script)) {
        fprintf(stderr, "airun native wrapper: vm-c script path overflow\n");
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
        exec_argv = (char**)calloc((size_t)filtered_argc + 1U, sizeof(char*));
        if (exec_argv == NULL) {
            free(filtered_argv);
            fprintf(stderr, "airun native wrapper: allocation failure\n");
            return 2;
        }
        exec_argv[0] = vm_c_script;
        for (i = 1; i < filtered_argc; i++) {
            exec_argv[i] = filtered_argv[i];
        }
        exec_argv[filtered_argc] = NULL;
        execv(vm_c_script, exec_argv);
        fprintf(stderr, "airun native wrapper: failed to exec vm-c runner (%s): %s\n", vm_c_script, strerror(errno));
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
