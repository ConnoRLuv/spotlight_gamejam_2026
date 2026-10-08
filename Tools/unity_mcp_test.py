"""Run Unity EditMode tests through the already configured local MCP server."""
import asyncio
import json
import sys
from pathlib import Path
from fastmcp import Client

async def main():
    async with Client("http://127.0.0.1:8080/mcp", timeout=120) as client:
        if len(sys.argv) > 1 and sys.argv[1] == "poll":
            result = await client.call_tool("get_test_job", {
                "job_id": sys.argv[2], "wait_timeout": 30, "include_failed_tests": True
            })
        else:
            result = await client.call_tool("run_tests", {"mode": "EditMode"})
        def payload(response):
            return json.loads(next(b.text for b in response.content if hasattr(b, "text")))
        value = payload(result)
        if not value.get("success"):
            print(json.dumps(value)); sys.exit(2)
        job_id = value["data"]["job_id"]
        while value["data"]["status"] in ("running", "queued"):
            value = payload(await client.call_tool("get_test_job", {
                "job_id": job_id, "wait_timeout": 30, "include_failed_tests": True
            }))
        Path("Logs").mkdir(exist_ok=True)
        Path(f"Logs/editmode-{job_id}.json").write_text(json.dumps(value, indent=2), encoding="utf-8")
        print(json.dumps({"job_id": job_id, "status": value["data"]["status"],
            "summary": (value["data"].get("result") or {}).get("summary"),
            "failures": value["data"].get("progress", {}).get("failures_so_far", [])}))
        sys.exit(0 if value["data"]["status"] == "succeeded" else 1)

asyncio.run(main())
