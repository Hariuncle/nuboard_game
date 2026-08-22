# KT AI Nexus MiniMax-H3 Video via Official Comfy MCP

Date: 2026-08-22

## Goal

Use Comfy Org's first-party `comfy-mcp` server inside the existing KT AI Nexus session `codex-val-media-image-20260822t021112z` to run a small MiniMax-H3 validation workflow and retrieve a playable video. The generation must execute on the session's allocated H200 GPU and use its existing H3 models and workflows.

## Current Environment

- KT session status: `RUNNING`
- Runtime: NGC PyTorch 25.11, Python 3.12, PyTorch 2.10, CUDA 13.0
- GPU: H200 0.5 fGPU with approximately 70.2 GiB visible VRAM
- Existing ComfyUI checkout: `/home/work/media-lab-data/minimax-h3/source/ComfyUI`
- Existing H3 models: all five assets listed by `minimax-h3-assets.json` are present with matching file sizes
- Existing launcher: `/home/work/media-lab-data/deploy/backendai/minimax-h3-start.sh`
- Existing API workflows: `/home/work/media-lab-data/minimax-h3/workflows/`
- ComfyUI is not currently running on port 8000

## Architecture

Install the official `comfy-mcp` and `comfy-cli>=1.14.0` packages into an isolated virtual environment inside the KT container. The MCP server and a lightweight MCP client run in the same container as ComfyUI, avoiding AI Nexus's authenticated HTTPS proxy for internal control.

```text
Codex browser terminal
        |
        v
MCP client process --stdio--> official comfy-mcp
                                |
                                v
                      comfy-cli --where local
                                |
                                v
                    ComfyUI http://127.0.0.1:8000
                                |
                                v
                  MiniMax-H3 models on H200 GPU
```

This is genuine MCP communication over stdio, but it does not dynamically register the server as a native tool in the already-running Codex task. Native Codex registration would require a new trusted transport, such as SSH or a private-network tunnel, which is outside this validation.

## Installation and Isolation

- Create an isolated environment under `/home/work/media-lab-data/minimax-h3/env/comfy-mcp`.
- Install only first-party packages needed for the bridge: `comfy-mcp` and `comfy-cli>=1.14.0`.
- Do not modify the existing H3 runtime virtual environment.
- Do not download or replace model weights.
- Point comfy-cli at the existing ComfyUI checkout with `comfy set-default` if required.
- Configure the target as local port 8000. Because the MCP server runs in the same container, no external app exposure, API key, cookie forwarding, or public port is needed.

## Execution Flow

1. Reconnect to the target KT session and confirm it is still `RUNNING`.
2. Install the isolated MCP environment after action-time confirmation.
3. Start ComfyUI with the existing H3 launcher and wait for a successful `/system_stats` response on port 8000.
4. Start `comfy-mcp` over stdio from the isolated environment.
5. Call MCP tools in this order:
   - `server_info` to confirm the target configuration.
   - `search_models` and `list_nodes` or equivalent discovery calls to confirm H3 assets and node classes.
   - `validate_workflow` on the selected existing API workflow.
   - `run_workflow` asynchronously.
   - `wait_for_job` or `job_status` until completion.
   - `fetch_outputs` into a dedicated validation-results directory.
6. Confirm the output is a non-empty playable video and report its duration, dimensions, codec, and path.

## Validation Workflow

Use the smallest existing H3 API/canary workflow that already matches the installed custom nodes and model filenames. Prefer bundled sample inputs over inventing a new graph. Keep its model-required dimensions and frame count unless the workflow explicitly exposes safe lower-cost parameters.

The test should produce one short video. It should avoid batch generation, model downloads, partner APIs, external credits, and public sharing.

## Output Location

Store fetched validation outputs under:

`/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/`

The original ComfyUI output remains in its normal output directory. No existing result is overwritten.

## Error Handling

- If package installation fails, preserve the existing environment and report the exact dependency conflict.
- If port 8000 is occupied by an unrelated process, inspect it and stop rather than terminating an unowned process.
- If the launcher fails, inspect its log and do not modify model files automatically.
- If MCP discovery describes the wrong workspace, correct the comfy-cli default path before submitting a job.
- If workflow validation reports missing nodes or models, stop before generation and report the missing names.
- If the job runs out of VRAM, do not retry with arbitrary settings; identify the workflow's supported lower-memory configuration first.
- If the job exceeds a bounded wait, retain the prompt ID and continue through status calls rather than submitting a duplicate.

## Security Boundaries

- Keep ComfyUI bound to the session and do not enable AI Nexus's external-public-app option.
- Do not expose AI Nexus cookies, passwords, tokens, or API keys to the MCP process.
- Do not use Comfy Cloud or partner-model calls that incur credits.
- Do not alter or terminate other running KT sessions.

## Success Criteria

- Official `comfy-mcp` completes an MCP handshake inside the KT container.
- MCP discovery confirms the intended H3 ComfyUI workspace.
- The chosen workflow passes preflight validation.
- One H3 job completes without an execution error.
- `fetch_outputs` retrieves a non-empty video into the dedicated validation directory.
- The result is playable and its basic media metadata is verified.

## Non-Goals

- Registering KT ComfyUI as a persistent native MCP server for future Codex tasks.
- Publishing the ComfyUI or MCP endpoint to the internet.
- Building a new H3 workflow from scratch.
- Producing a polished production video or a batch of variants.
- Changing model weights, custom nodes, or existing workflows.

