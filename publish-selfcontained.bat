@echo off
echo Publishing self-contained builds for win-x64...
echo.

for %%P in (
    Gibbed.MadMax.SmallUnpack
    Gibbed.MadMax.SmallPack
    Gibbed.MadMax.Unpack
    Gibbed.MadMax.ConvertAdf
    Gibbed.MadMax.ConvertProperty
    Gibbed.MadMax.ConvertSpreadsheet
    Gibbed.MadMax.XvmAssemble
    Gibbed.MadMax.XvmCompile
    Gibbed.MadMax.XvmDecompile
    Gibbed.MadMax.XvmDisassemble
    Gibbed.MadMax.BinarySearch
    Gibbed.MadMax.ResolveHashes
    RebuildFileLists
) do (
    echo Building %%P...
    dotnet publish %%P\%%P.csproj -r win-x64 --self-contained -c Release -p:PublishTrimmed=true -p:PublishSingleFile=true -o publish
    echo.
)

echo.
echo Done! Self-contained builds are in the "publish" folder.
pause
