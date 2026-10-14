param(
    [string]$GameDirectory='W:\SteamLibrary\steamapps\common\Derail Valley',
    [string]$CsvPath=(Join-Path (Split-Path $PSScriptRoot -Parent) 'package/translations.csv')
)
$ErrorActionPreference='Stop'
# Use the game's parser: quoted translations can contain real newlines.
[Reflection.Assembly]::LoadFrom((Join-Path $GameDirectory 'DerailValley_Data/Managed/I2.Localization.dll')) | Out-Null
$rows=[I2.Loc.LocalizationReader]::ReadCSV([IO.File]::ReadAllText($CsvPath),',')
if($rows.Count -lt 3) {throw 'Translation CSV has no terms.'}
$columns=$rows[0].Length
for($i=1;$i -lt $rows.Count;$i++) {
    if($rows[$i].Length -ne $columns) {
        throw "Translation CSV row $($i+1) ($($rows[$i][0])) has $($rows[$i].Length) columns; expected $columns."
    }
}
Write-Output "Translation CSV validated: $($rows.Count-2) terms, $columns columns."
