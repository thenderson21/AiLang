# AiLang Zero-C# DoD Dashboard

Generated: 2026-03-03 21:40:27 UTC

Overall status: **PASS**

## Gates

| Gate | Status | Details |
|---|---|---|
| Behavioral parity | PASS | 66/66 (100.00%) with mode=native |
| Zero-C# | PASS | tracked_csharp=0, dotnet_refs_in_ci_scripts=0 |
| Test coverage | PASS | test-aivm-c=pass, test.sh=pass, determinism=pass |
| Benchmark | PASS | bench_run=pass, baseline=present, threshold=within-threshold, regressions=0, missing=0, max_pct=5 |
| Samples completion | PASS | complete=4/4 (manifest=Docs/Sample-Completion-Manifest.md) |
| Memory/GC | PASS | rc_test=yes, cycle_test=yes, leak_script=yes, profile_script=yes |

## Behavioral Sub-Gates

| Entrypoint | Status | Details |
|---|---|---|
| run source | PASS | backed by canonical golden corpus parity |
| embedded bytecode | PASS | vm=c run bytecode-oriented source completed (exit=0) |
| embedded bundle | PASS | vm=c run .aibundle succeeded |
| serve | PASS | serve is not part of the current native runtime command surface |

## Behavioral Cases

| Result | Case | Canonical Exit | C VM Exit |
|---|---|---:|---:|
| PASS | check_duplicate_ids | 2 | 2 |
| PASS | check_if_shape | 2 | 2 |
| PASS | check_missing_name | 2 | 2 |
| PASS | fmt_basic | 2 | 2 |
| PASS | http_health_route_refactor | 2 | 2 |
| PASS | http_parse_basic | 3 | 3 |
| PASS | http_parse_full | 3 | 3 |
| PASS | http_route_basic | 2 | 2 |
| PASS | json_basic | 2 | 2 |
| PASS | json_key_ordering | 2 | 2 |
| PASS | json_order_keys | 2 | 2 |
| PASS | lifecycle_app_basic | 2 | 2 |
| PASS | lifecycle_app_exit_code | 2 | 2 |
| PASS | lifecycle_app_no_init_update | 2 | 2 |
| PASS | lifecycle_command_emit_stdout | 2 | 2 |
| PASS | lifecycle_command_exit_after_print | 2 | 2 |
| PASS | lifecycle_command_print | 2 | 2 |
| PASS | lifecycle_event_message_basic | 2 | 2 |
| PASS | lifecycle_event_source_start | 2 | 2 |
| PASS | lifecycle_loop_structure | 2 | 2 |
| PASS | manifest_absolute_path | 2 | 2 |
| PASS | manifest_include_absolute_path | 2 | 2 |
| PASS | manifest_include_missing_version | 2 | 2 |
| PASS | manifest_include_valid | 2 | 2 |
| PASS | manifest_missing_field | 2 | 2 |
| PASS | manifest_valid | 2 | 2 |
| PASS | new_cli_success | 2 | 2 |
| PASS | new_directory_exists | 2 | 2 |
| PASS | new_gui_success | 2 | 2 |
| PASS | new_http_success | 2 | 2 |
| PASS | new_lib_success | 2 | 2 |
| PASS | new_missing_name | 2 | 2 |
| PASS | new_success | 2 | 2 |
| PASS | new_unknown_template | 2 | 2 |
| PASS | publish_binary_runs | 2 | 2 |
| PASS | publish_bundle_cycle_error | 2 | 2 |
| PASS | publish_bundle_single_file | 2 | 2 |
| PASS | publish_bundle_with_import | 2 | 2 |
| PASS | publish_include_missing_library | 2 | 2 |
| PASS | publish_include_success | 2 | 2 |
| PASS | publish_include_version_mismatch | 2 | 2 |
| PASS | publish_missing_dir | 2 | 2 |
| PASS | publish_missing_manifest | 2 | 2 |
| PASS | publish_overwrite_bundle | 2 | 2 |
| PASS | publish_writes_bundle | 2 | 2 |
| PASS | run_import_cycle | 2 | 2 |
| PASS | run_import_missing | 2 | 2 |
| PASS | run_import_simple | 2 | 2 |
| PASS | run_nontrivial | 2 | 2 |
| PASS | run_var | 2 | 2 |
| PASS | sample_cli_fetch | 2 | 2 |
| PASS | sample_weather_api | 2 | 2 |
| PASS | sample_weather_site | 2 | 2 |
| PASS | stdlib_io | 2 | 2 |
| PASS | stdlib_math | 2 | 2 |
| PASS | stdlib_str | 2 | 2 |
| PASS | trace_basic | 3 | 3 |
| PASS | trace_with_args | 2 | 2 |
| PASS | vm_default_is_canonical | 2 | 2 |
| PASS | vm_echo | 0 | 0 |
| PASS | vm_health_handler | 2 | 2 |
| PASS | vm_hello | 0 | 0 |
| PASS | vm_import_support | 2 | 2 |
| PASS | vm_map_field | 2 | 2 |
| PASS | vm_node_shapes | 2 | 2 |
| PASS | vm_unsupported_construct | 2 | 2 |
