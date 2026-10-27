import { Instance } from "cs_script/point_script";
// Only for the 27025 test server. Never part of the Workshop pack or real map.
let remaining = 0;
Instance.SetThink(() => {
    if (--remaining > 0) Instance.SetNextThink(Instance.GetGameTime() + 0.05);
});
Instance.OnScriptInput("Start", () => {
    remaining = 20;
    Instance.SetNextThink(Instance.GetGameTime() + 0.05);
});
Instance.OnScriptInput("Event", () => { Instance.Msg("ZE_SCRIPT_FIXTURE event handled"); });
Instance.OnScriptInput("Throw", () => { throw new Error("ZE_SCRIPT_FIXTURE expected caught exception"); });
