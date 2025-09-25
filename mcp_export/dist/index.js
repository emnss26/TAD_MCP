import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
// Server MCP
const server = new McpServer({
    name: "mcp-export",
    version: "1.0.0",
});
// Helper: devuelve JSON pretty como texto
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
/* ======================
   export.nwc
   ====================== */
const ExportNwcShape = {
    folder: z.string().optional(),
    filename: z.string().optional(),
    convertElementProperties: z.boolean().optional(),
    // Selección de vistas (todas opcionales; el bridge hace fallbacks)
    viewId: z.number().int().optional(),
    viewIds: z.array(z.number().int()).optional(),
    viewName: z.string().optional(),
    viewNames: z.array(z.string()).optional(),
};
const ExportNwcSchema = z.object(ExportNwcShape);
server.registerTool("export_nwc", {
    title: "Export NWC",
    description: "Exporta a Navisworks (.nwc). Acepta 1 o varias vistas por id/nombre. Si hay varias y no das filename, usa el nombre de la vista.",
    inputSchema: ExportNwcShape,
}, async (args) => {
    const result = await postRevit("export.nwc", args);
    return asText(result);
});
/* ======================
   export.dwg
   ====================== */
const ExportDwgShape = {
    folder: z.string().optional(),
    filename: z.string().optional(),
    exportSetupName: z.string().optional(), // si tienes un setup guardado en Revit
    viewIds: z.array(z.number().int()).optional(), // si omites, usa la vista activa
};
const ExportDwgSchema = z.object(ExportDwgShape);
server.registerTool("export_dwg", {
    title: "Export DWG",
    description: "Exporta vistas/laminas a DWG. Si no pasas viewIds, usa la vista activa.",
    inputSchema: ExportDwgShape,
}, async (args) => {
    const result = await postRevit("export.dwg", args);
    return asText(result);
});
/* ======================
   export.pdf
   ====================== */
const ExportPdfShape = {
    folder: z.string().optional(),
    filename: z.string().optional(), // sin extensión; el bridge añade .pdf
    combine: z.boolean().optional(), // Revit 2022+: combinar en un único PDF
    viewOrSheetIds: z.array(z.number().int()).optional(), // si omites, usa la vista activa
};
const ExportPdfSchema = z.object(ExportPdfShape);
server.registerTool("export_pdf", {
    title: "Export PDF",
    description: "Exporta a PDF (Revit 2022+). Si no pasas viewOrSheetIds, usa la vista activa.",
    inputSchema: ExportPdfShape,
}, async (args) => {
    const result = await postRevit("export.pdf", args);
    return asText(result);
});
// stdio
const transport = new StdioServerTransport();
await server.connect(transport);
