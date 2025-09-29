import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
// Servidor MCP
const server = new McpServer({
    name: "mcp-params",
    version: "1.0.0",
});
// Helper: serializa cualquier resultado como texto
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
// Helper: castea shapes locales a la firma que espera el SDK
const asInputShape = (shape) => shape;
/* ===========================
   params.get
   Args: { elementIds?: number[], paramNames?: string[], includeValueString?: boolean }
=========================== */
const ParamsGetShape = {
    elementIds: z.array(z.number().int()).default([]),
    paramNames: z.array(z.string()).default([]),
    includeValueString: z.boolean().default(true),
};
const ParamsGetSchema = z.object(ParamsGetShape);
server.registerTool("params_get", {
    title: "Get Parameters",
    description: "Lee parámetros (por nombre visible o BuiltInParameter) de una lista de elementos. Puede incluir el ValueString.",
    inputSchema: asInputShape(ParamsGetShape),
}, async (rawArgs) => {
    const args = ParamsGetSchema.parse(rawArgs);
    const result = await postRevit("params.get", args);
    return asText(result);
});
/* ===========================
   params.set
   Args: { updates: { elementId:number, param:string, value:string|number|boolean|null }[] }
=========================== */
const UpdateValueSchema = z.union([z.string(), z.number(), z.boolean(), z.null()]);
// 👇 Definimos explícitamente UpdateItemSchema (antes lo llamabas UpdateItem)
const UpdateItemSchema = z.object({
    elementId: z.number().int(),
    param: z.string(),
    value: UpdateValueSchema,
});
const ParamsSetShape = { updates: z.array(UpdateItemSchema).min(1) };
const ParamsSetSchema = z.object(ParamsSetShape);
server.registerTool("params_set", {
    title: "Set Parameters",
    description: "Actualiza parámetros (instancia o tipo) por nombre o BuiltInParameter.",
    inputSchema: asInputShape(ParamsSetShape),
}, async (rawArgs) => {
    const args = ParamsSetSchema.parse(rawArgs);
    const result = await postRevit("params.set", args);
    return asText(result);
});
/* ===========================
   params.bulk_from_table
   NOTA: El bridge C# espera que este server ya convierta CSV/XLSX a updates[].
   Si no se proveen updates[], el bridge devolverá un mensaje indicándolo.
=========================== */
const ParamsBulkFromTableShape = {
    // updates[] puede venir o no; si no viene, el bridge responderá el aviso correspondiente
    updates: z.array(UpdateItemSchema).optional(),
    // Opcionales (por si en el futuro procesas CSV/XLSX del lado TS)
    tableCsv: z.string().optional(),
    tableXlsxPath: z.string().optional(),
    idColumn: z.string().optional(),
    paramColumn: z.string().optional(),
    valueColumn: z.string().optional(),
};
const ParamsBulkFromTableSchema = z.object(ParamsBulkFromTableShape);
server.registerTool("params_bulk_from_table", {
    title: "Set Parameters from Table",
    description: "Actualiza parámetros desde una tabla. Este server puede enviar updates[] directamente; si no, el bridge responderá que espera updates[].",
    inputSchema: asInputShape(ParamsBulkFromTableShape),
}, async (rawArgs) => {
    const args = ParamsBulkFromTableSchema.parse(rawArgs);
    const result = await postRevit("params.bulk_from_table", args);
    return asText(result);
});
// Arrancar stdio
const transport = new StdioServerTransport();
await server.connect(transport);
