using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using View = Autodesk.Revit.DB.View;
using Autodesk.Revit.ApplicationServices;


namespace Purge_Rooms_UI
{
    [Transaction(TransactionMode.Manual)]
    public class IssueModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Application app = uiapp.Application;
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                IssueModelView window = new IssueModelView(uidoc);
                window.issuedByBox.Text = GetUserInitials(app);
                window.approvedByBox.Text = GetProjectLeadInitials(doc);
                if (CheckForCoordViews(doc) == true || CheckForCoordSheets(doc) == true)
                {
                    window.chkCoordViews.IsChecked = true;
                }
                if (CheckForLibraryPhase(doc) == true)
                {
                    window.chkLibraryPhase.IsChecked = true;
                }
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception e)
            {
                message = e.Message;
                return Result.Failed;
            }
        }
        public static string SyncCloudModel(Document doc)
        {
            TransactWithCentralOptions tOpt = new TransactWithCentralOptions();
            RelinquishOptions rOpt = new RelinquishOptions(true);
            rOpt.StandardWorksets = true;
            rOpt.ViewWorksets = true;
            rOpt.FamilyWorksets = true;
            rOpt.UserWorksets = true;
            rOpt.CheckedOutElements = true;
            SynchronizeWithCentralOptions syncOpt = new SynchronizeWithCentralOptions();
            syncOpt.SetRelinquishOptions(rOpt);
            syncOpt.Compact = false;
            syncOpt.SaveLocalBefore = true;
            syncOpt.SaveLocalAfter = true;
            doc.SynchronizeWithCentral(tOpt, syncOpt);

            string result = "Document Synced";
            return result;
        }
        public static string SaveIssueModel(Document doc)
        {
            var prjInfo = new FilteredElementCollector(doc).OfClass(typeof(ProjectInfo)).Cast<ProjectInfo>().FirstOrDefault();
            string prjDir = prjInfo.LookupParameter("Project Directory").AsString();
            DateTime today = DateTime.Today;
            string date = today.ToString("yyMMdd");
            string dirPath = $"{prjDir}\\01 WIP - Internal Work\\{date}";
            string fileName = doc.Title.Split('_')[0];
            string filePath = dirPath + "\\" + fileName + ".rvt";

            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            WorksharingSaveAsOptions wsOpt = new WorksharingSaveAsOptions();

            SaveAsOptions sOpt = new SaveAsOptions();
            sOpt.SetWorksharingOptions(wsOpt);
            sOpt.OverwriteExistingFile = true;
            sOpt.MaximumBackups = 1;
            sOpt.Compact = false;
            doc.SaveAs(filePath, sOpt);

            TransactWithCentralOptions tOpt = new TransactWithCentralOptions();
            RelinquishOptions rOpt = new RelinquishOptions(true);
            rOpt.StandardWorksets = true;
            rOpt.ViewWorksets = true;
            rOpt.FamilyWorksets = true;
            rOpt.UserWorksets = true;
            rOpt.CheckedOutElements = true;
            SynchronizeWithCentralOptions syncOpt = new SynchronizeWithCentralOptions();
            syncOpt.SetRelinquishOptions(rOpt);
            syncOpt.Compact = false;
            syncOpt.SaveLocalBefore = false;
            syncOpt.SaveLocalAfter = false;
            doc.SynchronizeWithCentral(tOpt, syncOpt);

            string result = "Document Saved";
            return result;
        }
        public static string RemoveRVTLins(Document doc)
        {
            var linkedRVT = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).ToList();
            foreach (RevitLinkType link in linkedRVT)
            {
                try
                {
                    doc.Delete(link.Id);
                }
                catch { }
            }
            string result = "RVT links removed.";
            return result;
        }
        public static string RemoveCADLins(Document doc)
        {
            var linkedCAD = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).ToList();
            foreach (ImportInstance link in linkedCAD)
            {
                try
                {
                    doc.Delete(link.Id);
                }
                catch { }
            }
            string result = "CAD links removed.";
            return result;
        }
        public static string RemovePDFLins(Document doc)
        {
            var linkedPDF = new FilteredElementCollector(doc).OfClass(typeof(ImageType)).ToList();
            foreach (ImageType link in linkedPDF)
            {
                try
                {
                    doc.Delete(link.Id);
                }
                catch { }
            }
            string result = "PDF & Image links removed.";
            return result;
        }
        public static List<ElementId> DeleteAllSheets(Document doc)
        {
            List<ElementId> sheetIdsToDelete = new List<ElementId>();
            List<ViewSheet> allSheets = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Sheets).Cast<ViewSheet>().Where(s => !s.SheetNumber.Contains("Project Information")).ToList();
            foreach (ViewSheet sheet in allSheets)
            {
                sheetIdsToDelete.Add(sheet.Id);
            }
            return sheetIdsToDelete;
        }
        public static List<ElementId> DeleteAllViews(Document doc)
        {
            List<ElementId> viewIdsToDelete = new List<ElementId>();
            List<View> allViews = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Views).WhereElementIsNotElementType().Cast<View>().ToList();
            foreach (View view in allViews)
            {
                if (view.Name == "IFC Export" || view.Name == "Naviswotks")
                {
                    continue;
                }
                else
                {
                    viewIdsToDelete.Add(view.Id);
                }
            }
            var allShedules = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Schedules).WhereElementIsNotElementType().ToList();
            foreach (var sch in allShedules)
            {
                viewIdsToDelete.Add(sch.Id);
            }
            return viewIdsToDelete;
        }
        public static List<ElementId> DeleteNonCoordSheets(Document doc)
        {
            var allSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.SheetNumber.Contains("Project Information")).ToList();
            List<ElementId> sheetIdsToDelete = new List<ElementId>();
            foreach (ViewSheet sheet in allSheets)
            {
                if (!sheet.LookupParameter("Sheet Phase").AsString().Contains("COORD"))
                {
                    sheetIdsToDelete.Add(sheet.Id);
                }
            }
            return sheetIdsToDelete;
        }
        private bool CheckForCoordSheets(Document doc)
        {
            var allSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.SheetNumber.Contains("Project Information")).ToList();
            bool result = false;
            foreach (ViewSheet sheet in allSheets)
            {
                if (sheet.LookupParameter("Sheet Phase").AsString().Contains("COORD"))
                {
                    result = true;
                    break;
                }
            }
            return result;
        }
        private bool CheckForCoordViews(Document doc)
        {
            List<View> allViews = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Views).WhereElementIsNotElementType().
                Where(v => v.Name != "IFC Export").Where(v => v.Name != "Navisworks").
                Cast<View>().ToList();
            List<View> viewsToProcess = new List<View>();
            bool result = false;
            foreach (View view in allViews)
            {
                if (!view.LookupParameter("View Folder 1 (View type)").HasValue)
                {
                    continue;
                }
                else
                {
                    if (view.LookupParameter("View Folder 1 (View type)").AsString().Contains("COORD"))
                    {
                        result = true;
                        break;
                    }
                }
                List<View> allSchedules = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Schedules).WhereElementIsNotElementType().Cast<View>().ToList();
                foreach (View sch in allSchedules)
                {
                    if (!sch.LookupParameter("View Folder 1 (View type)").HasValue)
                    {
                        continue;
                    }
                    else
                    {
                        if (sch.LookupParameter("View Folder 1 (View type)").AsString().Contains("COORD"))
                        {
                            result = true;
                            break;
                        }
                    }
                }
            }
            return result;
        }
        public static List<ElementId> DeleteNonCoordViews(Document doc)
        {
            List<View> allViews = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Views).WhereElementIsNotElementType().
                Where(v => v.Name != "IFC Export").Where(v => v.Name != "Navisworks").
                Cast<View>().ToList();
            List<View> viewsToProcess = new List<View>();
            List<ElementId> viewIdsToDelete = new List<ElementId>();
            foreach (View view in allViews)
            {
                if (!view.LookupParameter("View Folder 1 (View type)").HasValue)
                {
                    viewIdsToDelete.Add(view.Id);
                }
                else
                {
                    if (!view.LookupParameter("View Folder 1 (View type)").AsString().Contains("COORD"))
                    {
                        viewIdsToDelete.Add(view.Id);
                    }
                }
                List<View> allSchedules = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Schedules).WhereElementIsNotElementType().Cast<View>().ToList();
                foreach (View sch in allSchedules)
                {
                    if (!sch.LookupParameter("View Folder 1 (View type)").HasValue)
                    {
                        viewIdsToDelete.Add(sch.Id);
                    }
                    else
                    {
                        if (!sch.LookupParameter("View Folder 1 (View type)").AsString().Contains("COORD"))
                        {
                            viewIdsToDelete.Add(sch.Id);
                        }
                    }
                }
            }    
            return viewIdsToDelete;
        }
        public static string UngroupAllGroups(Document doc)
        {
            var allGroups = new FilteredElementCollector(doc).OfClass(typeof(Group)).Cast<Group>().ToList();
            foreach (Group group in allGroups)
            {
                try
                {
                    group.UngroupMembers();
                }
                catch { }
            }

            string result = "All groups ungrouped.";
            return result;
        }
        public static bool CheckForLibraryPhase(Document doc)
        {
            bool result = false;
            var allPhases = new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().ToList();
            foreach (Phase ph in allPhases)
            {
                if (ph.Name.Contains("Library"))
                {
                    result = true;
                    break;
                }
            }
            return result;
        }
        public static string DeleteLibraryElements(Document doc)
        {
            ElementId libPhaseId = null;
            ElementId exPhaseId = null;
            var allPhases = new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().ToList();
            foreach (Phase ph in allPhases)
            {
                if (ph.Name.Contains("Library"))
                {
                    libPhaseId = ph.Id;
                }
                else if (ph.Name.Contains("Existing"))
                {
                    exPhaseId = ph.Id;
                }
            }
            if (libPhaseId != null)
            {
                var allElements = new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e => e.get_Parameter(BuiltInParameter.PHASE_CREATED) != null).Where(e => e.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED) != null).ToList();
                List<ElementId> libIds = new List<ElementId>();
                List<ElementId> libIdsToDelete = new List<ElementId>();
                foreach (var el in allElements)
                {
                    if (el.get_Parameter(BuiltInParameter.PHASE_CREATED).AsValueString().Contains("Library") && el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED).AsValueString().Contains("Existing"))
                    {
                        libIdsToDelete.Add(el.Id);
                    }
                }
                foreach (ElementId id in libIdsToDelete)
                {
                    try
                    {
                        doc.Delete(id);
                    }
                    catch { }
                }
            }

            string result = "All library elements were deleted.";
            return result;
        }
        public static string ExportIFC(Document doc)
        {
            var prjInfo = new FilteredElementCollector(doc).OfClass(typeof(ProjectInfo)).Cast<ProjectInfo>().FirstOrDefault();
            string prjDir = prjInfo.LookupParameter("Project Directory").AsString();
            DateTime today = DateTime.Today;
            string date = today.ToString("yyMMdd");
            string dirPath = $"{prjDir}\\01 WIP - Internal Work\\{date}";
            string fileName = doc.Title.Split('_')[0];

            var IFCview = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.View)).Cast<Autodesk.Revit.DB.View>().Where(v => v.Name == "IFC Export").ToList()[0];
            IFCExportOptions ifcOpt = new IFCExportOptions();
            ifcOpt.FileVersion = IFCVersion.IFC2x3;
            ifcOpt.WallAndColumnSplitting = false;
            ifcOpt.ExportBaseQuantities = false;
            ifcOpt.FilterViewId = IFCview.Id;

            ifcOpt.AddOption("ExportInternalRevitPropertySets", "false");
            ifcOpt.AddOption("ExportIFCCommonPropertySets", "true");
            ifcOpt.AddOption("ExportAnnotations ", "true");
            ifcOpt.AddOption("SpaceBoundaries ", "0");
            ifcOpt.AddOption("ExportRoomsInView", "true");
            ifcOpt.AddOption("Use2DRoomBoundaryForVolume ", "true");
            ifcOpt.AddOption("UseFamilyAndTypeNameForReference ", "true");
            ifcOpt.AddOption("Export2DElements", "false");
            ifcOpt.AddOption("ExportPartsAsBuildingElements", "false");
            ifcOpt.AddOption("ExportBoundingBox", "false");
            ifcOpt.AddOption("ExportSolidModelRep", "true");
            ifcOpt.AddOption("ExportSchedulesAsPsets", "false");
            ifcOpt.AddOption("ExportSpecificSchedules", "false");
            ifcOpt.AddOption("ExportLinkedFiles", "false");
            ifcOpt.AddOption("IncludeSiteElevation", "true");
            ifcOpt.AddOption("StoreIFCGUID", "true");
            ifcOpt.AddOption("VisibleElementsOfCurrentView ", "true");
            ifcOpt.AddOption("UseActiveViewGeometry", "true");
            ifcOpt.AddOption("TessellationLevelOfDetail", "0,5");
            ifcOpt.AddOption("ExportUserDefinedPsets", "false");
            ifcOpt.AddOption("SitePlacement", "0");
            doc.Export(dirPath, fileName, ifcOpt);

            string result = "IFC file was exported.";
            return result;
        }
        public static string ExportNWC(Document doc)
        {
            var prjInfo = new FilteredElementCollector(doc).OfClass(typeof(ProjectInfo)).Cast<ProjectInfo>().FirstOrDefault();
            string prjDir = prjInfo.LookupParameter("Project Directory").AsString();
            DateTime today = DateTime.Today;
            string date = today.ToString("yyMMdd");
            string dirPath = $"{prjDir}\\01 WIP - Internal Work\\{date}";
            string fileName = doc.Title.Split('_')[0];

            try
            {
                var NWCview = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.View)).Cast<Autodesk.Revit.DB.View>().FirstOrDefault(v => v.Name == "Navisworks");
                NavisworksExportOptions nwcOpt = new NavisworksExportOptions();
                nwcOpt.ExportScope = NavisworksExportScope.View;
                nwcOpt.ViewId = NWCview.Id;
                nwcOpt.ExportLinks = false;
                nwcOpt.Coordinates = 0;
                nwcOpt.ExportElementIds = true;
                nwcOpt.ConvertElementProperties = true;
                nwcOpt.ExportRoomAsAttribute = true;
                nwcOpt.ExportRoomGeometry = false;

                doc.Export(dirPath, fileName, nwcOpt);
            }
            catch{ }

            string result = "NWC file was exported.";
            return result;
        }
        public static void PurgeModel(Document doc)
        {
            PerformanceAdviser perfAd = PerformanceAdviser.GetPerformanceAdviser();
            var allRulesList = perfAd.GetAllRuleIds();
            var rulesToExecute = new List<PerformanceAdviserRuleId>();
            foreach (PerformanceAdviserRuleId r in allRulesList)
            {
                if (perfAd.GetRuleName(r).Equals("Project contains unused families and types"))
                {
                    rulesToExecute.Add(r);
                }
            }
            for (int i = 0; i < 5; i++)
            {
                IList<FailureMessage> failMessages = perfAd.ExecuteRules(doc, rulesToExecute);
                if (failMessages.Count() == 0) return;

                ICollection<ElementId> failingElementIds = failMessages[0].GetFailingElements();
                foreach (ElementId id in failingElementIds)
                {
                    try
                    {
                        doc.Delete(id);
                    }
                    catch { }
                }
            }
        }
        public static string GetProjectLeadInitials(Document doc)
        {
            string plInitials = "";
            ProjectInfo pi = new FilteredElementCollector(doc).OfClass(typeof(ProjectInfo)).Cast<ProjectInfo>().FirstOrDefault();
            if (pi.LookupParameter("ACG_Project Lead").HasValue)
            {
                string plName = pi.LookupParameter("ACG_Project Lead").AsString();
                if (plName.Count() > 0 && plName.Count() <= 3)
                {
                    plInitials = plName;
                }
                else if (plName.Count() > 3)
                {
                    plInitials = string.Concat(plName.Where(char.IsUpper));
                }
            }
            return plInitials;
        }
        public static string GetUserInitials(Application app)
        {
            string userInitials = "";
            try
            {
                if (app.Username.StartsWith("ross.boyter"))
                {
                    userInitials = "RDB";
                }
                else if (app.Username.StartsWith("jp.v"))
                {
                    userInitials = "JPV";
                    
                }
                else
                {

                    char first = app.Username.Split('.')[0][0];
                    char second = app.Username.Split('.')[1][0];
                    userInitials = string.Concat(char.ToUpper(first), char.ToUpper(second));
                }
            }
            catch { }
                       
            return userInitials;
        }
    }
}
