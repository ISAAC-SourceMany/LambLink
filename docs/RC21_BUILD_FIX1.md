# RC21 build fix 1

Windows build evidence reported `CS1983` at `Plugin.cs` lines 118 and 477. Both methods used the
unqualified non-generic return type `Task`, which resolved to a same-named type from the Cult of
the Lamb game references instead of `System.Threading.Tasks.Task`.

The fix fully qualifies all task types and `Task.Delay` calls in `Plugin.cs`. It also changes the
PowerShell build scripts to inspect `$LASTEXITCODE` immediately after every `dotnet` invocation.
This prevents a failed compilation from continuing into misleading `Copy-Item` errors.

Expected result from `build-test-pair.ps1`:

```text
[OK] RC21 command-first dispatch + watchdog + cached status fallback plugin built and verified.
[OK] RC21 Companion built and verified.
[OK] RC21 matched Mod + Companion test pair is ready.
```
