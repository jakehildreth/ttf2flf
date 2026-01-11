Add-Type -Path "./Lib/SixLabors.ImageSharp.dll"
Add-Type -Path "./Lib/SixLabors.Fonts.dll"
Add-Type -Path "./Lib/SixLabors.ImageSharp.Drawing.dll"
. ./Private/Get-GlyphBitmap.ps1
. ./Private/ConvertTo-HalfBlockCharacters.ps1

$fontCollection = [SixLabors.Fonts.FontCollection]::new()
$family = $fontCollection.Add("./Tests/TestData/SourceTTF/Tiny5.ttf")

# Compare rendering at size 5 vs size 7
foreach ($size in @(5, 7)) {
    $font = $family.CreateFont($size)
    $bitmap = Get-GlyphBitmap -Font $font -Character ([char]72) -Height $size -PixelPerfect
    Write-Host "H at font size ${size}:"
    for ($y = 0; $y -lt $bitmap.Pixels.Count; $y++) {
        $row = $bitmap.Pixels[$y]
        $display = ($row | ForEach-Object { if ($_ -gt 0.5) { "█" } else { " " } }) -join ""
        Write-Host "  [$display]"
    }
    Write-Host ""
}
