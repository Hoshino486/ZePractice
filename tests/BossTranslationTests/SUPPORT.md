# Boss / translation adapter: tested support matrix

## Files and verification

- `src/BossTranslationService.cs`: CounterStrikeSharp 1.0.376/.NET 10 runtime adapter.
- `src/BossTranslationLogic.cs`: engine-independent JSONC parsing, matching, HP gauges, stage/timer state, safe text rendering.
- `tests/BossTranslationTests`: dependency-free executable tests (`dotnet run --project tests/BossTranslationTests --configuration Release`).
- 2026-10-11 pure-test run: **1046 checks passed**; actual FYS `2001` input **316 Boss files / 1468 definitions / 31 Proxy files**, **677 translation files / 37073 phrases**.
- These are parser/state tests, NOT proof of in-game HUD, CSS hooks, binary ABI or cross-framework compatibility. No server launch, deployment or changes to game files were made by this task.

## Critical integration issue

Official CS2Fixes `main/src/entwatch.cpp` defines `entwatch_enable` with the description **INCOMPATIBLE WITH CS#**. Do not advertise native CS2Fixes EntWatch + CounterStrikeSharp as a known-working combination. The default plain center HUD below avoids reusing the usual HTML event channel, but does not fix native incompatibility. Root integration must either implement item tracking in CSS or verify/change the native integration before enabling that feature. See `INTEGRATION-NOTE.txt` for exact upstream location.

## Inputs / binding / live state

| FYS field | Implemented behavior | Boundary |
|---|---|---|
| `Breakables.target` | Match real entity targetname; read schema `CBaseEntity.Health/MaxHealth`; listen to health/damage/break outputs | Not a rewrite of map HP. No entity -> logged unmatched, no fake display |
| `Monsters.identity` | Exact `CBaseEntity.UniqueHammerID` string, handles both health entities and math counters | Never interpret Hammer ID as runtime entity index. Existing converted map may have changed IDs |
| `Counters.iterator` | Observe `OutValue`, `OnGetValue`, `OnHitMin`, `OnHitMax`; live schema min/max/disabled | No unsafe offsets. `SetValueNoFire`/pre-hook initial value is not readable through public CSS output API; show waiting until a genuine output. We do not force `GetValue` because that can fire map IO |
| `hitbox` | Entity damage activates the corresponding boss for 8 seconds | Uses serial-bearing handles, not bare indices |
| `increase` | Up-counting iterator displays `max - value` remaining HP | If real max is absent, display upward progress, not invented HP |
| `backup` | Observed backup value provides current segment max | Initial backup can also be pending until its first real output |
| `counter` / `reverse` | Observe segment counter; reverse uses actual max minus value | Shows **segment HP plus segment count separately**, not an undocumented aggregate formula |
| `mass` | Parsed, retained, diagnostic/status labelled segmented HP | Public comments only say “backup algorithm parameter”; the private formula is unproven. Never multiply by player count or manufacture whole-boss total |
| `stages` | Current stage advances only after observed terminal hit then refill, clamped to configured total | Observation-based approximation of map transitions, not hidden FYS internal state |
| `count` | Health-bar visual subdivisions, clamped 1..32 | Default10. No current FYS 2001 file sets this field; behavior follows field comment |
| `Proxy` | Parsed with explicit warning; explicit other selectors still operate | Private/native proxy protocol is not public; not silently claimed supported |
| unknown sections/fields | Report diagnostic | Real files have no separate `modes`; direction modes are `increase/reverse` |

Targetnames support exact match, trailing `*`, and genuine template `&NNNN` suffixes. Auxiliary counters preferentially bind the same template instance; an unsuffixed global backup may be shared. Different template instances are never picked arbitrarily. Entity handles include serial numbers; output values and bindings reset at map/round changes. Registry scans at most once a second on spawn churn, otherwise every three seconds; rendering defaults to 0.5 seconds and reads only tracked entities. Output hooks use separate delegate instances because CSS keys its subscriptions by delegate.

`ze_forhidden_slicoo_cs2` currently has one explicit counter (`drg7_hp_1`, `drg7_hp_3`, `drg7_hitbox`, reverse, mass174) and three Monster IDs (`4134`, `17238`, `17181`). For alias `ze_forhidden_slicoo_cn`, loading the source config does not guarantee the converted map still has these names/IDs. `css_bossstatus` and `FYS_BOSS_BIND/WAIT` logs identify this precisely.

## Translation and countdown

| Field/behavior | Implementation |
|---|---|
| Original JSONC | Comments, trailing commas, BOM; exact source-string dictionary; original files are never rewritten |
| Source capture | Server-only `say` pre-listener (player chat skipped) plus SayText2 entityindex0 and chat-destination TextMsg fallback |
| `translation` | Suppress original; output localized chat next frame. Never execute translated text as a console command |
| `blocked` | Suppress sentence in chat and subtitle; independent explicit clear/countdown operations still apply |
| `clearText` | Clear this adapter's subtitle queue; does not erase other plugins' HUD data |
| `clearTimer` | Clear all this adapter's map countdowns before adding this rule's new timer |
| `countdown` | Independent real deadlines; multiple timers preserved; show two soonest; bounded16 live timers; round/map/unload clears |
| colors | Common `{green}`/`{red}` etc -> CS chat color controls; plain center removes colors; optional HTML uses an allowlisted color map and HTML-escapes all other content |
| normalization | Exact wins. Optional whitespace/one surrounding quote/control-color normalization only; ambiguous normalized keys are not guessed |
| loop / recipient protection | Emission reentrancy guard; callbacks use generation token, serial-bearing recipient IDs; old map queued messages discarded |

A TextMsg without sender data cannot prove its producer is specifically point_servercommand; only exact known phrases in chat destination are translated. No player SayText2 or regular client `say` message is rewritten.

## HUD arbitration / commands

Default **plain center TextMsg** deliberately avoids `show_survival_respawn_status` HTML, a channel used by many item HUDs. CSS active menus suppress our output. Foreign center/alert TextMsg yields for2 seconds for its recipients. Rendering sends no empty background when idle; only a HUD it previously owned is cleared. Server-driven hidden FYS proxy UI and third-party Panorama layouts are not controllable through this arbitration.

- `!bosshud on` / `!bosshud off`: player-local Boss/subtitle/timer display. Chat translation remains independent.
- `css_bossstatus`: admin live bindings/counter observation state.
- `css_bosshudmode center|html|off`: admin in-memory channel setting. HTML is explicit opt-in; disable other writers of that HTML channel before using it.
- Main-plugin `css_zereload`: calls `OnMapStart` and reloads unmodified source JSONC.

No external package, resource preload, Panorama file or renderer is silently installed. For deployment, verify status/health changes in an actual map first; the test report makes no assertion that this has happened.