static void airun_log_capture_close(void)
{
    if (g_airun_log_file != NULL) {
        fclose(g_airun_log_file);
        g_airun_log_file = NULL;
    }
}

static void airun_log_capture_configure(int emit_bundle, const char* out_dir)
{
    char path[PATH_MAX];
    airun_log_capture_close();
    if (!emit_bundle || out_dir == NULL) {
        return;
    }
    if (!ensure_directory_recursive(out_dir)) {
        return;
    }
    if (!join_path(out_dir, "runtime_trace.log", path, sizeof(path))) {
        return;
    }
    g_airun_log_file = fopen(path, "wb");
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

static int write_native_debug_bundle(
    const NativeDebugOptions* options,
    const AivmProgram* program,
    const AivmVm* vm,
    int exit_code,
    int has_exit_code,
    const char* diagnostics_line)
{
    char path[PATH_MAX];
    char local0_preview[128];
    char local1_preview[128];
    char local2_preview[128];
    char stack0_preview[128];
    char stack1_preview[128];
    char stack2_preview[128];
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
    fprintf(f, "runtime_name = \"airun-native-c\"\n");
    fprintf(f, "runtime_abi = %u\n", aivm_c_abi_version());
    fprintf(f, "runtime_exe = \"%s\"\n", g_airun_runtime_exe_path[0] == '\0' ? "" : g_airun_runtime_exe_path);
    fprintf(f, "status = \"%s\"\n", (vm != NULL && vm->status == AIVM_VM_STATUS_ERROR) ? "error" : "ok");
    fprintf(f, "exit_code = %d\n", has_exit_code ? exit_code : 0);
    fprintf(f, "node_gc_interval_allocations = %d\n", AIVM_VM_NODE_GC_INTERVAL_ALLOCATIONS);
    fprintf(f, "node_gc_pressure_threshold_nodes = %d\n", AIVM_VM_NODE_GC_PRESSURE_THRESHOLD);
    fprintf(f, "node_gc_pressure_threshold_attrs = %d\n", AIVM_VM_NODE_ATTR_GC_PRESSURE_THRESHOLD);
    fprintf(f, "node_gc_pressure_threshold_children = %d\n", AIVM_VM_NODE_CHILD_GC_PRESSURE_THRESHOLD);
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
        size_t locals_base = 0U;
        size_t local0_index = 0U;
        size_t local1_index = 0U;
        size_t local2_index = 0U;
        local0_preview[0] = '\0';
        local1_preview[0] = '\0';
        local2_preview[0] = '\0';
        stack0_preview[0] = '\0';
        stack1_preview[0] = '\0';
        stack2_preview[0] = '\0';
        if (vm->call_frame_count > 0U) {
            locals_base = vm->call_frames[vm->call_frame_count - 1U].locals_base;
        }
        local0_index = locals_base;
        local1_index = locals_base + 1U;
        local2_index = locals_base + 2U;
        if (local0_index < vm->locals_count) {
            airun_format_value_preview(&vm->locals[local0_index], local0_preview, sizeof(local0_preview));
        }
        if (local1_index < vm->locals_count) {
            airun_format_value_preview(&vm->locals[local1_index], local1_preview, sizeof(local1_preview));
        }
        if (local2_index < vm->locals_count) {
            airun_format_value_preview(&vm->locals[local2_index], local2_preview, sizeof(local2_preview));
        }
        if (vm->stack_count > 0U) {
            airun_format_value_preview(&vm->stack[vm->stack_count - 1U], stack0_preview, sizeof(stack0_preview));
        }
        if (vm->stack_count > 1U) {
            airun_format_value_preview(&vm->stack[vm->stack_count - 2U], stack1_preview, sizeof(stack1_preview));
        }
        if (vm->stack_count > 2U) {
            airun_format_value_preview(&vm->stack[vm->stack_count - 3U], stack2_preview, sizeof(stack2_preview));
        }
        fprintf(f, "last = { function = \"main\", pc = %llu, op = \"%s\", node_id = \"unknown\" }\n",
            (unsigned long long)vm->instruction_pointer,
            aivm_opcode_name(inst->opcode));
        fprintf(f, "locals = { count = %llu, local0 = \"%s\", local1 = \"%s\", local2 = \"%s\" }\n",
            (unsigned long long)vm->locals_count,
            local0_preview,
            local1_preview,
            local2_preview);
        fprintf(f, "stack = { count = %llu, top0 = \"%s\", top1 = \"%s\", top2 = \"%s\" }\n",
            (unsigned long long)vm->stack_count,
            stack0_preview,
            stack1_preview,
            stack2_preview);
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
    fprintf(f, "node_gc_pressure_threshold_attrs = %d\n", AIVM_VM_NODE_ATTR_GC_PRESSURE_THRESHOLD);
    fprintf(f, "node_gc_pressure_threshold_children = %d\n", AIVM_VM_NODE_CHILD_GC_PRESSURE_THRESHOLD);
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
    fprintf(f, "events = [\n");
    for (size_t i = 0U; i < g_airun_captured_events.count; ++i) {
        const NativeHostUiEvent* event = &g_airun_captured_events.events[i];
        fprintf(
            f,
            "  { type = \"%s\", x = %d, y = %d, key = \"%s\", text = \"%s\", target = \"%s\" },\n",
            event->type,
            event->x,
            event->y,
            event->key,
            event->text,
            event->target_id);
    }
    fprintf(f, "]\n");
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
    fprintf(f, "runtime = { name = \"airun-native-c\", abi = %u, exe = \"%s\" }\n",
        aivm_c_abi_version(),
        g_airun_runtime_exe_path[0] == '\0' ? "" : g_airun_runtime_exe_path);
    fprintf(f, "memory = { string_arena_used = %llu, string_arena_high_water = %llu, bytes_arena_used = %llu, bytes_arena_high_water = %llu, node_count = %llu, node_high_water = %llu, node_attr_count = %llu, node_attr_high_water = %llu, node_child_count = %llu, node_child_high_water = %llu, node_gc_compactions = %llu, node_gc_attempts = %llu, node_gc_reclaimed_nodes = %llu, node_gc_allocations_since_gc = %llu, node_gc_interval_allocations = %d, node_gc_pressure_threshold_nodes = %d, node_gc_pressure_threshold_attrs = %d, node_gc_pressure_threshold_children = %d, string_arena_pressure_count = %llu, bytes_arena_pressure_count = %llu, node_arena_pressure_count = %llu }\n",
        (unsigned long long)((vm == NULL) ? 0U : vm->string_arena_used),
        (unsigned long long)((vm == NULL) ? 0U : vm->string_arena_high_water),
        (unsigned long long)((vm == NULL) ? 0U : vm->bytes_arena_used),
        (unsigned long long)((vm == NULL) ? 0U : vm->bytes_arena_high_water),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_high_water),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_attr_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_attr_high_water),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_child_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_child_high_water),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_compaction_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_attempt_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_gc_reclaimed_nodes),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_allocations_since_gc),
        AIVM_VM_NODE_GC_INTERVAL_ALLOCATIONS,
        AIVM_VM_NODE_GC_PRESSURE_THRESHOLD,
        AIVM_VM_NODE_ATTR_GC_PRESSURE_THRESHOLD,
        AIVM_VM_NODE_CHILD_GC_PRESSURE_THRESHOLD,
        (unsigned long long)((vm == NULL) ? 0U : vm->string_arena_pressure_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->bytes_arena_pressure_count),
        (unsigned long long)((vm == NULL) ? 0U : vm->node_arena_pressure_count));
    if (g_native_net_last_failure.valid != 0) {
        fprintf(
            f,
            "network = { kind = %d, tls = %s, stage = \"%s\", classification = \"%s\", resolution = \"%s\", host = \"%s\", resolved_ip = \"%s\", port = %d, error = \"%s\" }\n",
            g_native_net_last_failure.kind,
            g_native_net_last_failure.use_tls ? "true" : "false",
            native_net_error_stage(g_native_net_last_failure.error),
            native_net_error_classification(g_native_net_last_failure.error),
            native_net_resolution_state(g_native_net_last_failure.resolved_ip, g_native_net_last_failure.error),
            g_native_net_last_failure.host,
            g_native_net_last_failure.resolved_ip,
            g_native_net_last_failure.port,
            g_native_net_last_failure.error);
    }
    fclose(f);
    return 1;
}
