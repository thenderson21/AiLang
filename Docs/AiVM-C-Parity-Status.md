# AiLang Zero-C# DoD Dashboard

Generated: 2026-03-02 16:45:15 UTC

Overall status: **FAIL**

## Gates

| Gate | Status | Details |
|---|---|---|
| Behavioral parity | FAIL | 18/66 (27.27%) with mode=execute |
| Zero-C# | FAIL | tracked_csharp=91, dotnet_refs_in_ci_scripts=13 |
| Test coverage | PASS | test-aivm-c=pass, test.sh=pass, determinism=pass |
| Benchmark | FAIL | bench_run=pass, baseline=present, threshold=baseline-not-calibrated |
| Samples completion | FAIL | complete=0/4 (manifest=Docs/Sample-Completion-Manifest.md) |
| Memory/GC | FAIL | rc_test=no, cycle_test=no, leak_script=yes, profile_script=yes |

## Behavioral Sub-Gates

| Entrypoint | Status | Details |
|---|---|---|
| run source | FAIL | backed by canonical golden corpus parity |
| embedded bytecode | PENDING | dedicated end-to-end entrypoint parity harness not finalized |
| embedded bundle | PENDING | dedicated end-to-end entrypoint parity harness not finalized |
| serve | PENDING | dedicated deterministic parity harness not finalized |

## Behavioral Cases

| Result | Case | Canonical Exit | C VM Exit |
|---|---|---:|---:|
| PASS | check_duplicate_ids | 2 | 2 |
| PASS | check_if_shape | 2 | 2 |
| PASS | check_missing_name | 2 | 2 |
| FAIL | fmt_basic | 0 | 1 |
| FAIL | http_health_route_refactor | 0 | 1 |
| FAIL | http_parse_basic | 3 | 1 |
| FAIL | http_parse_full | 3 | 1 |
| FAIL | http_route_basic | 3 | 1 |
| FAIL | json_basic | 3 | 1 |
| FAIL | json_key_ordering | 3 | 1 |
| FAIL | json_order_keys | 3 | 1 |
| FAIL | lifecycle_app_basic | 0 | 1 |
| FAIL | lifecycle_app_exit_code | 9 | 1 |
| FAIL | lifecycle_app_no_init_update | 0 | 1 |
| FAIL | lifecycle_command_emit_stdout | 0 | 1 |
| FAIL | lifecycle_command_exit_after_print | 7 | 1 |
| FAIL | lifecycle_command_print | 0 | 1 |
| FAIL | lifecycle_event_message_basic | 0 | 1 |
| FAIL | lifecycle_event_source_start | 0 | 1 |
| FAIL | lifecycle_loop_structure | 0 | 1 |
| PASS | manifest_absolute_path | 2 | 2 |
| PASS | manifest_include_absolute_path | 2 | 2 |
| PASS | manifest_include_missing_version | 2 | 2 |
| PASS | manifest_include_valid | 3 | 3 |
| PASS | manifest_missing_field | 2 | 2 |
| PASS | manifest_valid | 3 | 3 |
| FAIL | new_cli_success | 0 | 1 |
| FAIL | new_directory_exists | 0 | 1 |
| FAIL | new_gui_success | 0 | 1 |
| FAIL | new_http_success | 0 | 1 |
| FAIL | new_lib_success | 0 | 1 |
| FAIL | new_missing_name | 0 | 1 |
| FAIL | new_success | 0 | 1 |
| FAIL | new_unknown_template | 0 | 1 |
| FAIL | publish_binary_runs | 0 | 1 |
| FAIL | publish_bundle_cycle_error | 0 | 1 |
| FAIL | publish_bundle_single_file | 0 | 1 |
| FAIL | publish_bundle_with_import | 0 | 1 |
| FAIL | publish_include_missing_library | 0 | 1 |
| FAIL | publish_include_success | 0 | 1 |
| FAIL | publish_include_version_mismatch | 0 | 1 |
| FAIL | publish_missing_dir | 0 | 1 |
| FAIL | publish_missing_manifest | 0 | 1 |
| FAIL | publish_overwrite_bundle | 0 | 1 |
| FAIL | publish_writes_bundle | 0 | 1 |
| PASS | run_import_cycle | 2 | 2 |
| PASS | run_import_missing | 3 | 3 |
| PASS | run_import_simple | 2 | 2 |
| FAIL | run_nontrivial | 0 | 1 |
| FAIL | run_var | 0 | 1 |
| FAIL | sample_cli_fetch | 3 | 1 |
| FAIL | sample_weather_api | 3 | 1 |
| FAIL | sample_weather_site | 3 | 1 |
| PASS | stdlib_io | 2 | 2 |
| PASS | stdlib_math | 2 | 2 |
| PASS | stdlib_str | 2 | 2 |
| FAIL | trace_basic | 3 | 1 |
| FAIL | trace_with_args | 0 | 1 |
| PASS | vm_default_is_canonical | 3 | 3 |
| FAIL | vm_echo | 3 | 1 |
| FAIL | vm_health_handler | 3 | 1 |
| FAIL | vm_hello | 3 | 1 |
| PASS | vm_import_support | 3 | 3 |
| FAIL | vm_map_field | 3 | 1 |
| FAIL | vm_node_shapes | 3 | 1 |
| PASS | vm_unsupported_construct | 3 | 3 |
