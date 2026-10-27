# Admin access regression tests

These tests link the actual four `AdminPracticeService` partial files, permission policy/runtime and punishment/map-catalog code. Engine/player/menu/timer adapters are in-memory fixtures. No game process, RCON, deployment or real player permission file is used.

```powershell
dotnet run --project "D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\tests\AdminAccessTests\AdminAccessTests.csproj" -c Release --no-restore
dotnet build "D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\tests\AdminAccessTests\ApiSmoke\ApiSmoke.csproj" -c Release --no-restore
```

The API smoke project additionally compiles those sources against the installed real CounterStrikeSharp API assembly; it is compile-only and is not loaded by CS2.

Coverage: exact per-action grants; role/CSS target immunity; Owner identity; stale menu owner/target and confirmation callbacks; self-vs-target respawn; automatic/deferred respawn revocation; selective god/noclip rollback; original pawn/handle protection; checkpoint clearing; permissions reload with active menus; menu disabled states; read-only map menu; gag-vs-service commands; mode-off/Dispose cleanup.

Integration details:

- `GetPracticeToggles` has `god`, `noclip`, `checkpoint` entries. `RecheckPermissions` is called every maintenance tick, and may be called immediately after permission reload.
- `css_ze_clearcheckpoint` only clears the checkpoint, never god/noclip. Its default grant is in `practicer` and it is included in the practice-disabled filter. `css_ze_practice` remains a public menu entry; actions need their independent grants.
- Added an executable `css_ze_respawn_other <target>` command for the dedicated target permission. This permits a custom role to administer respawn without granting self-practice respawn. The existing `css_ze_respawn <target>` also rechecks the target operation, but its common command router first checks the base `css_ze_respawn` permission.
- `CanTarget` does not grant any action. It blocks owners, higher immunity, and same-level protected administrators; a genuinely higher-level actor may target a lower-level administrator.
- Existing menu owner slot/UserId and target slot/UserId are both rechecked. Confirmation rechecks exact operation permission as well.
- Helper restoration only writes back a property previously applied to the matching captured pawn. Repeated rechecks do not repeatedly restore or reset unrelated helper state.
- The central full-width bridge remains authoritative. The gag filter uses its shared parser to allow permitted command/menu input; ordinary chat is still suppressed.
- Native CS2Fixes permissions/flags and FYS `ms_*` command semantics are not modified here.