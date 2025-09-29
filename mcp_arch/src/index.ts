import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";

// Server MCP
const server = new McpServer({
  name: "mcp-graphics",
  version: "1.0.0",
});

// Helper: respuesta como texto (JSON pretty)
const asText = (obj: unknown) => ({
  content: [{ type: "text" as const, text: JSON.stringify(obj, null, 2) }],
});

/* =========================================
   view.category.set_visibility
========================================= */
const SetVisibilityShape = {
  categories: z.array(z.string()).min(1),
  visible: z.boolean().optional(),
  forceDetachTemplate: z.boolean().optional(),
  viewId: z.number().int().optional(),
};
const SetVisibilitySchema = z.object(SetVisibilityShape);

server.registerTool(
  "graphics_set_visibility",
  {
    title: "Set Category Visibility",
    description:
      "Muestra/oculta categorías en la vista actual o una dada. Usa forceDetachTemplate para desvincular plantilla.",
    inputSchema: SetVisibilityShape,
  },
  async (args: z.infer<typeof SetVisibilitySchema>) => {
    const result = await postRevit("view.category.set_visibility", args);
    return asText(result);
  }
);

/* =========================================
   view.category.clear_overrides
========================================= */
const ClearOverridesShape = {
  categories: z.array(z.string()).min(1),
  forceDetachTemplate: z.boolean().optional(),
  viewId: z.number().int().optional(),
};
const ClearOverridesSchema = z.object(ClearOverridesShape);

server.registerTool(
  "graphics_clear_overrides",
  {
    title: "Clear Category Overrides",
    description:
      "Elimina overrides gráficos de las categorías en la vista objetivo.",
    inputSchema: ClearOverridesShape,
  },
  async (args: z.infer<typeof ClearOverridesSchema>) => {
    const result = await postRevit("view.category.clear_overrides", args);
    return asText(result);
  }
);

/* =========================================
   view.category.override_color
========================================= */
const Rgb = z.object({
  r: z.number().int().min(0).max(255),
  g: z.number().int().min(0).max(255),
  b: z.number().int().min(0).max(255),
});
const Hex = z.string().regex(/^#?[0-9A-Fa-f]{6}$/, "Use #RRGGBB");

const OverrideColorShape = {
  categories: z.array(z.string()).min(1),
  color: z.union([Hex, Rgb]),
  transparency: z.number().int().min(0).max(100).optional(),
  halftone: z.boolean().optional(),
  surfaceSolid: z.boolean().optional(),
  projectionLines: z.boolean().optional(),
  forceDetachTemplate: z.boolean().optional(),
  viewId: z.number().int().optional(),
};
const OverrideColorSchema = z.object(OverrideColorShape);

server.registerTool(
  "graphics_override_color",
  {
    title: "Override Category Color",
    description:
      "Aplica color/trasparencia/halftone a categorías; soporta color #RRGGBB o {r,g,b}.",
    inputSchema: OverrideColorShape,
  },
  async (args: z.infer<typeof OverrideColorSchema>) => {
    const result = await postRevit("view.category.override_color", args);
    return asText(result);
  }
);

/* =========================================
   view.apply_template
========================================= */
const ApplyTemplateShape = {
  viewId: z.number().int().optional(),
  templateId: z.number().int().optional(),
  templateName: z.string().optional(),
};
const ApplyTemplateSchema = z.object(ApplyTemplateShape);

server.registerTool(
  "view_apply_template",
  {
    title: "Apply View Template",
    description:
      "Aplica una View Template por id o nombre a la vista actual o una dada.",
    inputSchema: ApplyTemplateShape,
  },
  async (args: z.infer<typeof ApplyTemplateSchema>) => {
    const result = await postRevit("view.apply_template", args);
    return asText(result);
  }
);

/* =========================================
   view.set_scale
========================================= */
const SetScaleShape = {
  viewId: z.number().int().optional(),
  scale: z.number().int().min(1),
};
const SetScaleSchema = z.object(SetScaleShape);

server.registerTool(
  "view_set_scale",
  {
    title: "Set View Scale",
    description: "Cambia la escala de la vista.",
    inputSchema: SetScaleShape,
  },
  async (args: z.infer<typeof SetScaleSchema>) => {
    const result = await postRevit("view.set_scale", args);
    return asText(result);
  }
);

/* =========================================
   view.set_detail_level
========================================= */
const SetDetailLevelShape = {
  viewId: z.number().int().optional(),
  detailLevel: z.enum(["coarse", "medium", "fine"]).optional(),
};
const SetDetailLevelSchema = z.object(SetDetailLevelShape);

server.registerTool(
  "view_set_detail_level",
  {
    title: "Set Detail Level",
    description: "Ajusta el nivel de detalle: coarse | medium | fine.",
    inputSchema: SetDetailLevelShape,
  },
  async (args: z.infer<typeof SetDetailLevelSchema>) => {
    const result = await postRevit("view.set_detail_level", args);
    return asText(result);
  }
);

/* =========================================
   view.set_discipline
========================================= */
const SetDisciplineShape = {
  viewId: z.number().int().optional(),
  discipline: z
    .enum(["architectural", "structural", "mechanical", "coordination"])
    .optional(),
};
const SetDisciplineSchema = z.object(SetDisciplineShape);

server.registerTool(
  "view_set_discipline",
  {
    title: "Set View Discipline",
    description:
      "Cambia la disciplina de la vista: architectural | structural | mechanical | coordination.",
    inputSchema: SetDisciplineShape,
  },
  async (args: z.infer<typeof SetDisciplineSchema>) => {
    const result = await postRevit("view.set_discipline", args);
    return asText(result);
  }
);

/* =========================================
   view.set_phase
========================================= */
const SetPhaseShape = {
  viewId: z.number().int().optional(),
  phase: z.string().optional(),
};
const SetPhaseSchema = z.object(SetPhaseShape);

server.registerTool(
  "view_set_phase",
  {
    title: "Set View Phase",
    description:
      "Define la fase de la vista (por nombre). Si no se especifica, el bridge usa la última fase.",
    inputSchema: SetPhaseShape,
  },
  async (args: z.infer<typeof SetPhaseSchema>) => {
    const result = await postRevit("view.set_phase", args);
    return asText(result);
  }
);

/* =========================================
   views.duplicate
========================================= */
const ViewsDuplicateShape = {
  viewIds: z.array(z.number().int()).default([]),
  mode: z.enum(["duplicate", "with_detailing", "as_dependent"]).optional(),
};
const ViewsDuplicateSchema = z.object(ViewsDuplicateShape);

server.registerTool(
  "views_duplicate",
  {
    title: "Duplicate Views",
    description:
      "Duplica vistas por ids. mode: duplicate | with_detailing | as_dependent.",
    inputSchema: ViewsDuplicateShape,
  },
  async (args: z.infer<typeof ViewsDuplicateSchema>) => {
    const result = await postRevit("views.duplicate", args);
    return asText(result);
  }
);

/* =========================================
   imports.hide
========================================= */
const HideImportsShape = {
  viewId: z.number().int().optional(),
};
const HideImportsSchema = z.object(HideImportsShape);

server.registerTool(
  "imports_hide",
  {
    title: "Hide CAD Imports (View)",
    description:
      "Oculta importaciones CAD en la vista actual o la vista indicada.",
    inputSchema: HideImportsShape,
  },
  async (args: z.infer<typeof HideImportsSchema>) => {
    const result = await postRevit("imports.hide", args);
    return asText(result);
  }
);

// stdio
const transport = new StdioServerTransport();
await server.connect(transport);
