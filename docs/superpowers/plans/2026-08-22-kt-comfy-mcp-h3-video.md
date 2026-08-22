# KT Comfy MCP H3 Video Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Start the existing KT MiniMax-H3 ComfyUI, control it through Comfy Org's official stdio MCP server, and retrieve one verified video.

**Architecture:** Run ComfyUI and the first-party `comfy-mcp` server inside the existing KT container. A small Python MCP client communicates with the server over stdio, invokes discovery, validation, execution, wait, and output-fetch tools, and keeps all traffic on `127.0.0.1:8000`.

**Tech Stack:** Python 3.12, ComfyUI, MiniMax-H3, `comfy-mcp`, `comfy-cli>=1.14.0`, Python MCP SDK, ffprobe

---

### Task 1: Reconnect and verify the target

**Files:**
- Inspect: `/home/work/media-lab-data/deploy/backendai/minimax-h3-start.sh`
- Inspect: `/home/work/media-lab-data/minimax-h3/workflows/*.json`

- [ ] **Step 1: Reconnect to `codex-val-media-image-20260822t021112z`**

Open its AI Nexus console and confirm the shell prompt contains the exact session name.

- [ ] **Step 2: Verify the target and port**

```bash
test -x /home/work/media-lab-data/deploy/backendai/minimax-h3-start.sh
test -d /home/work/media-lab-data/minimax-h3/source/ComfyUI
nvidia-smi --query-gpu=name,memory.total,memory.free --format=csv,noheader
curl -fsS --max-time 2 http://127.0.0.1:8000/system_stats || true
```

Expected: launcher and checkout exist; GPU is H200; port 8000 is either unavailable or already returns ComfyUI JSON.

### Task 2: Install the isolated official MCP environment

**Files:**
- Create: `/home/work/media-lab-data/minimax-h3/env/comfy-mcp/`

- [ ] **Step 1: Create the environment**

```bash
python -m venv /home/work/media-lab-data/minimax-h3/env/comfy-mcp
```

- [ ] **Step 2: Install the first-party packages**

```bash
/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/pip install "comfy-cli>=1.14.0" comfy-mcp
```

Expected: exit code 0.

- [ ] **Step 3: Verify versions**

```bash
/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy --version
/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy-mcp --version
```

Expected: comfy-cli is at least 1.14.0 and `comfy-mcp` reports a version.

### Task 3: Start and verify H3 ComfyUI

**Files:**
- Create: `/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/comfyui.log`
- Create: `/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/comfyui.pid`

- [ ] **Step 1: Start the existing launcher**

```bash
mkdir -p /home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822
nohup /home/work/media-lab-data/deploy/backendai/minimax-h3-start.sh \
  > /home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/comfyui.log 2>&1 &
echo $! > /home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/comfyui.pid
```

- [ ] **Step 2: Wait for readiness**

```bash
for i in $(seq 1 60); do
  curl -fsS http://127.0.0.1:8000/system_stats >/tmp/h3-system-stats.json && break
  sleep 2
done
python -m json.tool /tmp/h3-system-stats.json >/dev/null
```

Expected: valid JSON before the 120-second deadline.

- [ ] **Step 3: Confirm H3 nodes are exposed**

```bash
curl -fsS http://127.0.0.1:8000/object_info >/tmp/h3-object-info.json
python -c "import json; d=json.load(open('/tmp/h3-object-info.json')); print(len(d)); print([k for k in d if 'MiniMax' in k or 'H3' in k][:30])"
```

Expected: non-zero node count and at least one MiniMax/H3-related class, or existing workflow node classes verified by exact name.

### Task 4: Create and smoke-test the MCP client

**Files:**
- Create: `/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/run_mcp_h3.py`
- Create: `/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/mcp-tools.json`

- [ ] **Step 1: Write the stdio MCP client**

Write this exact file:

```python
import argparse
import asyncio
import json
import os
import re
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT = Path("/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822")
MCP_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy-mcp"
COMFY_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy"


def dump(value):
    if hasattr(value, "model_dump"):
        return value.model_dump(mode="json")
    return value


def prompt_id_from(result):
    raw = json.dumps(dump(result), ensure_ascii=False)
    match = re.search(r'"prompt_id"\s*:\s*"([^"]+)"', raw)
    if not match:
        raise RuntimeError(f"prompt_id missing: {raw}")
    return match.group(1)


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--probe", action="store_true")
    parser.add_argument("--workflow")
    parser.add_argument("--out-dir")
    args = parser.parse_args()

    env = os.environ.copy()
    env["COMFY_BIN"] = COMFY_BIN
    env["COMFY_LOCAL_URL"] = "http://127.0.0.1:8000"
    params = StdioServerParameters(command=MCP_BIN, args=[], env=env)

    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            listed = await session.list_tools()
            names = [tool.name for tool in listed.tools]
            ROOT.mkdir(parents=True, exist_ok=True)
            (ROOT / "mcp-tools.json").write_text(
                json.dumps(dump(listed), indent=2, ensure_ascii=False), encoding="utf-8"
            )
            print("tools", names)
            if "server_info" not in names:
                raise RuntimeError("server_info tool missing")
            info = await session.call_tool("server_info", {})
            print("server_info", json.dumps(dump(info), ensure_ascii=False))
            if args.probe:
                return

            if not args.workflow or not args.out_dir:
                raise RuntimeError("--workflow and --out-dir are required")
            if "validate_workflow" not in names or "run_workflow" not in names:
                raise RuntimeError("workflow tools missing")

            validated = await session.call_tool(
                "validate_workflow", {"workflow_path": args.workflow}
            )
            print("validate", json.dumps(dump(validated), ensure_ascii=False))
            submitted = await session.call_tool(
                "run_workflow", {"workflow_path": args.workflow, "wait": False}
            )
            prompt_id = prompt_id_from(submitted)
            (ROOT / "job.json").write_text(
                json.dumps({"prompt_id": prompt_id}, indent=2), encoding="utf-8"
            )
            print("prompt_id", prompt_id)

            if "wait_for_job" in names:
                done = await session.call_tool(
                    "wait_for_job", {"prompt_id": prompt_id, "timeout_seconds": 600.0}
                )
            elif "job" in names:
                done = await session.call_tool(
                    "job",
                    {"action": "wait", "prompt_id": prompt_id, "timeout_seconds": 600.0},
                )
            else:
                raise RuntimeError("job wait tool missing")
            print("job", json.dumps(dump(done), ensure_ascii=False))

            if "fetch_outputs" not in names:
                raise RuntimeError("fetch_outputs tool missing")
            fetched = await session.call_tool(
                "fetch_outputs", {"prompt_id": prompt_id, "out_dir": args.out_dir}
            )
            print("outputs", json.dumps(dump(fetched), ensure_ascii=False))


if __name__ == "__main__":
    asyncio.run(main())
```

- [ ] **Step 2: Run the handshake**

```bash
/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/python \
  /home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/run_mcp_h3.py --probe
```

Expected: MCP initialize succeeds; `server_info` returns the ComfyUI workspace and the tool list includes `run_workflow`, `wait_for_job` or `job`, and `fetch_outputs`.

### Task 5: Select, validate, and execute the smallest existing H3 workflow

**Files:**
- Read: `/home/work/media-lab-data/minimax-h3/workflows/*.json`
- Create: `/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/selected-workflow.json`
- Create: `/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/job.json`

- [ ] **Step 1: Select the smallest API workflow**

Run this exact selector. It retains only API-format graphs whose every `class_type` exists in the live server and copies the candidate with the fewest nodes.

```bash
/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/python - <<'PY'
import json
from pathlib import Path

src = Path('/home/work/media-lab-data/minimax-h3/workflows')
dst = Path('/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/selected-workflow.json')
object_info = json.load(open('/tmp/h3-object-info.json', encoding='utf-8'))
candidates = []
missing_by_file = {}
for path in src.glob('*.json'):
    try:
        graph = json.load(open(path, encoding='utf-8'))
    except Exception:
        continue
    if not isinstance(graph, dict) or not graph:
        continue
    nodes = list(graph.values())
    if not all(isinstance(node, dict) and 'class_type' in node for node in nodes):
        continue
    missing = sorted({node['class_type'] for node in nodes if node['class_type'] not in object_info})
    if missing:
        missing_by_file[path.name] = missing
        continue
    candidates.append((len(nodes), path, graph))
if not candidates:
    raise SystemExit('no runnable API workflow: ' + json.dumps(missing_by_file, ensure_ascii=False))
count, path, graph = min(candidates, key=lambda item: (item[0], item[1].name))
dst.write_text(json.dumps(graph, indent=2, ensure_ascii=False), encoding='utf-8')
print('selected', path, 'nodes', count, 'output', dst)
PY
```

- [ ] **Step 2: Validate through MCP**

Call `validate_workflow` with the exact selected path.

Expected: valid result with no missing models or nodes.

- [ ] **Step 3: Submit asynchronously through MCP**

Call `run_workflow` with `wait=false`, save the returned `prompt_id` in `job.json`, and do not submit a second job.

- [ ] **Step 4: Wait through MCP**

Call `wait_for_job` when present; otherwise call `job` with action `wait` in bounded intervals until terminal status.

Expected: completed status without execution errors.

### Task 6: Fetch and verify the generated video

**Files:**
- Create: `/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/output/`
- Create: `/home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/media-info.json`

- [ ] **Step 1: Fetch through MCP**

Call `fetch_outputs(prompt_id, out_dir)` with the exact prompt ID and output directory.

- [ ] **Step 2: Locate the video**

```bash
find /home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/output \
  -type f \( -iname '*.mp4' -o -iname '*.webm' -o -iname '*.mov' \) -size +0c
```

Expected: exactly one or more non-empty video files.

- [ ] **Step 3: Verify playback metadata**

```bash
ffprobe -v error -show_entries format=duration:stream=codec_name,width,height,r_frame_rate \
  -of json "$(find /home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/output -type f -iname '*.mp4' | head -1)" \
  > /home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/media-info.json
python -m json.tool /home/work/media-lab-data/minimax-h3/runs/mcp-validation-20260822/media-info.json
```

Expected: positive duration, video codec, and non-zero dimensions.

### Task 7: Handoff

- [ ] **Step 1: Report the MCP evidence**

Report the MCP tool names used, `prompt_id`, terminal job status, and verified media metadata.

- [ ] **Step 2: Provide the output**

Copy or download the verified video to an accessible local path and render it in the Codex response.
