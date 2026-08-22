"""Deprecated R2V experiment.

Do not use for the meadow intro: its still-derived guide videos suppress real
character motion. Use run_h3_meadow.py (official H3 first-frame I2V) instead.
"""

import asyncio
import copy
import json
import os
import re
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


ROOT = Path("/home/work/media-lab-data/minimax-h3/runs/meadow-story")
TEMPLATE = Path(
    "/home/work/media-lab-data/minimax-h3/workflows/"
    "minimax-h3-r2v-long-segment-guided-api.json"
)
MCP_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy-mcp"
COMFY_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy"

SHOTS = (
    {
        "slug": "01_bombardment",
        "image": "meadow-intro-01.png",
        "guide": "meadow-intro-01-guide.mp4",
        "seed": 2026082211,
        "prompt": (
            "Premium animated 3D fantasy game cinematic, use the reference as the exact first frame. "
            "Pomora, the exact fluffy white cat queen in pink petal mantle and heart crown, continues "
            "running toward camera with real weight while turning toward the blast. The explosion already "
            "visible in the first frame expands into dirt, sparks, petals and a pressure wave while the "
            "airborne acorn continues its fast spinning arc and the Bomber completes his follow-through. "
            "Grass, fur, petals and water react physically. Fast low camera dolly with parallax and an impact "
            "jolt, not a static pan or Ken Burns zoom. Preserve exact faces, outfits, meadow buildings "
            "and props. No text, logo, watermark, morphing, extra limbs, or cuts."
        ),
    },
    {
        "slug": "02_rally",
        "image": "meadow-intro-02.png",
        "guide": "meadow-intro-02-guide.mp4",
        "seed": 2026082212,
        "prompt": (
            "Premium animated 3D fantasy game trailer, use the reference as the exact first frame. "
            "Pomora completes one decisive forward command with her raised paw and a brave expression. "
            "Thorn Knight holds the rose shield against one glowing impact while Berry Archer releases "
            "the already-drawn flower arrow past camera. Fur, capes, ears and petals react to the shot. "
            "Camera arcs quickly around "
            "the trio with strong depth and parallax, no frozen portrait or simple zoom. Preserve exact "
            "character identity, costumes, weapons and Blossom Meadow. No text, logo, watermark, face "
            "morph, extra limbs, or cuts."
        ),
    },
    {
        "slug": "03_first_person",
        "image": "meadow-intro-03.png",
        "guide": "meadow-intro-03-guide.mp4",
        "seed": 2026082213,
        "prompt": (
            "Premium animated 3D fantasy first-person action cinematic, use the reference as the exact "
            "first frame. The airborne Acorn Bomber continues his visible forward arc with clear body "
            "rotation while the existing spinning acorn travels toward camera. Berry Archer releases her "
            "already-drawn flower arrow at it. The first-person blossom blaster tracks the acorn through "
            "the physical ring sight and fires one bright golden-pink cleansing pulse with controlled "
            "recoil. The acorn bursts into harmless glowing petals; the flash expands into a circular "
            "reticle and ends on a stable centered first-person gameplay view. Strong depth, character "
            "motion and camera follow-through, no static zoom. Preserve exact characters, weapon and "
            "meadow. No text, logo, watermark, deformed paws, morphing weapon, or cuts."
        ),
    },
)


def dump(value):
    if hasattr(value, "model_dump"):
        return value.model_dump(mode="json")
    return value


def content_text(result):
    return "\n".join(
        block.text for block in getattr(result, "content", ()) if hasattr(block, "text")
    )


def prompt_id_from(result):
    raw = content_text(result) or json.dumps(dump(result), ensure_ascii=False)
    match = re.search(r'"prompt_id"\s*:\s*"([^"]+)"', raw)
    if not match:
        raise RuntimeError(f"prompt_id missing: {raw}")
    return match.group(1)


def workflow_for(template, shot):
    graph = copy.deepcopy(template)
    graph["ref_image"]["inputs"]["image"] = shot["image"]
    graph["ref_video"]["inputs"]["file"] = shot["guide"]
    graph["previous_tail"]["inputs"]["file"] = shot["guide"]
    graph["h3"]["inputs"].update(
        prompt=shot["prompt"], width=1344, height=768, length=48
    )
    graph["noise"]["inputs"]["noise_seed"] = shot["seed"]
    graph["schedule"]["inputs"]["steps"] = 8
    graph["video"]["inputs"]["fps"] = 24
    graph["save"]["inputs"]["filename_prefix"] = f"meadow_story/{shot['slug']}"
    return graph


async def main():
    template = json.loads(TEMPLATE.read_text(encoding="utf-8"))
    workflow_dir = ROOT / "workflows"
    workflow_dir.mkdir(parents=True, exist_ok=True)
    workflow_paths = []
    for shot in SHOTS:
        path = workflow_dir / f"{shot['slug']}.json"
        path.write_text(
            json.dumps(workflow_for(template, shot), ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        workflow_paths.append(path)

    env = os.environ.copy()
    env["COMFY_BIN"] = COMFY_BIN
    env["COMFY_LOCAL_URL"] = "http://127.0.0.1:8189"
    params = StdioServerParameters(command=MCP_BIN, args=[], env=env)

    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            names = {tool.name for tool in (await session.list_tools()).tools}
            required = {"validate_workflow", "run_workflow", "job", "fetch_outputs"}
            missing = required - names
            if missing:
                raise RuntimeError(f"missing MCP tools: {sorted(missing)}")

            for path in workflow_paths:
                result = await session.call_tool(
                    "validate_workflow", {"workflow_path": str(path)}
                )
                text = content_text(result)
                print("VALIDATE", path.stem, text, flush=True)
                if '"valid": true' not in text.lower():
                    raise RuntimeError(f"workflow validation failed: {path}: {text}")

            jobs = []
            for shot, path in zip(SHOTS, workflow_paths):
                result = await session.call_tool(
                    "run_workflow", {"workflow_path": str(path), "wait": False}
                )
                prompt_id = prompt_id_from(result)
                jobs.append((shot, prompt_id))
                print("QUEUED", shot["slug"], prompt_id, flush=True)

            (ROOT / "jobs.json").write_text(
                json.dumps(
                    {shot["slug"]: prompt_id for shot, prompt_id in jobs}, indent=2
                ),
                encoding="utf-8",
            )

            fetched_dir = ROOT / "fetched"
            fetched_dir.mkdir(parents=True, exist_ok=True)
            for shot, prompt_id in jobs:
                for attempt in range(100):
                    result = await session.call_tool(
                        "job", {"action": "status", "prompt_id": prompt_id}
                    )
                    status_text = content_text(result).lower()
                    print(
                        "STATUS", shot["slug"], attempt, status_text[:500], flush=True
                    )
                    if "completed" in status_text:
                        break
                    if any(word in status_text for word in ("failed", "cancelled")):
                        raise RuntimeError(
                            f"H3 job failed: {shot['slug']}: {status_text}"
                        )
                    await asyncio.sleep(10)
                else:
                    raise TimeoutError(f"H3 job timeout: {shot['slug']}")

                fetched = await session.call_tool(
                    "fetch_outputs",
                    {"prompt_id": prompt_id, "out_dir": str(fetched_dir)},
                )
                print(
                    "FETCHED", shot["slug"], content_text(fetched), flush=True
                )

    print("H3_MEADOW_ALL_DONE", flush=True)


if __name__ == "__main__":
    asyncio.run(main())
