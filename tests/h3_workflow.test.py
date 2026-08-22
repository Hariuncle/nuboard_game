import importlib.util
import json
import sys
import types
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
fake_mcp = types.ModuleType("mcp")
fake_mcp.ClientSession = object
fake_mcp.StdioServerParameters = object
fake_mcp_client = types.ModuleType("mcp.client")
fake_mcp_stdio = types.ModuleType("mcp.client.stdio")
fake_mcp_stdio.stdio_client = object
sys.modules.setdefault("mcp", fake_mcp)
sys.modules.setdefault("mcp.client", fake_mcp_client)
sys.modules.setdefault("mcp.client.stdio", fake_mcp_stdio)
SPEC = importlib.util.spec_from_file_location("run_h3_meadow", ROOT / "tools" / "run_h3_meadow.py")
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class H3WorkflowTest(unittest.TestCase):
    def test_inner_h3_subgraph_does_not_keep_comfyui_demo_prompt(self):
        template = json.loads(
            (ROOT / "tools" / "templates" / "video_minimax_h3_i2v.json").read_text(encoding="utf-8")
        )
        shot = MODULE.SHOTS[0]
        graph = MODULE.workflow_for(template, shot)
        nodes = {
            node["id"]: node
            for subgraph in graph["definitions"]["subgraphs"]
            for node in subgraph["nodes"]
        }

        self.assertEqual(nodes[104]["widgets_values"], [shot["prompt"], 1344, 768, 124])
        self.assertEqual(nodes[111]["widgets_values"], [5])
        self.assertEqual(nodes[15]["widgets_values"], [shot["seed"], "fixed"])
        self.assertNotIn("DIRECTED BY COMFYUI", nodes[104]["widgets_values"][0])


if __name__ == "__main__":
    unittest.main()
