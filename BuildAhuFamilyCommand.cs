using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AHU_BUILDER
{
    [Transaction(TransactionMode.Manual)]
    public class BuildAhuFamilyCommand : IExternalCommand
    {
        private const double MmPerFoot = 304.8;

        private const double L = 5036.0;
        private const double W = 1780.0;
        private const double H = 1720.0;
        private const double BaseH = 100.0;
        private const double Frame = 60.0;

        private static readonly string DefaultDxfPath =
            @"C:\Users\m.abdellatif\Desktop\AC\_-P-231\_HUMAIN\_AI\_DC101\_50\_MW\Humain DC-Critical Building\_R1\_COPY\_AHU-CIR-M-1C, AHU-CIR-1D\_2860271_.DXF";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                message = "No active Revit document.";
                return Result.Failed;
            }

            if (!doc.IsFamilyDocument)
            {
                TaskDialog.Show("AHU Builder", "Open a Mechanical Equipment family in Family Editor, then run AHU Builder.");
                return Result.Cancelled;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Build Detailed AHU"))
                {
                    tx.Start();
                    EnsureFamilyParameters(doc.FamilyManager);
                    BuildCabinetFrame(doc);
                    BuildExternalPanelsAndDoors(doc);
                    BuildInternalBulkheads(doc);
                    BuildInletDampers(doc);
                    BuildFilterBank(doc);
                    BuildCoolingCoil(doc);
                    BuildElectricCoil(doc);
                    BuildDrainPansAndPipeStubs(doc);
                    BuildDehumidifier(doc);
                    BuildFanArray(doc);
                    BuildLights(doc);
                    TryImportDxf(doc, Find3DOrPlanView(doc), DefaultDxfPath);
                    tx.Commit();
                }

                TaskDialog.Show("AHU Builder",
                    "Detailed AHU generated from the supplied DXF.\n\n" +
                    "Included: frame, panels, access doors, dampers, filters, cooling coil, electric coil, drain pans, pipe stubs, dehumidifier, 4-fan array and lights.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("AHU Builder - Error", ex.ToString());
                return Result.Failed;
            }
        }

        private static double Ft(double mm) => mm / MmPerFoot;

        private static void EnsureFamilyParameters(FamilyManager fm)
        {
            SetLength(fm, "AHU_Length", L);
            SetLength(fm, "AHU_Width", W);
            SetLength(fm, "AHU_Height", H);
            SetLength(fm, "Base_Height", BaseH);
            SetLength(fm, "Cabinet_1_Length", 2195.0);
            SetLength(fm, "Inter_Cabinet_Gap", 60.0);
            SetLength(fm, "Cabinet_2_Length", 2781.0);
        }

        private static void SetLength(FamilyManager fm, string name, double valueMm)
        {
            FamilyParameter p = fm.Parameters.Cast<FamilyParameter>()
                .FirstOrDefault(x => string.Equals(x.Definition.Name, name, StringComparison.OrdinalIgnoreCase));

            if (p == null)
                p = fm.AddParameter(name, GroupTypeId.Geometry, SpecTypeId.Length, false);

            if (fm.CurrentType != null)
                fm.Set(p, Ft(valueMm));
        }

        private static void BuildCabinetFrame(Document doc)
        {
            AddBox(doc, 0, 0, 0, L, W, BaseH, "Base Plinth");

            double[] xs = { 0, 2195, 2255, 5036 - Frame };
            foreach (double x in xs)
            {
                AddBox(doc, x, 0, BaseH, Frame, Frame, H - BaseH, "Frame Post");
                AddBox(doc, x, W - Frame, BaseH, Frame, Frame, H - BaseH, "Frame Post");
            }

            AddRailSet(doc, 0, 2195, "Cabinet 1");
            AddRailSet(doc, 2255, 5036, "Cabinet 2");
        }

        private static void AddRailSet(Document doc, double x0, double x1, string prefix)
        {
            double len = x1 - x0;
            AddBox(doc, x0, 0, BaseH, len, Frame, Frame, prefix + " Bottom Front Rail");
            AddBox(doc, x0, W - Frame, BaseH, len, Frame, Frame, prefix + " Bottom Rear Rail");
            AddBox(doc, x0, 0, H - Frame, len, Frame, Frame, prefix + " Top Front Rail");
            AddBox(doc, x0, W - Frame, H - Frame, len, Frame, Frame, prefix + " Top Rear Rail");
        }

        private static void BuildExternalPanelsAndDoors(Document doc)
        {
            const double skin = 22.0;

            // Roof and rear skin split by cabinet so the 60 mm inter-cabinet joint remains visible.
            AddBox(doc, Frame, Frame, H - skin, 2195 - 2 * Frame, W - 2 * Frame, skin, "Cabinet 1 Roof Panel");
            AddBox(doc, 2255 + Frame, Frame, H - skin, 2781 - 2 * Frame, W - 2 * Frame, skin, "Cabinet 2 Roof Panel");
            AddBox(doc, Frame, W - skin, BaseH + Frame, 2195 - 2 * Frame, skin, H - BaseH - 2 * Frame, "Cabinet 1 Rear Panel");
            AddBox(doc, 2255 + Frame, W - skin, BaseH + Frame, 2781 - 2 * Frame, skin, H - BaseH - 2 * Frame, "Cabinet 2 Rear Panel");

            // End panels.
            AddBox(doc, 0, Frame, BaseH + Frame, skin, W - 2 * Frame, H - BaseH - 2 * Frame, "Inlet End Panel");
            AddBox(doc, L - skin, Frame, BaseH + Frame, skin, W - 2 * Frame, H - BaseH - 2 * Frame, "Outlet End Panel");

            // Access doors derived from DXF front-view extents.
            AddFrontDoor(doc, 422.2, 733.8, 162, 1560, "Filter Access Door");
            AddFrontDoor(doc, 1661.9, 300.0, 162, 1560, "Electric Coil Removable Panel");
            AddFrontDoor(doc, 2535.3, 531.7, 162, 1560, "Empty Section Access Door");
            AddFrontDoor(doc, 3531.4, 495.6, 162, 1560, "Fan Service Door 1");
            AddFrontDoor(doc, 4355.8, 691.2, 162, 1560, "Fan Service Door 2");
        }

        private static void AddFrontDoor(Document doc, double x, double widthX, double z, double height, string name)
        {
            const double t = 28.0;
            AddBox(doc, x, -t, z, widthX, t, height, name);
            // Simple vertical handle.
            AddBox(doc, x + widthX - 85, -55, z + height * 0.45, 22, 30, 220, name + " Handle");
        }

        private static void BuildInternalBulkheads(Document doc)
        {
            double[] bulkheads = { 531, 1191, 1686, 3102, 4382 };
            foreach (double x in bulkheads)
                AddBox(doc, x, 62, 162, 12, 1720, 1560, "Internal Bulkhead @ " + x.ToString("0") + " mm");
        }

        private static void BuildInletDampers(Document doc)
        {
            // Left-side inlet damper: exact DXF bbox approximately x=-128..2, y=-6.8..1782, z=687..1197.
            AddBox(doc, -128, 0, 687, 130, W, 510, "Inlet Damper Frame");
            for (int i = 0; i < 7; ++i)
                AddBox(doc, -145, 80 + i * 245, 900, 165, 180, 35, "Inlet Damper Blade " + (i + 1));

            // Top damper from DXF bbox approximately x=62..472, y=593..1227, z=1782..1912.
            AddBox(doc, 62, 593, 1782, 410, 634, 130, "Top Damper Frame");
            for (int i = 0; i < 5; ++i)
                AddBox(doc, 95 + i * 72, 610, 1795, 28, 600, 100, "Top Damper Blade " + (i + 1));
        }

        private static void BuildFilterBank(Document doc)
        {
            // Flat filter face locations taken from the DXF.
            double[] ys = { 171.5, 463.5, 1060.5 };
            double[] zs = { 211.5, 813.5, 1415.5 };

            int n = 1;
            foreach (double y in ys)
            {
                foreach (double z in zs)
                {
                    double filterW = y < 400 ? 287 : 592;
                    double filterH = z > 1400 ? 287 : 592;
                    if (y < 400 && z > 1400) continue; // this cell is absent in vendor DXF.

                    AddBox(doc, 532, y, z, 48, filterW, filterH, "Flat Filter " + n);
                    AddBox(doc, 600, y, z, 510, filterW, filterH, "Bag Filter " + n);
                    n++;
                }
            }
        }

        private static void BuildCoolingCoil(Document doc)
        {
            AddBox(doc, 1192, 199, 212, 300, 1495, 1450, "Cooling Coil");

            // Tube/header details visible enough for coordination without creating thousands of fins.
            for (int i = 0; i < 8; ++i)
                AddBox(doc, 1220 + i * 32, 220, 250, 10, 1450, 1370, "Cooling Coil Fin Pack " + (i + 1));

            AddBox(doc, 1251, 121, 232, 16, 16, 1410, "Cooling Coil Vertical Pipe");
            AddBox(doc, 1251, -104, 1630, 16, 225, 16, "Cooling Coil Top Pipe");
        }

        private static void BuildElectricCoil(Document doc)
        {
            AddBox(doc, 1687, 217, 222, 200, 1410, 1440, "Electric Coil");
            for (int i = 0; i < 7; ++i)
                AddBox(doc, 1700 + i * 25, 250, 250, 8, 1350, 1380, "Electric Coil Bank " + (i + 1));
        }

        private static void BuildDrainPansAndPipeStubs(Document doc)
        {
            AddBox(doc, 1203.5, 82, 32, 437, 1680, 130, "Cooling Coil Drain Pan");
            AddBox(doc, 2613, 82, 32, 420, 1680, 130, "Empty Section 06 Drain Pan");
            AddBox(doc, 3113, 82, 32, 110, 1680, 130, "Dehumidifier Drain Pan");
            AddBox(doc, 3303, 82, 32, 220, 1680, 130, "Empty Section 08 Drain Pan");

            AddBox(doc, 1409.5, -68, 59.5, 25, 150, 25, "Cooling Drain Pipe Stub");
            AddBox(doc, 2810.5, -68, 59.5, 25, 150, 25, "Section 06 Drain Pipe Stub");
            AddBox(doc, 3155.5, -68, 59.5, 25, 150, 25, "Dehumidifier Drain Pipe Stub");
            AddBox(doc, 3400.5, -68, 59.5, 25, 150, 25, "Section 08 Drain Pipe Stub");
        }

        private static void BuildDehumidifier(Document doc)
        {
            // Vendor block is planar in DXF; give it a practical 130 mm thickness for readable 3D coordination.
            AddBox(doc, 3103, 172, 222, 130, 1500, 1440, "Dehumidifier Module");
            for (int i = 0; i < 6; ++i)
                AddBox(doc, 3115 + i * 18, 220, 260, 7, 1400, 1360, "Dehumidifier Fin Pack " + (i + 1));
        }

        private static void BuildFanArray(Document doc)
        {
            // Four EC fans from the DXF bboxes. Octagonal prisms are used for stable Revit geometry.
            AddFan(doc, 4383, 188.7, 234.2, 438.6, 630, 630, "EC Fan 1");
            AddFan(doc, 4383, 1025.3, 234.2, 438.6, 630, 630, "EC Fan 2");
            AddFan(doc, 4383, 188.7, 1017.6, 438.6, 630, 630, "EC Fan 3");
            AddFan(doc, 4383, 1025.3, 1017.6, 438.6, 630, 630, "EC Fan 4");

            // Damper blocks immediately upstream of each fan.
            AddBox(doc, 4253, 212, 280, 130, 562, 540, "Fan Damper 1");
            AddBox(doc, 4253, 1049, 280, 130, 562, 540, "Fan Damper 2");
            AddBox(doc, 4253, 212, 1064, 130, 562, 540, "Fan Damper 3");
            AddBox(doc, 4253, 1049, 1064, 130, 562, 540, "Fan Damper 4");
        }

        private static void BuildLights(Document doc)
        {
            AddBox(doc, 817, 820, 1644, 100, 205, 74, "Filter Section Light");
            AddBox(doc, 2823, 820, 1644, 100, 205, 74, "Empty Section Light");
            AddBox(doc, 4308, 820, 1644, 100, 205, 74, "Fan Section Light");
        }

        private static void AddFan(Document doc, double x, double y, double z, double lengthX, double sizeY, double sizeZ, string name)
        {
            double cy = y + sizeY / 2.0;
            double cz = z + sizeZ / 2.0;
            double r = Math.Min(sizeY, sizeZ) * 0.47;

            List<XYZ> pts = new List<XYZ>();
            const int sides = 16;
            for (int i = 0; i < sides; ++i)
            {
                double a = 2.0 * Math.PI * i / sides;
                pts.Add(new XYZ(Ft(x), Ft(cy + r * Math.Cos(a)), Ft(cz + r * Math.Sin(a))));
            }

            CurveLoop loop = new CurveLoop();
            for (int i = 0; i < sides; ++i)
                loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % sides]));

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, XYZ.BasisX, Ft(lengthX));
            AddSolid(doc, solid, name);

            // Hub proxy.
            AddBox(doc, x + lengthX * 0.25, cy - 90, cz - 90, lengthX * 0.5, 180, 180, name + " Hub");
        }

        private static void AddBox(Document doc, double x, double y, double z, double dx, double dy, double dz, string name)
        {
            if (dx <= 0 || dy <= 0 || dz <= 0) return;

            XYZ p1 = new XYZ(Ft(x), Ft(y), Ft(z));
            XYZ p2 = new XYZ(Ft(x + dx), Ft(y), Ft(z));
            XYZ p3 = new XYZ(Ft(x + dx), Ft(y + dy), Ft(z));
            XYZ p4 = new XYZ(Ft(x), Ft(y + dy), Ft(z));

            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, XYZ.BasisZ, Ft(dz));
            AddSolid(doc, solid, name);
        }

        private static void AddSolid(Document doc, Solid solid, string name)
        {
            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_MechanicalEquipment));
            ds.Name = name;
            ds.SetShape(new GeometryObject[] { solid });
        }

        private static View Find3DOrPlanView(Document doc)
        {
            View v = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(x => !x.IsTemplate);
            if (v != null) return v;

            v = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .FirstOrDefault(x => !x.IsTemplate);
            return v ?? doc.ActiveView;
        }

        private static void TryImportDxf(Document doc, View view, string path)
        {
            if (view == null || !File.Exists(path)) return;

            try
            {
                DWGImportOptions options = new DWGImportOptions
                {
                    Placement = ImportPlacement.Origin,
                    OrientToView = false,
                    ThisViewOnly = false,
                    Unit = ImportUnit.Millimeter
                };

                ElementId importedId;
                doc.Import(path, options, view, out importedId);
            }
            catch
            {
                // Native geometry must still be created even if CAD import is rejected by the current family/view.
            }
        }
    }
}
