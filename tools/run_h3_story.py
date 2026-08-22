import asyncio
import copy
import json
import os
import re
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


ROOT = Path("/home/work/media-lab-data/minimax-h3/runs/neon-breach-story")
TEMPLATE = Path(
    "/home/work/media-lab-data/minimax-h3/workflows/"
    "minimax-h3-r2v-long-segment-guided-api.json"
)
MCP_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy-mcp"
COMFY_BIN = "/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/comfy"

SHOTS = (
    {
        "slug": "01_alert",
        "image": "nb-story-alert.jpg",
        "guide": "nb-story-alert-guide.mp4",
        "seed": 2026082201,
        "prompt": (
            "Premium stylized 3D sci-fi game cinematic. A dark navy neon training "
            "arena locks down under sweeping red emergency strobes. The large "
            "foreground Overseer drone powers on first, its magenta core pulses, "
            "then the smaller drones wake in depth. Subtle controlled camera push "
            "forward, coherent mechanical motion, cyan rim light, tense volumetric "
            "haze. Preserve the exact arena and drone designs. No cuts, no text, no "
            "logo, no watermark, no malformed limbs, no camera shake."
        ),
    },
    {
        "slug": "02_comms",
        "image": "nb-story-comms.jpg",
        "guide": "nb-story-comms-guide.mp4",
        "seed": 2026082202,
        "prompt": (
            "Premium stylized 3D sci-fi game cinematic holographic transmission. "
            "RIN urgently warns the player, maintaining the exact same face, short "
            "silver-blue hair, magenta visor, navy-white armor and body proportions. "
            "Natural subtle breathing and lip motion, one blink, restrained cyan "
            "scanline shimmer and small hologram interference at the frame edges. "
            "Slow stable push in, face sharp and unobscured. No redesign, no cuts, "
            "no text, no logo, no watermark, no anatomy distortion."
        ),
    },
    {
        "slug": "03_breach",
        "image": "nb-story-breach.jpg",
        "guide": "nb-story-breach-guide.mp4",
        "seed": 2026082203,
        "prompt": (
            "Premium stylized 3D sci-fi action cinematic. Preserve RIN's exact "
            "identity, silver-blue hair, magenta visor, navy-white cyan-lit armor and "
            "compact pistol. She decisively finishes raising the weapon toward camera "
            "and steadies her aim without firing. The cyan circular scanner glow grows "
            "brighter from the perimeter and closes toward the lens as a clean "
            "transition into first-person gameplay. Stable face and five-finger hand, "
            "controlled camera push, no cuts, no muzzle flash, no text, no logo, no "
            "watermark, no deformation."
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
    graph["save"]["inputs"]["filename_prefix"] = f"neon_breach_story/{shot['slug']}"
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

    print("H3_STORY_ALL_DONE", flush=True)


if __name__ == "__main__":
    asyncio.run(main())
