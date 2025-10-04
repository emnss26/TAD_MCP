import { z } from "zod";
export declare const PlanInput: z.ZodObject<{
    intent: z.ZodEnum<["create_wall", "place_door", "place_window", "create_floor", "create_roof", "create_beam", "create_column", "create_pipe", "create_duct"]>;
    args: z.ZodOptional<z.ZodRecord<z.ZodString, z.ZodAny>>;
}, "strip", z.ZodTypeAny, {
    intent: "create_wall" | "place_door" | "place_window" | "create_floor" | "create_roof" | "create_beam" | "create_column" | "create_pipe" | "create_duct";
    args?: Record<string, any> | undefined;
}, {
    intent: "create_wall" | "place_door" | "place_window" | "create_floor" | "create_roof" | "create_beam" | "create_column" | "create_pipe" | "create_duct";
    args?: Record<string, any> | undefined;
}>;
export type PlanStep = {
    tool: string;
    args: any;
    why: string;
};
export declare function orchestratePlan(input: z.infer<typeof PlanInput>): PlanStep[];
