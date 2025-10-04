import { applyTemplate } from "./tmpl.js";
export async function runPlan(post, plan) {
    const report = [];
    const scope = { vars: plan.vars ?? {}, lastResult: null };
    for (const step of plan.steps) {
        try {
            // 1) requires
            if (step.require?.length) {
                for (const r of step.require) {
                    const rArgs = applyTemplate(r.args ?? {}, scope);
                    const rr = await post(r.action, rArgs);
                    const ok = r.expect ? !!r.expect(rr) : true;
                    if (!ok)
                        throw new Error(`require failed for ${r.action}`);
                }
            }
            // 2) wait
            if (step.waitMs && step.waitMs > 0)
                await new Promise(res => setTimeout(res, step.waitMs));
            // 3) foreach o simple
            if (step.foreach) {
                const it = scope.vars?.[step.foreach];
                if (!Array.isArray(it))
                    throw new Error(`foreach expects array vars.${step.foreach}`);
                const created = [];
                for (const item of it) {
                    const args = applyTemplate(step.argsTemplate ?? {}, { ...scope, item });
                    const res = plan.dryRun ? { dryRun: true, wouldCall: step.action, args } : await post(step.action, args);
                    created.push(res);
                    scope.lastResult = res;
                }
                report.push({ step, ok: true, results: created });
            }
            else if (step.action) {
                const args = applyTemplate(step.args ?? {}, scope);
                const res = plan.dryRun ? { dryRun: true, wouldCall: step.action, args } : await post(step.action, args);
                scope.lastResult = res;
                report.push({ step, ok: true, result: res });
            }
            // 4) setVar
            if (step.setVar) {
                scope.vars[step.setVar.name] = (step.setVar.from === "lastResult") ? scope.lastResult : scope.vars[step.setVar.from];
            }
        }
        catch (err) {
            report.push({ step, ok: false, error: String(err?.message || err) });
            if (!plan.continueOnError)
                break;
        }
    }
    return { ok: report.every(r => r.ok !== false), steps: report, vars: scope.vars };
}
