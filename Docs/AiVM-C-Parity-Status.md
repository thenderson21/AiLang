# AiLang Zero-C# DoD Dashboard

Generated: 2026-03-02 18:01:58 UTC

Overall status: **FAIL**

## Gates

| Gate | Status | Details |
|---|---|---|
| Behavioral parity | PASS | 66/66 (100.00%) with mode=execute |
| Zero-C# | FAIL | tracked_csharp=91, dotnet_refs_in_ci_scripts=15 |
| Test coverage | PASS | test-aivm-c=pass, test.sh=pass, determinism=pass |
| Benchmark | PASS | bench_run=pass, baseline=present, threshold=within-threshold, regressions=0, missing=0, max_pct=5 |
| Samples completion | PASS | complete=4/4 (manifest=Docs/Sample-Completion-Manifest.md) |
| Memory/GC | PASS | rc_test=yes, cycle_test=yes, leak_script=yes, profile_script=yes |

## Behavioral Sub-Gates

| Entrypoint | Status | Details |
|---|---|---|
| run source | PASS | backed by canonical golden corpus parity |
| embedded bytecode | PASS | vm=c run Bytecode source executes through bridge |
| embedded bundle | PASS | vm=c run .aibundle executes through bridge |
| serve | PASS | vm=c serve deterministically reports DEV008 (not linked) |

## Behavioral Cases

| Result | Case | Canonical Exit | C VM Exit |
|---|---|---:|---:|
| PASS | check_duplicate_ids | 2 | 2 |
| PASS | check_if_shape | 2 | 2 |
| PASS | check_missing_name | 2 | 2 |
| PASS | fmt_basic | 0 | 0 |
| PASS | http_health_route_refactor | 0 | 0 |
| PASS | http_parse_basic | 3 | 3 |
| PASS | http_parse_full | 3 | 3 |
| PASS | http_route_basic | 3 | 3 |
| PASS | json_basic | 3 | 3 |
| PASS | json_key_ordering | 3 | 3 |
| PASS | json_order_keys | 3 | 3 |
| PASS | lifecycle_app_basic | 0 | 0 |
| PASS | lifecycle_app_exit_code | 9 | 9 |
| PASS | lifecycle_app_no_init_update | 0 | 0 |
| PASS | lifecycle_command_emit_stdout | 0 | 0 |
| PASS | lifecycle_command_exit_after_print | 7 | 7 |
| PASS | lifecycle_command_print | 0 | 0 |
| PASS | lifecycle_event_message_basic | 0 | 0 |
| PASS | lifecycle_event_source_start | 0 | 0 |
| PASS | lifecycle_loop_structure | 0 | 0 |
| PASS | manifest_absolute_path | 2 | 2 |
| PASS | manifest_include_absolute_path | 2 | 2 |
| PASS | manifest_include_missing_version | 2 | 2 |
| PASS | manifest_include_valid | 3 | 3 |
| PASS | manifest_missing_field | 2 | 2 |
| PASS | manifest_valid | 3 | 3 |
| PASS | new_cli_success | 0 | 0 |
| PASS | new_directory_exists | 0 | 0 |
| PASS | new_gui_success | 0 | 0 |
| PASS | new_http_success | 0 | 0 |
| PASS | new_lib_success | 0 | 0 |
| PASS | new_missing_name | 0 | 0 |
| PASS | new_success | 0 | 0 |
| PASS | new_unknown_template | 0 | 0 |
| PASS | publish_binary_runs | 0 | 0 |
| PASS | publish_bundle_cycle_error | 0 | 0 |
| PASS | publish_bundle_single_file | 0 | 0 |
| PASS | publish_bundle_with_import | 0 | 0 |
| PASS | publish_include_missing_library | 0 | 0 |
| PASS | publish_include_success | 0 | 0 |
| PASS | publish_include_version_mismatch | 0 | 0 |
| PASS | publish_missing_dir | 0 | 0 |
| PASS | publish_missing_manifest | 0 | 0 |
| PASS | publish_overwrite_bundle | 0 | 0 |
| PASS | publish_writes_bundle | 0 | 0 |
| PASS | run_import_cycle | 2 | 2 |
| PASS | run_import_missing | 3 | 3 |
| PASS | run_import_simple | 2 | 2 |
| PASS | run_nontrivial | 0 | 0 |
| PASS | run_var | 0 | 0 |
| PASS | sample_cli_fetch | 3 | 3 |
| PASS | sample_weather_api | 3 | 3 |
| PASS | sample_weather_site | 3 | 3 |
| PASS | stdlib_io | 2 | 2 |
| PASS | stdlib_math | 2 | 2 |
| PASS | stdlib_str | 2 | 2 |
| PASS | trace_basic | 3 | 3 |
| PASS | trace_with_args | 0 | 0 |
| PASS | vm_default_is_canonical | 3 | 3 |
| PASS | vm_echo | 3 | 3 |
| PASS | vm_health_handler | 3 | 3 |
| PASS | vm_hello | 3 | 3 |
| PASS | vm_import_support | 3 | 3 |
| PASS | vm_map_field | 3 | 3 |
| PASS | vm_node_shapes | 3 | 3 |
| PASS | vm_unsupported_construct | 3 | 3 |
