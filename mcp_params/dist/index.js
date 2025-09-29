import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { postRevit } from "./bridge.js";
const server = new McpServer({
    name: "mcp-params",
    version: "1.0.0",
});
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
// Registrar herramientas SIN schemas - dejar que el bridge valide
server.registerTool("params_get", {
    description: "Lee parámetros de elementos. Ejemplo: {elementIds: [344900], paramNames: ['Comments']}",
}, async (args) => {
    try {
        const result = await postRevit("params.get", args);
        return asText(result);
    }
    catch (e) {
        return asText({ ok: false, error: String(e?.message ?? e) });
    }
});
server.registerTool("params_set", {
    description: "Actualiza parámetros. Ejemplo: {updates: [{elementId: 344900, param: 'Comments', value: 'texto'}]}",
}, async (args) => {
    try {
        console.error("=== params_set ===");
        console.error("Args recibidos:", JSON.stringify(args, null, 2));
        const result = await postRevit("params.set", args);
        console.error("Resultado:", JSON.stringify(result, null, 2));
        return asText(result);
    }
    catch (e) {
        console.error("Error:", e);
        return asText({ ok: false, error: String(e?.message ?? e) });
    }
});
server.registerTool("params_set_where", {
    description: "Actualiza parámetros con filtros. Ejemplo: {where: {elementIds: [344900]}, set: [{param: 'Comments', value: 'texto'}]}",
}, async (args) => {
    try {
        const result = await postRevit("params.set_where", args);
        return asText(result);
    }
    catch (e) {
        return asText({ ok: false, error: String(e?.message ?? e) });
    }
});
server.registerTool("params_bulk_from_table", {
    description: "Actualiza parámetros desde tabla. Formato: {updates: [{elementId, param, value}]}",
}, async (args) => {
    try {
        const result = await postRevit("params.bulk_from_table", args);
        return asText(result);
    }
    catch (e) {
        return asText({ ok: false, error: String(e?.message ?? e) });
    }
});
const transport = new StdioServerTransport();
await server.connect(transport);
