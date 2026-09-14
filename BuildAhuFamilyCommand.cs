using System;
using System.Collections.Generic;
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

        // Main dimensions taken from the supplied vendor DXF.
        private const double L = 5036.0;
        private const double W = 1780.0;
        private const double H = 1720.0;
        private const double BaseH = 100.0;

        private const double Frame = 45.0;
        private const double Skin = 28.0;
        private const double DoorGap = 8.0;

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
                using (Transaction tx = new Transaction(doc, "Build AHU Exterior"))
                {
                    tx.Start();

                    EnsureFamilyParameters(doc.FamilyManager);
                    BuildContinuousCabinet(doc);
                    BuildBaseAndPerimeterFrame(doc);
                    BuildRoofPanels(doc);
                    BuildFrontServicePanels(doc);
                    BuildRearPanels(doc);
                    BuildEndPanels(doc);
                    BuildModuleJoints(doc);
                    BuildInletLouver(doc);
                    BuildTopWeatherHood(doc);

                    tx.Commit();
                }

                TaskDialog.Show(
                    "AHU Builder",
                    "Exterior-focused AHU family generated successfully.\n\n" +
                    "The model now prioritizes a continuous realistic casing, service doors, frames, hinges, handles, roof panels, module joints, base frame and external inlet/top accessories.\n" +
                    "Internal fan/filter/coil details are intentionally omitted to keep the family clean and coordinated.");

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
            SetLength(fm, "Inter_Cabinet_Joint", 60.0);
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

        // A fully continuous core guarantees that the AHU never shows unrealistic open gaps.
        private static void BuildContinuousCabinet(Document doc)
        {
            AddBox(doc, 0, 0, BaseH, L, W, H - BaseH, "AHU Continuous Cabinet Core");
        }

        private static void BuildBaseAndPerimeterFrame(Document doc)
        {
            // Continuous structural plinth.
            AddBox(doc, 0, 0, 0, L, W, BaseH, "AHU Base Plinth");

            // Raised front/rear base rails.
            AddBox(doc, 0, -18, BaseH - 18, L, 36, 80, "Front Base Rail");
            AddBox(doc, 0, W - 18, BaseH - 18, L, 36, 80, "Rear Base Rail");

            // Top perimeter rails.
            AddBox(doc, 0, -10, H - Frame, L, Frame, Frame, "Front Top Rail");
            AddBox(doc, 0, W - Frame + 10, H - Frame, L, Frame, Frame, "Rear Top Rail");

            // Corner posts.
            AddVerticalPost(doc, 0, 0, "Front Left Corner");
            AddVerticalPost(doc, 0, W - Frame, "Rear Left Corner");
            AddVerticalPost(doc, L - Frame, 0, "Front Right Corner");
            AddVerticalPost(doc, L - Frame, W - Frame, "Rear Right Corner");
        }

        private static void AddVerticalPost(Document doc, double x, double y, string name)
        {
            AddBox(doc, x, y, BaseH, Frame, Frame, H - BaseH, name);
        }

        private static void BuildRoofPanels(Document doc)
        {
            // Roof is broken into logical removable sections while remaining continuous.
            double[] xBreaks = { 0, 930, 1680, 2195, 2255, 3160, 4040, L };
            for (int i = 0; i < xBreaks.Length - 1; i++)
            {
                double x0 = xBreaks[i];
                double x1 = xBreaks[i + 1];
                AddBox(doc, x0 + 4, 4, H, x1 - x0 - 8, W - 8, Skin, "Roof Panel " + (i + 1));
            }

            // Subtle seam strips between roof panels.
            for (int i = 1; i < xBreaks.Length - 1; i++)
                AddBox(doc, xBreaks[i] - 5, 0, H + Skin, 10, W, 8, "Roof Seam " + i);
        }

        private static void BuildFrontServicePanels(Document doc)
        {
            // Door/panel extents are arranged from the vendor drawing so the facade reads as one coherent AHU.
            var doors = new[]
            {
                new DoorSpec(130, 780, "Filter Access Door"),
                new DoorSpec(930, 650, "Coil Access Door"),
                new DoorSpec(1610, 500, "Electrical Section Door"),
                new DoorSpec(2325, 720, "Service Door 1"),
                new DoorSpec(3090, 650, "Service Door 2"),
                new DoorSpec(3785, 610, "Fan Access Door 1"),
                new DoorSpec(4425, 480, "Fan Access Door 2")
            };

            foreach (DoorSpec d in doors)
                AddServiceDoor(doc, d);

            // Fill every remaining facade zone with fixed panels so there are no unexplained openings.
            AddFixedFrontPanel(doc, 45, 130, "Front Fixed Panel A");
            AddFixedFrontPanel(doc, 910, 930, "Front Fixed Panel B");
            AddFixedFrontPanel(doc, 1580, 1610, "Front Fixed Panel C");
            AddFixedFrontPanel(doc, 2110, 2195, "Front Fixed Panel D");
            AddFixedFrontPanel(doc, 2255, 2325, "Front Fixed Panel E");
            AddFixedFrontPanel(doc, 3045, 3090, "Front Fixed Panel F");
            AddFixedFrontPanel(doc, 3740, 3785, "Front Fixed Panel G");
            AddFixedFrontPanel(doc, 4395, 4425, "Front Fixed Panel H");
            AddFixedFrontPanel(doc, 4905, L - 45, "Front Fixed Panel I");
        }

        private static void AddServiceDoor(Document doc, DoorSpec d)
        {
            const double y = -Skin;
            const double z = 185.0;
            const double height = 1435.0;

            double width = d.Width - 2 * DoorGap;
            double x = d.X + DoorGap;

            // Door leaf.
            AddBox(doc, x, y, z, width, Skin, height, d.Name);

            // Door perimeter extrusion gives the leaf a realistic framed appearance.
            AddBox(doc, x, y - 6, z, width, 8, 26, d.Name + " Bottom Edge");
            AddBox(doc, x, y - 6, z + height - 26, width, 8, 26, d.Name + " Top Edge");
            AddBox(doc, x, y - 6, z, 24, 8, height, d.Name + " Left Edge");
            AddBox(doc, x + width - 24, y - 6, z, 24, 8, height, d.Name + " Right Edge");

            // Three hinges on the left edge.
            double[] hingeZ = { z + 150, z + height / 2.0 - 45, z + height - 240 };
            foreach (double hz in hingeZ)
                AddBox(doc, x - 18, y - 28, hz, 38, 22, 90, d.Name + " Hinge");

            // Vertical pull handle on the opposite side.
            AddBox(doc, x + width - 95, y - 58, z + height * 0.43, 26, 32, 220, d.Name + " Handle");
            AddBox(doc, x + width - 108, y - 65, z + height * 0.43 + 20, 12, 18, 180, d.Name + " Handle Return");
        }

        private static void AddFixedFrontPanel(Document doc, double x0, double x1, string name)
        {
            if (x1 <= x0) return;
            AddBox(doc, x0, -Skin + 2, 185, x1 - x0, Skin - 4, 1435, name);
        }

        private static void BuildRearPanels(Document doc)
        {
            // Rear elevation uses clean removable panels rather than exposing internals.
            double[] xBreaks = { 45, 930, 1680, 2195, 2255, 3160, 4040, L - 45 };
            for (int i = 0; i < xBreaks.Length - 1; i++)
            {
                double x0 = xBreaks[i];
                double x1 = xBreaks[i + 1];
                AddBox(doc, x0 + 3, W, 185, x1 - x0 - 6, Skin, 1435, "Rear Panel " + (i + 1));
            }
        }

        private static void BuildEndPanels(Document doc)
        {
            // Left and right cabinet ends are fully closed around the external air connection accessories.
            AddBox(doc, -Skin, 45, 150, Skin, W - 90, H - 210, "Left End Panel");
            AddBox(doc, L, 45, 150, Skin, W - 90, H - 210, "Right End Panel");
        }

        private static void BuildModuleJoints(Document doc)
        {
            // Main vendor module joint from the drawing: Cabinet 1 = 2195, joint = 60, Cabinet 2 starts at 2255.
            AddJointFrame(doc, 2195, "Cabinet Joint Left");
            AddJointFrame(doc, 2255, "Cabinet Joint Right");

            // Additional vertical frame lines follow facade panelization.
            double[] xs = { 910, 1580, 2110, 3045, 3740, 4395 };
            foreach (double x in xs)
                AddJointFrame(doc, x, "Panel Frame @ " + x.ToString("0") + " mm");
        }

        private static void AddJointFrame(Document doc, double x, string name)
        {
            AddBox(doc, x - 14, -32, BaseH + 20, 28, 38, H - BaseH - 40, name + " Front");
            AddBox(doc, x - 14, W - 6, BaseH + 20, 28, 38, H - BaseH - 40, name + " Rear");
            AddBox(doc, x - 14, 0, H - 25, 28, W, 25, name + " Top");
        }

        private static void BuildInletLouver(Document doc)
        {
            // External inlet box derived from the left-side DXF damper position, but modeled as a realistic weather louver.
            const double ext = 180.0;
            const double y0 = 180.0;
            const double widthY = 1420.0;
            const double z0 = 650.0;
            const double height = 610.0;

            // Hood/frame around the louver.
            AddBox(doc, -ext, y0, z0, ext, widthY, 45, "Inlet Louver Bottom");
            AddBox(doc, -ext, y0, z0 + height - 45, ext, widthY, 45, "Inlet Louver Top");
            AddBox(doc, -ext, y0, z0, ext, 45, height, "Inlet Louver Side A");
            AddBox(doc, -ext, y0 + widthY - 45, z0, ext, 45, height, "Inlet Louver Side B");

            // Horizontal blades.
            for (int i = 0; i < 6; i++)
            {
                double z = z0 + 85 + i * 78;
                AddBox(doc, -ext - 18, y0 + 50, z, ext + 18, widthY - 100, 22, "Inlet Louver Blade " + (i + 1));
            }
        }

        private static void BuildTopWeatherHood(Document doc)
        {
            // Top external connection from the vendor DXF, represented as a closed curb/hood.
            const double x = 90.0;
            const double y = 590.0;
            const double lx = 410.0;
            const double ly = 630.0;
            const double hz = 150.0;

            AddBox(doc, x, y, H + Skin, lx, ly, 35, "Top Hood Base");
            AddBox(doc, x + 18, y + 18, H + Skin + 35, lx - 36, 32, hz, "Top Hood Side 1");
            AddBox(doc, x + 18, y + ly - 50, H + Skin + 35, lx - 36, 32, hz, "Top Hood Side 2");
            AddBox(doc, x + 18, y + 18, H + Skin + 35, 32, ly - 36, hz, "Top Hood End 1");
            AddBox(doc, x + lx - 50, y + 18, H + Skin + 35, 32, ly - 36, hz, "Top Hood End 2");
            AddBox(doc, x - 8, y - 8, H + Skin + 35 + hz, lx + 16, ly + 16, 30, "Top Hood Cap");
        }

        private static void AddBox(
            Document doc,
            double xMm,
            double yMm,
            double zMm,
            double lengthMm,
            double widthMm,
            double heightMm,
            string name)
        {
            if (lengthMm <= 0 || widthMm <= 0 || heightMm <= 0) return;

            XYZ p1 = new XYZ(Ft(xMm), Ft(yMm), Ft(zMm));
            XYZ p2 = new XYZ(Ft(xMm + lengthMm), Ft(yMm), Ft(zMm));
            XYZ p3 = new XYZ(Ft(xMm + lengthMm), Ft(yMm + widthMm), Ft(zMm));
            XYZ p4 = new XYZ(Ft(xMm), Ft(yMm + widthMm), Ft(zMm));

            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                Ft(heightMm));

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_MechanicalEquipment));
            ds.Name = name;
            ds.SetShape(new List<GeometryObject> { solid });
        }

        private sealed class DoorSpec
        {
            public DoorSpec(double x, double width, string name)
            {
                X = x;
                Width = width;
                Name = name;
            }

            public double X { get; }
            public double Width { get; }
            public string Name { get; }
        }
    }
}
