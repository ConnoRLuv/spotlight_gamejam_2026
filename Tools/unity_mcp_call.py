"""Call the configured local Unity MCP bridge when its tools are not exposed by the client."""
import asyncio
import json
import sys
from pathlib import Path
from fastmcp import Client


async def main():
    async with Client("http://127.0.0.1:8080/mcp", timeout=120) as client:
        action = sys.argv[1]
        if action == "resource":
            response = await client.read_resource(sys.argv[2])
            for item in response:
                if hasattr(item, "text"):
                    print(item.text)
        elif action == "schema":
            for item in await client.list_tools():
                if item.name in sys.argv[2:]:
                    print(json.dumps({"name": item.name, "schema": item.inputSchema}))
        else:
            raw = sys.argv[2] if len(sys.argv) > 2 else "{}"
            arguments = json.loads(Path(raw[1:]).read_text(encoding="utf-8") if raw.startswith("@") else raw)
            response = await client.call_tool(action, arguments)
            for item in response.content:
                if hasattr(item, "text"):
                    print(item.text)
            if response.is_error:
                sys.exit(1)


asyncio.run(main())
