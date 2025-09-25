// src/index.ts
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";

const SERVER_NAME = process.env.MCP_NAME ?? "mcp_export";   // cámbialo por MCP
const SERVER_VERSION = "0.1.0";
const REVIT_ENDPOINT = process.env.REVIT_ENDPOINT ?? "http://127.0.0.1:55234/mcp";

// Helper para llamar al Bridge (C#)
async function callRevit(action: string, args: any) {
  const res = await fetch(REVIT_ENDPOINT, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ action, args }),
  });
  const payload = await res.json().catch(() => ({}));
  if (!res.ok || payload?.ok === false) {
    const msg = payload?.message || `HTTP ${res.status}`;
    throw new Error(`RevitBridge error on '${action}': ${msg}`);
  }
  return payload.data;
}

const server = new Server(
  { name: SERVER_NAME, version: SERVER_VERSION },
  { capabilities: { tools: {} } }
);

// Registrador rápido de tools con schema abierto (proxy 1:1 al action del Bridge)
function registerTools(names: string[]) {
  for (const name of names) {
    server.tool(
      {
        name,
        description: `Proxy of RevitBridge action '${name}'.`,
        // dejamos schema abierto para no encorsetar; tu validación vive en C#
        inputSchema: { type: "object", additionalProperties: true },
      },
      async (args) => callRevit(name, args)
    );
  }
}

// ======== CAMBIA ESTA LISTA SEGÚN EL MCP ========
const TOOLS: string[] = [
  // EJEMPLO para Arquitectura (sustitúyelo abajo por cada MCP)
  "export.nwc",
  "export.dwg",
  "export.pdf",
];

registerTools(TOOLS);

async function main() {
  await server.connect(new StdioServerTransport());
  // opcional: log no bloqueante para confirmar arranque en local
  if (process.env.DEBUG?.toLowerCase() === "true") {
    console.error(`[${SERVER_NAME}] ready. Bridge -> ${REVIT_ENDPOINT}`);
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
