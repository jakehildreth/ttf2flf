# FLF Showcase

Interactive terminal browser for the 79 FIGlet fonts in `./fonts` — the full
`name_X_vY` bitmap collection plus the repo's test fonts, all converted to pixel-perfect
`.flf` with [ttf2flf](../README.md).

## Run

```powershell
./Show-FLFShowcase.ps1
```

Requires PowerShell 7+ and the **PwshSpectreConsole** module
(`Install-Module PwshSpectreConsole`). Rendering uses Spectre.Console's built-in
FIGlet engine (`Write-SpectreFigletText`), which is pixel-identical to `figlet` —
no native `figlet` binary needed.

## Use

A searchable list of every font. **Type to filter**, `up`/`down` to move, `enter`
to render the sample "Sphinx of black quartz, judge my vow 0123456789" in the
selected font. Any key returns to the list; `esc` quits.

```powershell
# Custom sample text
./Show-FLFShowcase.ps1 -Sample 'Hello World'
```

## Files

- `fonts/*.flf` — 79 converted fonts (all pass `Test-FLFFile`)
- `Show-FLFShowcase.ps1` — the browser
