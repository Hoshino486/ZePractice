# Permission component integration notes

Runtime API: `new ZePermissionService(plugin)`, `Initialize()`, `Allows(player, canonicalCommand)`, `HasAdminAccess(player)`, `IsOwner(player)`, `Immunity(player)`, `Describe(player)`, `Reload(out error)`, `Dispose()`.

Configuration file is `<plugin.ModuleDirectory>/data/permissions.json`. `ConfigurationPath` exposes the actual path. It is created with defaults only if absent; existing invalid edits are not overwritten. Failed reloads keep the previous snapshot. Initial malformed configuration retains safe player/practicer defaults; no administrator is enrolled.

- Command routing owns alias normalization. The defaults use `css_ze`, `css_zeadmin`, `css_zhelp`, `css_zebuy`, `css_ze_status`, `css_ze_reload`, and the requested UI command keys. Weapon/HE shortcuts and `css_zeweapon` share `css_zebuy`. This policy deliberately does not accept chat strings or command arguments.
- Every command and menu/button callback must perform `Allows` at invocation time. `HasAdminAccess` is classification for existing management UI, not a grant for every administrator operation.
- Roles support exact `css_...`, terminal prefixes such as `css_ze_*`, or `*`; regex is not supported. Deny wins across all assigned/default/inherited roles. Immunity is independent and never grants commands.
- `AllowEveryonePracticeCommands=false` removes the default practicer role and blocks self-practice grants originating only from default roles. Explicit authenticated user-role grants still work. It does not override explicit Deny rules.
- The default admin role denies `css_ze_permissions*` and `css_ze_root*`; only existing Owner/root/loopback/console bypass can reload initially. A trusted custom role may explicitly allow reload, but any inherited or additional admin-role Deny still wins: remove that Deny deliberately or do not combine it with the trusted assignment.
- A role named `owner`, and even explicit `Allow:["*"]`, does not make `IsOwner` true. True owner identity comes only from the existing authenticated OwnerSteamIds, trusted numeric loopback, server/RCON, or CSS root when CssRootBypass=true.
- Users assignments and OwnerSteamIds use AuthorizedSteamID only; raw controller SteamID is never used for authorization. CSS-native commands still have their separate native flags; this module does not rewrite their authorization.
- Normal immunity is max(role inheritance/default/assignment, authenticated CSS immunity), below the owner sentinel `int.MaxValue`.

Verification commands:

```powershell
dotnet run --project "D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\tests\PermissionTests\PermissionTests.csproj" -c Release --no-restore
dotnet build "D:\34184\Documents\ChatGPT\CS2脚本\ZePractice\tests\PermissionTests\ApiSmoke\ApiSmoke.csproj" -c Release --no-restore
```

Tests use synthetic identities and fixture directories only. Reports are written below `E:\mapper\chana\临时文件夹\ze-ui-access-20261011\permission-tests`. No real deployed player permission configuration is read or changed by these tests.