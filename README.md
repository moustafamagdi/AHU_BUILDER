# AHU_BUILDER

Revit 2024 add-in that builds the HUMAIN AHU family geometry using dimensions extracted from the supplied vendor DXF.

## Target
- Autodesk Revit 2024
- Revit API 2024
- .NET Framework 4.8
- x64

## DXF-derived dimensions
- Overall length: 5036 mm
- Overall width: 1780 mm
- Overall height: 1720 mm
- Base height: 100 mm
- Section 1 length: 2195 mm
- Gap: 60 mm
- Section 2 length: 2781 mm

## Build
Open `AHU_BUILDER.csproj` in Visual Studio with Revit 2024 installed and build Release x64.

Expected API references:
- `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll`
- `C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll`

## Install
1. Copy `AHU_BUILDER.dll` to `C:\RevitAddins\AHU_BUILDER\`.
2. Copy `AHU_BUILDER.addin` to `%AppData%\Autodesk\Revit\Addins\2024\`.
3. Open a Mechanical Equipment family in Revit Family Editor.
4. Run **Build AHU Family** from External Tools.

The command creates native coordination solids, family parameters, reference planes, and attempts to import the original DXF at its supplied path as a 2D family reference.
