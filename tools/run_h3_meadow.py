"""Generate three real MiniMax H3 I2V shots through the official Comfy MCP.

This deliberately uses H3's first-frame input. It does not feed a still-image
pan/zoom movie into R2V or previous_tail, because that suppresses articulated
motion and produces the fake-looking result this project previously had.
"""

from __future__ import annotations

import argparse
import asyncio
import copy
import hashlib
import json
import os
import re
import shutil
import urllib.request
from pathlib import Path
from typing import Any

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


DEFAULT_ROOT = Path("/home/work/media-lab-data/minimax-h3/runs/meadow-h3-i2v")
MCP_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy-mcp"
COMFY_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy"
COMFY_URL = "http://127.0.0.1:8189"
TEMPLATE_URL = (
    "https://raw.githubusercontent.com/Comfy-Org/workflow_templates/"
    "main/templates/video_minimax_h3_i2v.json"
)
TEMPLATE_SHA256 = "313b029321a8be303e827dad471bff3022ca564c8bf8c6198a3e70b65c599671"
# KT's validator checks every combo widget even when turbo_mode=False. This
# installed LoRA only satisfies the inactive branch's combo; the base H3 path
# remains selected and the LoRA is never sampled.
KT_INACTIVE_LORA = "lightx2v_I2V_14B_480p_cfg_step_distill_rank64_bf16.safetensors"
ASSET_BASE_URL = (
    "https://raw.githubusercontent.com/Hariuncle/nuboard_game/"
    "main/game/assets/images"
)

SHOTS = (
    {
        "slug": "01_bombardment",
        "image": "meadow-h3-01.png",
        "seed": 2026082211,
        "prompt": (
            "One continuous 5-second premium 3D fantasy game cinematic. Start exactly from "
            "<Picture 1>. Pomora, the fluffy white cat queen, sprints two strong steps toward "
            "camera, plants one paw, and turns toward the blast. The airborne Acorn Bomber "
            "spins forward and completes his throwing follow-through while the visible acorn "
            "arcs across frame. The explosion expands with dirt, sparks and flower petals; "
            "fur, grass, ears and water react physically. Low camera tracks sideways with real "
            "foreground-background parallax and one short impact jolt. Preserve every face, "
            "costume, body proportion and meadow prop. No cut, no static pose, no Ken Burns "
            "pan, no morphing, no extra limbs, no text or watermark. Audio: quick pawsteps, "
            "acorn whistle, soft magical blast, petals and heroic orchestral pulse."
        ),
    },
    {
        "slug": "02_rally",
        "image": "meadow-h3-02.png",
        "seed": 2026082212,
        "prompt": (
            "One continuous 5-second premium 3D fantasy game cinematic. Start exactly from "
            "<Picture 1>. Pomora makes one clear forward command with her raised paw, shifts "
            "her weight and looks toward the enemy. Thorn Knight braces behind the rose shield "
            "as one glowing acorn impact lands and pushes the shield backward. Berry Archer "
            "draws the flower bow a little farther and releases one arrow past camera; her arms, "
            "bowstring and cape complete the motion. Petals and fur react to the arrow wake. "
            "Camera performs a controlled half-orbit around the trio with strong 3D parallax. "
            "Preserve exact identities, outfits and weapons. No cut, frozen portrait, simple "
            "zoom, face morph, extra limbs, text or watermark. Audio: shield impact, bowstring "
            "snap, arrow whoosh, rustling petals and rising heroic music."
        ),
    },
    {
        "slug": "03_first_person",
        "image": "meadow-h3-03.png",
        "seed": 2026082213,
        "prompt": (
            "One continuous 5-second first-person fantasy action cinematic. Start exactly from "
            "<Picture 1>. The airborne Acorn Bomber rotates through his existing leap while the "
            "spinning acorn travels rapidly toward the viewer. Berry Archer releases the drawn "
            "flower arrow. The first-person blossom blaster smoothly tracks the acorn through "
            "the physical ring sight, fires one golden-pink cleansing pulse, recoils once, then "
            "settles. The pulse hits the acorn and bursts it into glowing petals and a circular "
            "hit shockwave, ending on a stable centered gameplay aim. Preserve the exact weapon, "
            "hands, characters and environment. Genuine articulated motion and depth, no cut, "
            "static zoom, warped weapon, extra fingers, text or watermark. Audio: bow snap, acorn "
            "whistle, blaster pulse, magical petal burst and short victory sting."
        ),
    },
)


def download(url: str, destination: Path, expected_sha256: str | None = None) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(url, timeout=60) as response:
        data = response.read()
    if expected_sha256:
        actual = hashlib.sha256(data).hexdigest()
        if actual != expected_sha256:
            raise RuntimeError(f"SHA-256 mismatch for {url}: {actual}")
    destination.write_bytes(data)


def text_content(result: Any) -> str:
    return "\n".join(
        block.text for block in getattr(result, "content", ()) if hasattr(block, "text")
    )


def payload(result: Any) -> dict[str, Any]:
    is_error = getattr(result, "is_error", None)
    if is_error is None:
        is_error = getattr(result, "isError", False)
    if is_error:
        raise RuntimeError(f"Comfy MCP tool failed: {text_content(result)}")
    structured = getattr(result, "structuredContent", None)
    if isinstance(structured, dict):
        return structured
    structured = getattr(result, "structured_content", None)
    if isinstance(structured, dict):
        return structured
    raw = text_content(result).strip()
    try:
        parsed = json.loads(raw)
        return parsed if isinstance(parsed, dict) else {"value": parsed}
    except json.JSONDecodeError:
        match = re.search(r"\{.*\}", raw, re.DOTALL)
        if match:
            parsed = json.loads(match.group(0))
            if isinstance(parsed, dict):
                return parsed
    return {"text": raw}


def find_value(value: Any, key: str) -> Any:
    if isinstance(value, dict):
        if key in value:
            return value[key]
        for child in value.values():
            found = find_value(child, key)
            if found is not None:
                return found
    elif isinstance(value, list):
        for child in value:
            found = find_value(child, key)
            if found is not None:
                return found
    return None


def node(graph: dict[str, Any], node_id: int) -> dict[str, Any]:
    return next(item for item in graph["nodes"] if item["id"] == node_id)


def set_widgets(item: dict[str, Any], values: list[Any], named: dict[str, Any]) -> None:
    item["widgets_values"] = values
    item["widgets_values_named"] = named


def workflow_for(template: dict[str, Any], shot: dict[str, Any]) -> dict[str, Any]:
    graph = copy.deepcopy(template)

    for subgraph in graph.get("definitions", {}).get("subgraphs", []):
        for inner in subgraph.get("nodes", []):
            inner_id = inner.get("id")
            inner_type = inner.get("type")
            # Comfy MCP currently expands the subgraph from these inner widget
            # defaults. Updating only the outer group widgets leaves the
            # official template's vaporwave COMFYUI demo prompt active.
            if inner_id == 104 and inner_type == "MiniMaxH3ImageToVideo":
                inner["widgets_values"] = [shot["prompt"], 1344, 768, 124]
                inner["widgets_values_named"] = {
                    "prompt": shot["prompt"],
                    "width": 1344,
                    "height": 768,
                    "length": 124,
                }
            elif inner_id == 111 and inner_type == "PrimitiveFloat":
                inner["widgets_values"] = [5]
                inner["widgets_values_named"] = {"value": 5}
            elif inner_id == 15 and inner_type == "RandomNoise":
                inner["widgets_values"] = [shot["seed"], "fixed"]
                inner["widgets_values_named"] = {
                    "noise_seed": shot["seed"],
                    "control_after_generate": "fixed",
                }
            elif inner_id == 121 and inner_type == "LoraLoaderModelOnly":
                inner["widgets_values"] = [KT_INACTIVE_LORA, 1]
                inner["widgets_values_named"] = {
                    "lora_name": KT_INACTIVE_LORA,
                    "strength_model": 1,
                }

    load = node(graph, 114)
    set_widgets(load, [shot["image"], "image"], {"image": shot["image"], "upload": "image"})

    resolution = node(graph, 115)
    set_widgets(
        resolution,
        ["16:9 (Widescreen)", 0.98, 32],
        {"aspect_ratio": "16:9 (Widescreen)", "megapixels": 0.98, "multiple": 32},
    )

    h3 = node(graph, 105)
    values = list(h3["widgets_values"])
    values[0:5] = [shot["prompt"], 1344, 768, 5, shot["seed"]]
    values[10] = KT_INACTIVE_LORA
    h3["widgets_values"] = values
    h3["widgets_values_named"].update(
        prompt=shot["prompt"],
        width=1344,
        height=768,
        value_1=5,
        noise_seed=shot["seed"],
        lora_name=KT_INACTIVE_LORA,
    )

    save = node(graph, 92)
    prefix = f"video/meadow_h3/{shot['slug']}"
    set_widgets(
        save,
        [prefix, "auto", "auto"],
        {"filename_prefix": prefix, "format": "auto", "codec": "auto"},
    )
    return graph


def terminal_status(data: dict[str, Any]) -> str:
    status = find_value(data, "status")
    if isinstance(status, dict):
        status = status.get("status_str") or status.get("status")
    return str(status or "").lower()


async def wait_for_job(session: ClientSession, prompt_id: str) -> dict[str, Any]:
    # MCP wait calls are intentionally bounded; repeat without substring guessing.
    for _ in range(120):
        result = await session.call_tool(
            "job", {"action": "wait", "prompt_id": prompt_id, "timeout_seconds": 25}
        )
        data = payload(result)
        status = terminal_status(data)
        if status in {"completed", "success", "succeeded"}:
            return data
        if status in {"failed", "error", "cancelled", "canceled"}:
            raise RuntimeError(f"H3 job {prompt_id} ended as {status}: {data}")
        print("WAIT", prompt_id, status or "running", flush=True)
    raise TimeoutError(f"H3 job timed out: {prompt_id}")


async def run(root: Path) -> None:
    root.mkdir(parents=True, exist_ok=True)
    template_path = root / "video_minimax_h3_i2v.json"
    bundled_template = Path(__file__).resolve().parent / "templates" / "video_minimax_h3_i2v.json"
    if bundled_template.exists():
        data = bundled_template.read_bytes()
        actual = hashlib.sha256(data).hexdigest()
        if actual != TEMPLATE_SHA256:
            raise RuntimeError(f"Bundled template SHA-256 mismatch: {actual}")
        shutil.copyfile(bundled_template, template_path)
    else:
        download(TEMPLATE_URL, template_path, TEMPLATE_SHA256)
    template = json.loads(template_path.read_text(encoding="utf-8"))

    for shot in SHOTS:
        image_path = root / shot["image"]
        if not image_path.exists():
            download(f"{ASSET_BASE_URL}/{shot['image']}", image_path)

    workflow_dir = root / "workflows"
    workflow_dir.mkdir(exist_ok=True)
    workflow_paths: list[Path] = []
    for shot in SHOTS:
        path = workflow_dir / f"{shot['slug']}.json"
        path.write_text(
            json.dumps(workflow_for(template, shot), ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        workflow_paths.append(path)

    env = os.environ.copy()
    env.update(COMFY_BIN=COMFY_BIN, COMFY_LOCAL_URL=COMFY_URL)
    env.pop("COMFYUI_URL", None)
    params = StdioServerParameters(command=MCP_BIN, args=[], env=env)

    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tool_names = {tool.name for tool in (await session.list_tools()).tools}
            required = {
                "server_info", "upload_file", "validate_workflow", "run_workflow",
                "job", "fetch_outputs",
            }
            if missing := required - tool_names:
                raise RuntimeError(f"Missing official Comfy MCP tools: {sorted(missing)}")

            info = payload(await session.call_tool("server_info", {}))
            print("SERVER_INFO", json.dumps(info, ensure_ascii=False)[:1000], flush=True)
            resolved_url = (
                find_value(info, "url")
                or find_value(info, "base_url")
                or find_value(info, "server_url")
            )
            if isinstance(resolved_url, str) and "127.0.0.1:8189" not in resolved_url:
                raise RuntimeError(f"Comfy MCP resolved the wrong server: {resolved_url}")
            running = find_value(info, "running")
            if running is False:
                raise RuntimeError(f"ComfyUI is not running: {info}")

            upload = payload(
                await session.call_tool(
                    "upload_file",
                    {"paths": [str(root / shot["image"]) for shot in SHOTS], "overwrite": True},
                )
            )
            print("UPLOADED", json.dumps(upload, ensure_ascii=False), flush=True)

            fetched_dir = root / "fetched"
            fetched_dir.mkdir(exist_ok=True)
            jobs: dict[str, str] = {}

            # The three workflows share one H3 GPU, so submit them serially to avoid VRAM thrash.
            for shot, path in zip(SHOTS, workflow_paths):
                validation = payload(
                    await session.call_tool("validate_workflow", {"workflow_path": str(path)})
                )
                if find_value(validation, "valid") is not True:
                    raise RuntimeError(f"Workflow validation failed for {path}: {validation}")
                print("VALID", shot["slug"], flush=True)

                submitted = payload(
                    await session.call_tool(
                        "run_workflow", {"workflow_path": str(path), "wait": False}
                    )
                )
                prompt_id = find_value(submitted, "prompt_id")
                if not isinstance(prompt_id, str) or not prompt_id:
                    raise RuntimeError(f"prompt_id missing: {submitted}")
                jobs[shot["slug"]] = prompt_id
                (root / "jobs.json").write_text(json.dumps(jobs, indent=2), encoding="utf-8")
                print("QUEUED", shot["slug"], prompt_id, flush=True)

                await wait_for_job(session, prompt_id)
                before_media = {
                    path: (path.stat().st_mtime_ns, path.stat().st_size)
                    for suffix in ("*.mp4", "*.webm", "*.mov", "*.mkv")
                    for path in fetched_dir.rglob(suffix)
                }
                fetched = payload(
                    await session.call_tool(
                        "fetch_outputs", {"prompt_id": prompt_id, "out_dir": str(fetched_dir)}
                    )
                )
                print("FETCHED", shot["slug"], json.dumps(fetched, ensure_ascii=False), flush=True)
                after_media = [
                    path
                    for suffix in ("*.mp4", "*.webm", "*.mov", "*.mkv")
                    for path in fetched_dir.rglob(suffix)
                    if path.stat().st_size > 0
                    and before_media.get(path) != (path.stat().st_mtime_ns, path.stat().st_size)
                ]
                if not after_media:
                    raise RuntimeError(
                        f"fetch_outputs returned without a new playable video: {fetched}"
                    )
                print("VERIFIED_MEDIA", shot["slug"], *(str(path) for path in after_media), flush=True)

    print("H3_MEADOW_I2V_ALL_DONE", fetched_dir, flush=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    args = parser.parse_args()
    asyncio.run(run(args.root))


if __name__ == "__main__":
    main()
