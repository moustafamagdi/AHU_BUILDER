using System;
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

        // Dimensions extracted from the supplied AHU DXF.
        private const double OverallLengthMm = 5036.0;
        private const double OverallWidthMm = 1780.0;
        private const double OverallHeightMm = 1720.0;
        private const double BaseHeightMm = 100.0;
        private const double Section1LengthMm = 2195.0;
        private const double GapMm = 60.0;
        private const double Section2LengthMm = 2781.0;

        private static readonly string DefaultDxfPath =
            @"C:\Users\m.abdellatif\Desktop\AC\_-P-231\_HUMAIN\_AI\_DC101\_50\_MW\Humain DC-Critical Building\_R1\_COPY\_AHU-CIR-M-1C, AHU-CIR-1D\_2860271_.DXF";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc?.Document;

            if (doc == null)
            {
                message = "No active Revit document.";
                return Result.Failed;
            }

            if (!doc.IsFamilyDocument)
            {
                TaskDialog.Show("AHU Builder", "Open a Mechanical Equipment family in the Family Editor, then run the command again.");
                return Result.Cancelled;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Build AHU Family"))
                {
                    tx.Start();

                    FamilyManager fm = doc.FamilyManager;
                    EnsureFamilyParameters(fm);

                    View planView = FindPlanView(doc);
                    SketchPlane planSketchPlane = CreateOrGetPlanSketchPlane(doc, planView);

                    // Main casing
                    CreateBoxDirectShape(doc,
                        xMm: 0,
                        yMm: 0,
                        zMm: BaseHeightMm,
                        lengthMm: OverallLengthMm,
                        widthMm: OverallWidthMm,
                        heightMm: OverallHeightMm,
                        name: "AHU Main Casing");

                    // Base/plinth
                    CreateBoxDirectShape(doc,
                        xMm: 0,
                        yMm: 0,
                        zMm: 0,
                        lengthMm: OverallLengthMm,
                        widthMm: OverallWidthMm,
                        heightMm: BaseHeightMm,
                        name: "AHU Base");

                    // Section marker solids for easier coordination/model reading.
                    CreateBoxDirectShape(doc,
                        xMm: Section1LengthMm,
                        yMm: 0,
                        zMm: BaseHeightMm,
                        lengthMm: GapMm,
                        widthMm: OverallWidthMm,
                        heightMm: OverallHeightMm,
                        name: "AHU Section Gap");

                    CreateReferencePlanes(doc, planView);

                    TryImportDxf(doc, planView, DefaultDxfPath);

                    tx.Commit();
                }

                TaskDialog.Show(
                    "AHU Builder",
                    "AHU family geometry created for Revit 2024.\n\n" +
                    $"Overall: {OverallLengthMm:0} x {OverallWidthMm:0} x {OverallHeightMm:0} mm\n" +
                    $"Base: {BaseHeightMm:0} mm\n" +
                    $"Sections: {Section1LengthMm:0} + {GapMm:0} + {Section2LengthMm:0} mm");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("AHU Builder - Error", ex.Message);
                return Result.Failed;
            }
        }

        private static double Ft(double mm) => mm / MmPerFoot;

        private static void EnsureFamilyParameters(FamilyManager fm)
        {
            EnsureParameter(fm, "AHU_Length", SpecTypeId.Length, GroupTypeId.Geometry, OverallLengthMm);
            EnsureParameter(fm, "AHU_Width", SpecTypeId.Length, GroupTypeId.Geometry, OverallWidthMm);
            EnsureParameter(fm, "AHU_Height", SpecTypeId.Length, GroupTypeId.Geometry, OverallHeightMm);
            EnsureParameter(fm, "Base_Height", SpecTypeId.Length, GroupTypeId.Geometry, BaseHeightMm);
            EnsureParameter(fm, "Section_1_Length", SpecTypeId.Length, GroupTypeId.Geometry, Section1LengthMm);
            EnsureParameter(fm, "Section_Gap", SpecTypeId.Length, GroupTypeId.Geometry, GapMm);
            EnsureParameter(fm, "Section_2_Length", SpecTypeId.Length, GroupTypeId.Geometry, Section2LengthMm);
        }

        private static void EnsureParameter(
            FamilyManager fm,
            string name,
            ForgeTypeId spec,
            ForgeTypeId group,
            double defaultMm)
        {
            FamilyParameter p = fm.Parameters.Cast<FamilyParameter>()
                .FirstOrDefault(x => string.Equals(x.Definition.Name, name, StringComparison.OrdinalIgnoreCase));

            if (p == null)
                p = fm.AddParameter(name, group, spec, false);

            if (fm.CurrentType != null)
                fm.Set(p, Ft(defaultMm));
        }

        private static View FindPlanView(Document doc)
        {
            View view = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

            return view ?? doc.ActiveView;
        }

        private static SketchPlane CreateOrGetPlanSketchPlane(Document doc, View view)
        {
            if (view?.SketchPlane != null)
                return view.SketchPlane;

            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
            return SketchPlane.Create(doc, plane);
        }

        private static void CreateBoxDirectShape(
            Document doc,
            double xMm,
            double yMm,
            double zMm,
            double lengthMm,
            double widthMm,
            double heightMm,
            string name)
        {
            XYZ origin = new XYZ(Ft(xMm), Ft(yMm), Ft(zMm));
            XYZ p1 = origin;
            XYZ p2 = origin + new XYZ(Ft(lengthMm), 0, 0);
            XYZ p3 = origin + new XYZ(Ft(lengthMm), Ft(widthMm), 0);
            XYZ p4 = origin + new XYZ(0, Ft(widthMm), 0);

            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new[] { loop },
                XYZ.BasisZ,
                Ft(heightMm));

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_MechanicalEquipment));
            ds.Name = name;
            ds.SetShape(new GeometryObject[] { solid });
        }

        private static void CreateReferencePlanes(Document doc, View view)
        {
            double l = Ft(OverallLengthMm);
            double w = Ft(OverallWidthMm);
            double s1 = Ft(Section1LengthMm);
            double s2 = Ft(Section1LengthMm + GapMm);

            CreateReferencePlane(doc, view, "AHU_Left", new XYZ(0, -1, 0), new XYZ(0, w + 1, 0));
            CreateReferencePlane(doc, view, "AHU_Right", new XYZ(l, -1, 0), new XYZ(l, w + 1, 0));
            CreateReferencePlane(doc, view, "AHU_Bottom", new XYZ(-1, 0, 0), new XYZ(l + 1, 0, 0));
            CreateReferencePlane(doc, view, "AHU_Top", new XYZ(-1, w, 0), new XYZ(l + 1, w, 0));
            CreateReferencePlane(doc, view, "Section_1_End", new XYZ(s1, -1, 0), new XYZ(s1, w + 1, 0));
            CreateReferencePlane(doc, view, "Section_2_Start", new XYZ(s2, -1, 0), new XYZ(s2, w + 1, 0));
        }

        private static void CreateReferencePlane(Document doc, View view, string name, XYZ bubbleEnd, XYZ freeEnd)
        {
            ReferencePlane existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return;

            ReferencePlane rp = doc.FamilyCreate.NewReferencePlane(bubbleEnd, freeEnd, XYZ.BasisZ, view);
            rp.Name = name;
        }

        private static void TryImportDxf(Document doc, View view, string path)
        {
            if (view == null || !File.Exists(path))
                return;

            DWGImportOptions options = new DWGImportOptions
            {
                Placement = ImportPlacement.Origin,
                OrientToView = true,
                ThisViewOnly = true,
                Unit = ImportUnit.Millimeter
            };

            ElementId importedId;
            doc.Import(path, options, view, out importedId);
        }
    }
}
