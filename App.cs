using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Line = Autodesk.Revit.DB.Line;
using XYZ = Autodesk.Revit.DB.XYZ;
//全接合
namespace CreateColumn
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication a)
        {
            a.CreateRibbonTab("建造");
            RibbonPanel AECPanelDebug = a.CreateRibbonPanel("建造", "建造");
            string path = Assembly.GetExecutingAssembly().Location;
            #region DockableWindow

            PushButtonData AutoJoinGeomatryUtils = new PushButtonData("建立樑柱", "建立樑柱", path, "CreateColumn.AutoJoinGeomatryUtils");

            //PushButtonData deJoinGeomatryUtils = new PushButtonData("deJoinGeomatryUtils", "deJoinGeomatryUtils", path, "CreateColumn.deAutoJoinGeomatryUtils");
            //deJoinGeomatryUtils.LargeImage = GetImage(Resources.red.GetHbitmap());

            RibbonItem ri4 = AECPanelDebug.AddItem(AutoJoinGeomatryUtils);
            //RibbonItem r15 = AECPanelDebug.AddItem(deJoinGeomatryUtils);

            #endregion
            return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }
        //botton image
    }
    [Transaction(TransactionMode.Manual)]
    public class AutoJoinGeomatryUtils : IExternalCommand
    {
        //文字層
        public static List<TextInfo> textinfos = new List<TextInfo>();     //全文字
        public static List<TextInfo> Coltextinf = new List<TextInfo>();    //柱文字
        public static List<TextInfo> Frametextinf = new List<TextInfo>();  //樑文字
        public static List<TextInfo> DictText = new List<TextInfo>();      //尺寸對照表
        //圖層
        public static string[] colsLayer = { };
        public static string[] framesLayer = { };   // S-BEAM  / test
        public static string[] colstextLayer = { };
        public static string[] framestextLayer = { };
        public static string gridTLayer = "";
        public static string gridLLayer = "";

        //文字規則
        public static string pattern = "";
        public static string Dictrule = "";
        //
        public static Dictionary<Autodesk.Revit.DB.PolyLine, TextInfo> sizedict = new Dictionary<Autodesk.Revit.DB.PolyLine, TextInfo>();
        public static string Userimpath = "";
        public static double _tolerance = new double();
        public static double k = new double();  //座標轉換比例 公分(CAD)轉英尺(Revit)
        public static XYZ Vector = new XYZ();
        public static double radian = new double();
        public static Dictionary<string, TextInfo> TextDict = new Dictionary<string, TextInfo>();
        public static Dictionary<Line, string> scatterLinedict = new Dictionary<Line, string>();
        public static Dictionary<Line, string> midlinedict = new Dictionary<Line, string>();
        public static Dictionary<string, Grid> RGridDict = new Dictionary<string, Grid>();
        public static Dictionary<string, Line> CGridDict = new Dictionary<string, Line>();
        public static List<ACadSharp.Entities.Line> CADGridLine = new List<ACadSharp.Entities.Line>();

        private static void Reset()
        {
            textinfos = new List<TextInfo>();//全CAD文字
            Coltextinf = new List<TextInfo>();//柱文字
            Frametextinf = new List<TextInfo>();//樑文字
            DictText = new List<TextInfo>();//尺寸對照表
            //圖層
            colsLayer = new string[] { "COLUMN4" };
            framesLayer = new string[] { "GIRDER", "BEAM" };   // S-BEAM  / test
            colstextLayer = new string[] { "C-TEXT" };
            framestextLayer = new string[] { "b-TEXT", "G-TEXT", "GY-TEXT" };
            gridTLayer = "NOTE3";
            gridLLayer = "CENTER";
            //文字規則
            pattern = @"(\d+)\s*x\s*(\d+)";
            Dictrule = @"([A-Z]+)或([A-Z][A-Z]+)";
            sizedict = new Dictionary<Autodesk.Revit.DB.PolyLine, TextInfo>();
            Userimpath = "";
            _tolerance = 0.001;
            k = 0.032808399;  //座標轉換比例 公分(CAD)轉英尺(Revit)
            Vector = new XYZ();
            radian = 6.28;
            TextDict = new Dictionary<string, TextInfo>();
            scatterLinedict = new Dictionary<Line, string>();
            midlinedict = new Dictionary<Line, string>();
            RGridDict = new Dictionary<string, Grid>();
            CGridDict = new Dictionary<string, Line>();
        }
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Reset();
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Autodesk.Revit.DB.Document doc = uidoc.Document;
            Autodesk.Revit.DB.View activeview = doc.ActiveView;


            Userimpath = GetCADFilePath();
            if (Userimpath == null)
            {
                message += "未選取檔案";
            }
            ReadCad(Userimpath);
            GetVector(doc, activeview);

            DWGImportOptions options = new DWGImportOptions
            {
                Unit = ImportUnit.Centimeter,
                Placement = ImportPlacement.Origin,
                ThisViewOnly = true,
                AutoCorrectAlmostVHLines = true,
                ColorMode = ImportColorMode.Preserved,
                VisibleLayersOnly = true,

            };
            using (Transaction transf = new Transaction(doc, "import CAD"))
            {
                transf.Start();
                try
                {
                    bool result = doc.Import(Userimpath, options, activeview, out ElementId elementId);
                    doc.GetElement(elementId).Pinned = false;
                    ElementTransformUtils.MoveElement(doc, elementId, Vector);
                    transf.Commit();
                }
                catch
                {
                    transf.RollBack();
                }
            }
            using (Transaction trans = new Transaction(doc, "處理現有CAD並創建柱子"))
            {

                trans.Start();
                try
                {
                    // 查找現有的CAD Import
                    FilteredElementCollector collector = new FilteredElementCollector(doc, activeview.Id);
                    var existingImports = collector
                        .OfClass(typeof(ImportInstance))
                        .Cast<ImportInstance>()
                        .Where(imp => imp.IsLinked == false) // 只找Import，不是Link
                        .ToList();
                    message = $"找到 {existingImports.Count} 個CAD Import，開始處理...\n";

                    int processedImports = 0;
                    int createdColumns = 0;
                    List<string> pathlist = new List<string>();
                    //文字圖層名稱
                    Coltextinf = textinfos.Where(t => colstextLayer.Contains(t.LayerName)).ToList();
                    Frametextinf = textinfos.Where(t => framestextLayer.Contains(t.LayerName)).ToList();
                    DictText = textinfos.Where(t => t.LayerName.Contains("NOTE2")).ToList();
                    // 處理每個找到的CAD Import
                    foreach (ImportInstance cadImport in existingImports)
                    {
                        //cadTransform = GetCADTransform(cadImport);
                        pathlist.Add(getpath(cadImport));
                        try
                        {

                            GeometryElement geoElement = cadImport.get_Geometry(new Options());
                            if (geoElement != null)
                            {
                                int columnsFromThisImport = ProcessCADGeometry(doc, geoElement);
                                createdColumns += columnsFromThisImport;
                                processedImports++;
                            }
                        }
                        catch (Exception ex)
                        {
                            message += $"處理CAD Import時發生錯誤：{ex.Message}\n";
                        }
                    }

                    trans.Commit();
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    trans.RollBack();
                    message = "CAD處理失敗: " + ex.Message;
                    return Result.Failed;
                }
            }

        }
        //建立新類型
        private static FamilySymbol CreateType(Document doc, int width, int height, string typename, FamilySymbol copytype)
        {
            // 複製族群符號
            FamilySymbol newSymbol = copytype.Duplicate(typename) as FamilySymbol;
            // 設定尺寸參數
            SetDimensions(newSymbol, width, height);
            if (!newSymbol.IsActive)
                newSymbol.Activate();
            return newSymbol;
        }
        private static void GetVector(Document doc, Autodesk.Revit.DB.View activeview)
        {
            //找revit視圖上的gridline
            FilteredElementCollector gridline = new FilteredElementCollector(doc, activeview.Id).OfClass(typeof(Grid));
            IList<Element> gridlines = gridline.ToElements();
            foreach (Grid grid in gridlines)
            {
                RGridDict.Add(grid.Name, grid);
            }
            //讀CAD的gridline
            List<TextInfo> Cgridnames = textinfos.Where(t => gridTLayer.Equals(t.LayerName)).ToList();
            foreach (TextInfo gridtext in Cgridnames)
            {
                try
                {
                    if (gridtext.Content.Contains("X"))
                    {
                        var gridlineX = CADGridLine.Where(t => Math.Abs(t.StartPoint.X - gridtext.Position.X) < 100).FirstOrDefault();
                        XYZ startPoint = new XYZ(gridlineX.StartPoint.X, gridlineX.StartPoint.Y, doc.ActiveView.GenLevel.Elevation);
                        XYZ endPoint = new XYZ(gridlineX.EndPoint.X, gridlineX.EndPoint.Y, doc.ActiveView.GenLevel.Elevation);
                        CGridDict.Add(gridtext.Content, Line.CreateBound(startPoint, endPoint));
                    }
                    else if (gridtext.Content.Contains("Y"))
                    {
                        var gridlineY = CADGridLine.Where(t => Math.Abs(t.StartPoint.Y - gridtext.Position.Y) < 100).FirstOrDefault();
                        XYZ startPoint = new XYZ(gridlineY.StartPoint.X, gridlineY.StartPoint.Y, doc.ActiveView.GenLevel.Elevation);
                        XYZ endPoint = new XYZ(gridlineY.EndPoint.X, gridlineY.EndPoint.Y, doc.ActiveView.GenLevel.Elevation);
                        CGridDict.Add(gridtext.Content, Line.CreateBound(startPoint, endPoint));
                    }
                }
                catch
                {
                    continue;
                }
            }
            Line Xmin = CGridDict.Where(t => t.Key.Contains("X")).OrderBy(t => t.Value.GetEndPoint(0).X).Select(t => t.Value).FirstOrDefault();
            Line Ymax = CGridDict.Where(t => t.Key.Contains("Y")).OrderBy(t => t.Value.GetEndPoint(0).Y).Select(t => t.Value).LastOrDefault();
            if (Xmin != null && Ymax != null)
            {
                XYZ CADPoint = new XYZ(Xmin.GetEndPoint(0).X, Ymax.GetEndPoint(0).Y, doc.ActiveView.GenLevel.Elevation);
                XYZ RevitPoint = new XYZ(RGridDict[CGridDict.FirstOrDefault(t => t.Value == Xmin).Key].Curve.GetEndPoint(0).X,   //X1.X
                                            RGridDict[CGridDict.FirstOrDefault(t => t.Value == Ymax).Key].Curve.GetEndPoint(0).Y,  //Y1.Y
                                            doc.ActiveView.GenLevel.Elevation);
                Vector = -(CADPoint * k - RevitPoint);
            }
            else
            {
                MessageBox.Show("錯誤", "未找到有效的Grid線，請檢查CAD文件或Revit視圖。");
            }
        }
        //設定新類型尺寸
        private static void SetDimensions(FamilySymbol newSymbol, int width, int height)
        {
            string[] widthParam = { "b", "Width" };
            string[] heightParam = { "h", "Height" };
            foreach (string paraName in widthParam)
            {
                Autodesk.Revit.DB.Parameter param = newSymbol.LookupParameter(paraName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(UnitUtils.ConvertToInternalUnits(width, UnitTypeId.Millimeters));
                    break;
                }
            }
            foreach (string paramName in heightParam)
            {
                Autodesk.Revit.DB.Parameter param = newSymbol.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(UnitUtils.ConvertToInternalUnits(height, UnitTypeId.Millimeters));
                    break;
                }
            }
        }
        private static FamilySymbol FindColumnFamilySymbol(Document doc, TextInfo sizeinfo)
        {
            // 尋找結構柱族群，優先順序：RC_矩形柱 > 任何結構柱
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            var columnSymbols = collector
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .Cast<FamilySymbol>()
                .ToList();

            // 優先找RC_矩形柱
            var rcColumn = columnSymbols.FirstOrDefault(fs => fs.Family.Name.Contains("RC_矩形柱")
                            && fs.Name.Contains(sizeinfo.texWidth / 10 + "x" + sizeinfo.texHeight / 10 + "cm"));
            if (rcColumn != null) return ActivateSymbol(rcColumn);
            else
            {
                var DurcColumn = columnSymbols.FirstOrDefault(fs => fs.Family.Name.Contains("RC"));
                FamilySymbol columnT = CreateType(doc, sizeinfo.texWidth, sizeinfo.texHeight,
                            (sizeinfo.texWidth / 10) + "x" + (sizeinfo.texHeight / 10) + "cm", DurcColumn);
                return ActivateSymbol(columnT);
            }
        }
        private static FamilySymbol FindBeamFamilySymbol(Document doc, TextInfo sizeinfo)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            var BeamSymbols = collector
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilySymbol>()
                .ToList();

            // 優先找RC_矩形樑
            var rcBeam = BeamSymbols.FirstOrDefault(fs => fs.Family.Name.Contains("RC_矩形樑")
                            && fs.Name.Contains(sizeinfo.texWidth / 10 + "x" + sizeinfo.texHeight / 10 + "cm"));
            if (rcBeam != null) return ActivateSymbol(rcBeam);
            else
            {
                var DurcBeam = BeamSymbols.FirstOrDefault(fs => fs.Family.Name.Contains("RC"));
                FamilySymbol BeamT = CreateType(doc, sizeinfo.texWidth, sizeinfo.texHeight,
                            (sizeinfo.texWidth / 10) + "x" + (sizeinfo.texHeight / 10) + "cm", DurcBeam);
                return ActivateSymbol(BeamT);
            }
        }
        private static FamilySymbol ActivateSymbol(FamilySymbol symbol)
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }
            return symbol;
        }
        private int ProcessCADGeometry(Document doc, GeometryElement geoElement)
        {
            if (geoElement == null) return 0;
            scatterLinedict = new Dictionary<Line, string>();
            midlinedict = new Dictionary<Line, string>();
            TextDict = new Dictionary<string, TextInfo>();
            Dictionary<string, List<TextInfo>> typetotext = new Dictionary<string, List<TextInfo>>
            {
                { "column", Coltextinf },
                { "beam", Frametextinf },
            };
            List<string> text = new List<string>();
            //int cols = 0;
            //int beams = 0;
            foreach (GeometryObject geoObj in geoElement)
            {
                string type = "";
                string layer = "";
                if (geoObj is GeometryInstance geoInstance)
                {
                    // 遞迴處理幾何實例
                    GeometryElement instGeoElement = geoInstance.GetInstanceGeometry();
                    ProcessCADGeometry(doc, instGeoElement);
                }
                else if (geoObj is PolyLine polyline)
                {
                    ElementId graphicsStyleId = polyline.GraphicsStyleId;
                    (type, layer) = typefilter(graphicsStyleId, doc);
                    if (type != null) ProcessCurvePolyline(doc, polyline, type, layer);
                }
                else if (geoObj is Line line)
                {
                    ElementId graphicsStyleId = line.GraphicsStyleId;
                    (type, layer) = typefilter(graphicsStyleId, doc);
                    if (layer != null) scatterLinedict.Add(line, layer); // 將線段與圖層名稱對應
                }
            }
            if (scatterLinedict.Count != 0)
            {
                double width = 0;
                Line nearestline = null;
                List<Line> centerlins = new List<Line>();
                foreach (Line line in scatterLinedict.Keys)
                {
                    try
                    {
                        List<Line> linelist = scatterLinedict.Where(i => i.Value == scatterLinedict[line] && i.Key != line).Select(i => i.Key).ToList();
                        (width, nearestline) = getNearestLine(line, linelist);
                        if (width > 0)
                        {
                            Line centerline = createcenterline(line, nearestline, doc.ActiveView);
                            if (CheckBoxSameLine(centerline, centerlins)) continue;
                            midlinedict.Add(centerline, scatterLinedict[line]);
                            centerlins.Add(centerline);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 記錄錯誤但繼續處理其他幾何
                        System.Diagnostics.Debug.WriteLine($"創建失敗: {ex.Message}");
                        continue;
                    }
                }
                if (centerlins.Count > 0)
                {
                    foreach (Line centerline in centerlins)
                    {
                        try
                        {
                            XYZ middle = (centerline.GetEndPoint(0) + centerline.GetEndPoint(1)) / 2;          //中心線的中點
                            double angle = Math.Atan(centerline.Direction.Y / centerline.Direction.X);
                            createstrutrue(doc, angle, midlinedict[centerline], middle, centerline); //創建樑或柱
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            scatterLinedict = new Dictionary<Line, string>();
            midlinedict = new Dictionary<Line, string>();
            return 0;
        }
        private (string, string) typefilter(ElementId graphicsStyleId, Document doc)
        {
            //結構圖層名稱
            if (graphicsStyleId != ElementId.InvalidElementId)
            {
                GraphicsStyle style = doc.GetElement(graphicsStyleId) as GraphicsStyle;
                if (style != null && style.GraphicsStyleCategory != null)
                {
                    string layerName = style.GraphicsStyleCategory.Name;
                    // 篩選特定圖層（忽略大小寫）
                    if (colsLayer.Contains(layerName))
                    {
                        return ("column", layerName);
                    }
                    else if (framesLayer.Contains(layerName))
                    {
                        return ("beam", layerName);
                    }
                }
            }
            return (null, null);
        }
        //中心點&創建柱/樑
        private bool ProcessCurvePolyline(Document doc, PolyLine polyline, string type, string layer)
        {
            try
            {
                Autodesk.Revit.DB.View currentView = doc.ActiveView;                // 獲取當前視圖的樓層
                // 處理多段線
                IList<XYZ> coordinates = polyline.GetCoordinates();
                List<XYZ> pass = new List<XYZ>();
                List<XYZ> uniqueCoordinates = new List<XYZ>();
                List<Line> uniqueLines = TurncoorToline(coordinates);
                for (int i = 0; i < uniqueLines.Count; i++)
                {
                    double targetZ = currentView.GenLevel.Elevation;
                    Line line = uniqueLines[i];
                    XYZ start = new XYZ(line.GetEndPoint(0).X, line.GetEndPoint(0).Y, targetZ);
                    XYZ end = new XYZ(line.GetEndPoint(1).X, line.GetEndPoint(1).Y, targetZ);
                    uniqueLines[i] = Line.CreateBound(start, end);
                }
                if (coordinates.Last().IsAlmostEqualTo(coordinates[0])) goto pass;

                scatterLine:
                foreach (Line line in uniqueLines)
                {
                    scatterLinedict.Add(line, layer);
                }
                return false;


            pass:
                uniqueCoordinates = new List<XYZ>(uniqueLines.Select(i => i.GetEndPoint(0)));
                if (Isrectangle(uniqueCoordinates/*, doc, type, levelname, polyline*/) == "True")
                {
                    var rectanglepoints = GetRectangleCorners(uniqueCoordinates);

                    XYZ point = middlepoint(rectanglepoints);
                    if (type == "column")
                    {
                        sizedict.Add(polyline, FindnearestMtext(point, Coltextinf, 0));   // 每個柱的多段線與最近的尺寸文字對應
                        createstrutrue(doc, 0, type, point, null); //創建柱子
                    }
                    else if (type == "beam")
                    {
                        List<XYZ> changeZ = new List<XYZ>();
                        foreach (XYZ po in rectanglepoints)
                        {
                            XYZ cz = new XYZ(po.X, po.Y, currentView.GenLevel.Elevation);
                            changeZ.Add(cz);
                        }
                        var middleline = getMiddleline(changeZ);
                        double angle = Math.Atan(middleline.Direction.Y / middleline.Direction.X); //與中線角度相同的尺寸標籤
                        sizedict.Add(polyline, FindnearestMtext(point, Frametextinf, angle));   // 每個柱的多段線與最近的尺寸文字對應
                        createstrutrue(doc, angle, type, point, middleline); //創建樑
                    }
                }
                else if (Isrectangle(uniqueCoordinates/*, polyline*/) == "more")
                {
                    List<Line> centerlines = Getcenter(doc, uniqueLines, uniqueCoordinates);
                    foreach (Line centerline in centerlines)
                    {
                        try
                        {
                            XYZ middle = (centerline.GetEndPoint(0) + centerline.GetEndPoint(1)) / 2;          //中心線的中點
                            double angle = Math.Atan(centerline.Direction.Y / centerline.Direction.X);
                            createstrutrue(doc, angle, type, middle, centerline);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
                else /*if (Isrectangle(uniqueCoordinates) == "false")*/
                {
                    goto scatterLine;
                }
            }
            catch (Exception ex)
            {
                // 記錄錯誤但繼續處理其他幾何
                System.Diagnostics.Debug.WriteLine($"創建失敗: {ex.Message}");
            }
            return false;
        }
        private static List<Line> TurncoorToline(IList<XYZ> uniqueCoordinates)
        {
            List<Line> madelines = new List<Line>();
            for (int i = 0; i < uniqueCoordinates.Count; i++)
            {
                try
                {
                    Line line = null;
                    if (i == uniqueCoordinates.Count - 1 && uniqueCoordinates[i].IsAlmostEqualTo(uniqueCoordinates[0])) //最後一條封閉線段
                    {
                        break;
                    }
                    line = Line.CreateBound(uniqueCoordinates[i], uniqueCoordinates[i + 1]);
                    if (i == 0) goto ADD;
                    //同一邊上有兩條線
                    if (CheckDirection(line, madelines.Last()) && line.GetEndPoint(0).IsAlmostEqualTo(madelines.Last().GetEndPoint(1)))
                    {
                        if (uniqueCoordinates.Where(j => j.Equals(line.GetEndPoint(0))).Count() > 2) continue;
                        madelines[madelines.Count - 1] = Line.CreateBound(madelines.Last().GetEndPoint(0), line.GetEndPoint(1));
                        continue;
                    }
                ADD:
                    madelines.Add(line);

                }
                catch
                {
                    continue;
                }
            }
            return madelines;
        }
        private static XYZ middlepoint(List<XYZ> rectanglepoints)
        {
            double xS = 0, yS = 0;
            foreach (XYZ points in rectanglepoints.Take(4))
            {
                xS += points.X;
                yS += points.Y;
            }
            XYZ point = new XYZ(xS / rectanglepoints.Count, yS / rectanglepoints.Count, 0);
            return point;
        }
        private string Isrectangle(List<XYZ> points/*, Document doc, string type, string colsLayer, PolyLine polyline*/)
        {
            // 如果點數少於4或多於5，不可能是矩形
            if (points.Count < 4) return "false";
            if (points.Count > 5) return "more";
            // 如果是5個點，檢查是否有重複點或中間點
            if (points.Count == 5) return IsRectangleWith5Points(points).ToString();
            // 如果是4個點，檢查是否為矩形
            return IsRectangleWith4Points(points).ToString();
        }
        private bool IsRectangleWith4Points(List<XYZ> points)
        {
            // 檢查4個點是否組成矩形
            for (int i = 0; i < 4; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % 4];
                var p3 = points[(i + 2) % 4];

                // 計算向量
                var v1 = new XYZ(p1.X - p2.X, p1.Y - p2.Y, 0);
                var v2 = new XYZ(p3.X - p2.X, p3.Y - p2.Y, 0);

                // 檢查是否垂直（點積接近0）
                double dotProduct = v1.DotProduct(v2);
                if (Math.Abs(dotProduct) > 0.001)
                    return false;
            }
            return true;
        }
        private bool IsRectangleWith5Points(List<XYZ> points)
        {
            // 嘗試找出哪個點是多餘的（在一條邊上的中間點）
            for (int i = 0; i < points.Count; i++)
            {
                var testPoints = new List<XYZ>(points);
                testPoints.RemoveAt(i);
                if (IsRectangleWith4Points(testPoints))
                {
                    return true;
                }
            }
            return false;
        }
        private List<XYZ> GetRectangleCorners(List<XYZ> points)
        {
            if (points.Count == 4)
            {
                return points;
            }
            else if (points.Count == 5)
            {
                // 找出真正的4個角點
                for (int i = 0; i < points.Count; i++)
                {
                    var testPoints = new List<XYZ>(points);
                    testPoints.RemoveAt(i);

                    if (IsRectangleWith4Points(testPoints))
                    {
                        return testPoints;
                    }
                }
            }
            return points.Take(4).ToList(); // 備用方案
        }
        private static void Getstruinfo(TextInfo textInfo)
        {
            var matchtext = Regex.Match(textInfo.Content, pattern); //找"__x__"
            if (matchtext.Success)
            {
                //textInfo.prefix = matchtext.Groups[1].Value;
                textInfo.texWidth = int.Parse(matchtext.Groups[1].Value, CultureInfo.InvariantCulture) * 10;
                textInfo.texHeight = int.Parse(matchtext.Groups[2].Value, CultureInfo.InvariantCulture) * 10;
            }
            //test
            else  //找
            {
                if (TextDict.Count == 0) getTextdiction();
                foreach (string key in TextDict.Keys.Where(i => i != null))
                {
                    try
                    {
                        string letters = Regex.Match(Regex.Match(textInfo.Content, @"[A-Za-z]+").Value, key).Value;
                        if (letters == "") continue;
                        Getstruinfo(TextDict[letters]);
                        textInfo.texWidth = TextDict[letters].texWidth;
                        textInfo.texHeight = TextDict[letters].texHeight;
                        break;
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
        }
        private static TextInfo FindnearestMtext(XYZ strucpoint, List<TextInfo> textsinfo, double angle)
        {
            double mindis = double.MaxValue;
            TextInfo nearestMtext = null;
            foreach (TextInfo text in textsinfo.Where(i => (Math.Abs(i.Rotation % radian - Math.Abs(angle % radian))) < 1))
            {
                double distance = CalculateDistance(text.Position, strucpoint, k);
                if (distance < mindis)
                {
                    mindis = distance;
                    nearestMtext = text;
                }
            }
            return nearestMtext;
        }
        //距離計算
        private static double CalculateDistance(CSMath.XYZ pointCAD, XYZ Revpoint, double k)
        {
            double dx = pointCAD.X * k + Vector.X - Revpoint.X;
            double dy = pointCAD.Y * k + Vector.Y - Revpoint.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }
        private Line getMiddleline(List<XYZ> rectangle)
        {

            var Dside1 = Line.CreateBound((rectangle[0] + rectangle[3]) / 2, (rectangle[1] + rectangle[2]) / 2);
            var Dside2 = Line.CreateBound((rectangle[0] + rectangle[1]) / 2, (rectangle[2] + rectangle[3]) / 2);
            return Dside1.Length >= Dside2.Length ? Dside1 : Dside2;
        }
        private string GetCADFilePath()
        {
            using (System.Windows.Forms.OpenFileDialog openFilelog = new System.Windows.Forms.OpenFileDialog())
            {
                openFilelog.Filter = "CAD Files (*.dwg)|*.dwg|ALL Files(*.*)|*.*";
                openFilelog.Title = "匯入CAD檔案";
                openFilelog.RestoreDirectory = true;
                if (openFilelog.ShowDialog() == DialogResult.OK)
                {
                    return openFilelog.FileName;
                }
                return null;
            }
        }
        //get cad path
        private string getpath(ImportInstance import)
        {
            string path = import.Category.Name;
            return path;
        }
        private Level Foundlevelfrompath(Document doc)
        {
            var match = Regex.Match(Userimpath, @"([A-Z][A-Z]\d+)", RegexOptions.IgnoreCase);
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Level));

            foreach (Level level in collector.Cast<Level>())
            {
                if (match.Success)
                {
                    string sertch = match.Groups[1].Value;
                    FilteredElementCollector levelcoll = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level));
                    foreach (Level lev in levelcoll.Cast<Level>())
                    {
                        if (lev.Name.Contains(sertch))
                        {
                            return lev;
                        }
                    }
                }
            }
            return null;
        }
        public class TextInfo
        {
            public string Content { get; set; }
            public string LayerName { get; set; }
            public CSMath.XYZ Position { get; set; }
            public double Height { get; set; }
            public int texWidth { get; set; }
            public int texHeight { get; set; }
            public string prefix { get; set; }
            public double Rotation { get; set; }
            //public string StyleName { get; set; }
        }
        public class Lineinf
        {
            public Line Lineset { get; set; }
            public bool used { get; set; } // 是否已經使用過
        }
        private void ReadCad(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    CadDocument document;
                    var reader = new DwgReader(path);
                    document = reader.Read();
                    //文字類型
                    foreach (Entity entity in document.Entities)
                    {
                        if (entity is TextEntity text)
                        {
                            TextInfo info = new TextInfo
                            {
                                Content = text.Value,
                                LayerName = text.Layer.Name,
                                Position = text.InsertPoint,
                                Height = text.Height,
                                Rotation = text.Rotation,
                            };
                            textinfos.Add(info);
                        }
                        else if (entity is MText mtext)
                        {
                            TextInfo info = new TextInfo
                            {
                                Content = mtext.Value,
                                LayerName = mtext.Layer.Name,
                                Position = mtext.InsertPoint,
                                Height = mtext.Height,
                                Rotation = mtext.Rotation,
                            };
                            textinfos.Add(info);
                        }
                        else if (entity is ACadSharp.Entities.Line line && line.Layer.Name.Equals(gridLLayer))
                        {
                            CADGridLine.Add(line);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", "無法讀取CAD文件: " + ex.Message);
                // 可以選擇記錄錯誤或處理異常
            }
        }
        private List<Line> Getcenter(Document doc, List<Line> sidelines, List<XYZ> uniqueCoordinates)
        {
            List<Line> centerlineornot = new List<Line>();
            List<Line> centerlines = new List<Line>();
            List<Line> middleline = new List<Line>();
            double width = new double();
            Dictionary<Line, Line> linenear = new Dictionary<Line, Line>();
            Autodesk.Revit.DB.View currentView = doc.ActiveView;                // 獲取當前視圖的樓層
            foreach (Line line in sidelines)
            {
                Line nearestline = null;
                (width, nearestline) = getNearestLine(line, sidelines);
                if (width <= 0) continue;
                Line centerline = createcenterline(line, nearestline, currentView);


                if (centerline != null)
                {
                    centerlines = FilterCenterLinesContainingAnyOther(centerlines, centerline, width, uniqueCoordinates);

                }
            }

            return centerlines;
        }
        private static List<Line> FilterCenterLinesContainingAnyOther(List<Line> lines, Line line, double width, List<XYZ> uniqueCoordinates)
        {
            if (CheckBoxSameLine(line, lines)) return lines;

            // 創建新的線條
            XYZ normal = new XYZ(0, 0, 1);

            Curve lineplus = line.CreateOffset(width / 2, normal);
            Curve lineminus = line.CreateOffset(-width / 2, normal);
            int equals = 0;

            XYZ[] points = new[]
            {
                lineplus.GetEndPoint(0),
                lineplus.GetEndPoint(1),
                lineminus.GetEndPoint(0),
                lineminus.GetEndPoint(1)
            };
            foreach (XYZ poi in points)
            {
                if (equals == 4) break;
                foreach (XYZ coor in uniqueCoordinates)
                {
                    if (DistanceXY(poi, coor) <= _tolerance)
                    {
                        equals++;
                        break;
                    }
                }
                if (equals == 0) break;
            }
            if (equals < 4) return lines;
            lines.Add(line);

            // 遍歷每條線段
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != line)
                {
                    if (IsLineContained(lines[i], line))
                    {
                        if (lines[i].Length < line.Length)
                        {
                            lines.Remove(lines[i]);
                            i--;
                        }
                        else lines.Remove(line);
                    }
                }
            }
            return lines;
        }
        public static double DistanceXY(XYZ point1, XYZ point2)
        {
            double deltaX = point2.X - point1.X;
            double deltaY = point2.Y - point1.Y;
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }
        private static bool IsLineContained(Line line1, Line line2)
        {
            // 確保兩條線段都是有界的
            if (!line1.IsBound || !line2.IsBound)
            {
                return false; // 如果任一線段無界，直接返回 false
            }

            // 獲取 line2 的起點和終點
            XYZ startPoint = line2.GetEndPoint(0);
            XYZ endPoint = line2.GetEndPoint(1);
            IntersectionResult projStart = line1.Project(startPoint);
            IntersectionResult projEnd = line1.Project(endPoint);
            if (projStart == null || projEnd == null)
            {
                return false; // 如果投影結果為 null，則 line2 不在 line1 上
            }
            double paramStart = projStart.Parameter;
            double paramEnd = projEnd.Parameter;
            bool startInside = line1.IsInside(paramStart)
            && startPoint.DistanceTo(projStart.XYZPoint) < _tolerance;
            bool endInside = line1.IsInside(paramEnd)
            && endPoint.DistanceTo(projEnd.XYZPoint) < _tolerance;

            // 檢查方向一致性
            bool directionsAligned = DoLinesIntersect2D(line1, line2);

            return directionsAligned;
        }
        private static bool CheckDirection(Line line1, Line line2)
        {
            const double tolerance = 1E-9;
            XYZ direction1 = line1.Direction.Normalize();
            XYZ direction2 = line2.Direction.Normalize();
            double dotProduct = direction1.DotProduct(direction2);
            bool directionsAligned = Math.Abs(Math.Abs(dotProduct) - 1) < tolerance;
            return directionsAligned;
        }
        private static (double, Line) getNearestLine(Line line, List<Line> linelist)
        {
            Line nearest = null;
            double minDistance = double.MaxValue;
            for (int i = 0; i < linelist.Count; i++)
            {
                if (line.Equals(linelist[i])) continue; // 跳過自身
                if (CheckDirection(line, linelist[i]))
                {
                    IntersectionResult result1 = line.Project(linelist[i].GetEndPoint(0));
                    IntersectionResult result2 = line.Project(linelist[i].GetEndPoint(1));
                    double distance = result1.Distance <= result2.Distance ? result1.Distance : result2.Distance;
                    if (distance < minDistance && distance > _tolerance && (checkcontainline(line, linelist[i])))
                    {
                        minDistance = distance;
                        nearest = linelist[i];
                    }
                }
            }
            return (minDistance, nearest);
        }
        //與對應的最近平行線創建中線
        private static Line createcenterline(Line line1, Line line2, Autodesk.Revit.DB.View currentView)
        {
            if (line1 == null || line2 == null || line1 == null || line2 == null || currentView == null)
            {
                return null;
            }
            if (!line1.Direction.Normalize().Equals(line2.Direction.Normalize()))
            {
                //line2 = line2.CreateReversed() as Line;
                line2 = Line.CreateBound(line2.GetEndPoint(1), line2.GetEndPoint(0));
            }
            Line center = null;
            //if (line1.Length >= line2.Length)
            //{
            //    if (line1.GetEndPoint(0).DistanceTo(line2.GetEndPoint(0)) == line1.Distance(line2.GetEndPoint(0)))
            //    {
            //        center = line1.CreateOffset(line1.Distance(line2.GetEndPoint(0)) / 2, Line.CreateBound(line1.GetEndPoint(0), line2.GetEndPoint(0)).Direction) as Line;
            //    }
            //    else
            //    {
            //        center = line1.CreateOffset(line1.Distance(line2.GetEndPoint(0)) / 2, Line.CreateBound(line1.GetEndPoint(1), line2.GetEndPoint(1)).Direction) as Line;
            //    }
            //}
            //else
            //{
            //    if (line2.GetEndPoint(0).DistanceTo(line1.GetEndPoint(0)) == line2.Distance(line1.GetEndPoint(0)))
            //    {
            //        center = line2.CreateOffset(line2.Distance(line1.GetEndPoint(0)) / 2, Line.CreateBound(line2.GetEndPoint(0), line1.GetEndPoint(0)).Direction) as Line;
            //    }
            //    else
            //    {
            //        center = line2.CreateOffset(line2.Distance(line1.GetEndPoint(0)) / 2, Line.CreateBound(line2.GetEndPoint(1), line1.GetEndPoint(1)).Direction) as Line;
            //    }
            //}


            if (line1.Direction.IsAlmostEqualTo(XYZ.BasisX) || line1.Direction.IsAlmostEqualTo(-XYZ.BasisX))
            {
                center = Line.CreateBound(new XYZ((line1.Length >= line2.Length ? line1.GetEndPoint(0).X : line2.GetEndPoint(0).X),
                                                  (line1.GetEndPoint(0).Y + line2.GetEndPoint(0).Y) / 2,
                                                  currentView.GenLevel.Elevation),
                                          (new XYZ((line1.Length >= line2.Length ? line1.GetEndPoint(1).X : line2.GetEndPoint(1).X),
                                                  (line1.GetEndPoint(1).Y + line2.GetEndPoint(1).Y) / 2,
                                                  currentView.GenLevel.Elevation)));
            }
            else if (line1.Direction.IsAlmostEqualTo(XYZ.BasisY) || line1.Direction.IsAlmostEqualTo(-XYZ.BasisY))
            {
                center = Line.CreateBound(new XYZ((line1.GetEndPoint(0).X + line2.GetEndPoint(0).X) / 2,
                                                 (line1.Length >= line2.Length ? line1.GetEndPoint(0).Y : line2.GetEndPoint(0).Y),
                                                 currentView.GenLevel.Elevation),
                                          (new XYZ((line1.GetEndPoint(1).X + line2.GetEndPoint(1).X) / 2,
                                                 (line1.Length >= line2.Length ? line1.GetEndPoint(1).Y : line2.GetEndPoint(1).Y),
                                                 currentView.GenLevel.Elevation)));
            }
            //line1.used = true;
            //line2.used = true;

            return center;

        }
        //確認匹配的線是否投影重疊
        private static bool checkcontainline(Line line1, Line line2)
        {
            Line Lline = line1.Length >= line2.Length ? line1 : line2;
            Line Sline = line1.Length < line2.Length ? line1 : line2;
            double LlineSx = Lline.GetEndPoint(0).X;
            double LlineFx = Lline.GetEndPoint(1).X;
            double LlineSy = Lline.GetEndPoint(0).Y;
            double LlineFy = Lline.GetEndPoint(1).Y;
            double SlineSx = Sline.GetEndPoint(0).X;
            double SlineFx = Sline.GetEndPoint(1).X;
            double SlineSy = Sline.GetEndPoint(0).Y;
            double SlineFy = Sline.GetEndPoint(1).Y;

            double minLx = Math.Min(LlineSx, LlineFx);
            double maxLx = Math.Max(LlineSx, LlineFx);
            double minLy = Math.Min(LlineSy, LlineFy);
            double maxLy = Math.Max(LlineSy, LlineFy);
            double minSx = Math.Min(SlineSx, SlineFx);
            double maxSx = Math.Max(SlineSx, SlineFx);
            double minSy = Math.Min(SlineSy, SlineFy);
            double maxSy = Math.Max(SlineSy, SlineFy);

            if ((Lline.Direction.Normalize().IsAlmostEqualTo(XYZ.BasisX) || Lline.Direction.Normalize().IsAlmostEqualTo(-XYZ.BasisX)) &&
                (Sline.Direction.Normalize().IsAlmostEqualTo(XYZ.BasisX) || Sline.Direction.Normalize().IsAlmostEqualTo(-XYZ.BasisX)))
            {
                return !(minLx > minSx + _tolerance || maxLx + _tolerance < maxSx);
            }
            else if ((Lline.Direction.Normalize().IsAlmostEqualTo(XYZ.BasisY) || Lline.Direction.Normalize().IsAlmostEqualTo(-XYZ.BasisY)) &&
                     (Sline.Direction.Normalize().IsAlmostEqualTo(XYZ.BasisY) || Sline.Direction.Normalize().IsAlmostEqualTo(-XYZ.BasisY)))
            {
                return !(minLy > minSy + _tolerance || maxLy + _tolerance < maxSy);
            }
            return false;
        }
        private static bool DoLinesIntersect2D(Line line1, Line line2)
        {
            if (line1 == null || line2 == null || !line1.IsBound || !line2.IsBound)
            {
                return false; // 無效輸入或無界線段
            }

            // 獲取線段端點
            XYZ a = line1.GetEndPoint(0); // 起點 A
            XYZ b = line1.GetEndPoint(1); // 終點 B
            XYZ c = line2.GetEndPoint(0); // 起點 C
            XYZ d = line2.GetEndPoint(1); // 終點 D

            // 計算方向向量
            double d1x = b.X - a.X; // d1 = B - A
            double d1y = b.Y - a.Y;
            double d2x = d.X - c.X; // d2 = D - C
            double d2y = d.Y - c.Y;

            // 計算叉積
            double s1 = d1x * (c.Y - a.Y) - d1y * (c.X - a.X); // d1 × (C - A)
            double s2 = d1x * (d.Y - a.Y) - d1y * (d.X - a.X); // d1 × (D - A)
            double t1 = d2x * (a.Y - c.Y) - d2y * (a.X - c.X); // d2 × (A - C)
            double t2 = d2x * (b.Y - c.Y) - d2y * (b.X - c.X); // d2 × (B - C)

            // 檢查是否共線
            if (Math.Abs(s1) < _tolerance && Math.Abs(s2) < _tolerance && Math.Abs(t1) < _tolerance && Math.Abs(t2) < _tolerance)
            {
                // 檢查投影範圍是否重疊
                double minX1 = Math.Min(a.X, b.X);
                double maxX1 = Math.Max(a.X, b.X);
                double minX2 = Math.Min(c.X, d.X);
                double maxX2 = Math.Max(c.X, d.X);
                double minY1 = Math.Min(a.Y, b.Y);
                double maxY1 = Math.Max(a.Y, b.Y);
                double minY2 = Math.Min(c.Y, d.Y);
                double maxY2 = Math.Max(c.Y, d.Y);

                return !(minX1 > maxX2 + _tolerance || maxX1 + _tolerance < minX2 ||
                         minY1 > maxY2 + _tolerance || maxY1 + _tolerance < minY2);
            }

            // 檢查叉積是否異號（包括端點）
            return s1 * s2 <= _tolerance && t1 * t2 <= _tolerance;
        }

        private static void getTextdiction()
        {
            foreach (TextInfo size in DictText.Where(i => Regex.Match(i.Content, pattern).Success))
            {
                foreach (TextInfo text in DictText.Where(j => Regex.Match(j.Content, Dictrule, RegexOptions.IgnoreCase).Success))
                {
                    var matchdict = Regex.Match(text.Content, Dictrule, RegexOptions.IgnoreCase);
                    if (Math.Abs(size.Position.Y - text.Position.Y) <= 5 && Math.Abs(size.Position.X - text.Position.X) <= 1600)
                    {
                        TextDict.Add(matchdict.Groups[1].Value, size);
                        TextDict.Add(matchdict.Groups[2].Value, size);
                        break;
                    }
                }
            }
        }
        private static bool CheckBoxSameLine(Line line, List<Line> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != line)
                {
                    if (!line.Direction.Normalize().Equals(lines[i].Direction.Normalize()))
                    {
                        line = Line.CreateBound(line.GetEndPoint(1), line.GetEndPoint(0));
                    }
                    if ((lines[i].Distance(line.GetEndPoint(0)) < _tolerance &&
                        lines[i].Distance(line.GetEndPoint(1)) < _tolerance) ||
                        (line.Distance(lines[i].GetEndPoint(0)) < _tolerance &&
                        line.Distance(lines[i].GetEndPoint(1)) < _tolerance))
                    {
                        return true;
                    }

                }
                else return true;
            }
            return false;
        }
        private static void createstrutrue(Document doc, double angle, string type, XYZ middle, Line centerline)
        {
            if (colsLayer.Contains(type)) type = "column";
            if (framesLayer.Contains(type)) type = "beam";
            Autodesk.Revit.DB.View currentView = doc.ActiveView;
            if (type == "column")
            {
                var sizetext = FindnearestMtext(middle, Coltextinf, angle);
                Getstruinfo(sizetext); // 獲取尺寸信息


                FamilySymbol Symbol = FindColumnFamilySymbol(doc, sizetext);
                FamilyInstance structure = doc.Create.NewFamilyInstance(
                    middle,
                    Symbol,
                    currentView.GenLevel,
                    Autodesk.Revit.DB.Structure.StructuralType.Column);
            }
            else if (type == "beam")
            {
                var sizetext = FindnearestMtext(middle, Frametextinf, angle);
                Getstruinfo(sizetext); // 獲取尺寸信息

                FamilySymbol Symbol = FindBeamFamilySymbol(doc, sizetext);
                FamilyInstance structure = doc.Create.NewFamilyInstance(
                    centerline,
                    Symbol,
                    currentView.GenLevel,
                    Autodesk.Revit.DB.Structure.StructuralType.Beam);
            }

        }
    }
}



//用CAD匯入的內容提取CAD圖的level
//foreach (ImportInstance import in existingImports)
//{
//    Parameter levelParam = import.get_Parameter(BuiltInParameter.IMPORT_BASE_LEVEL);
//    if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
//    {
//        Level level = doc.GetElement(levelParam.AsElementId()) as Level;
//        if (level != null)
//        {
//            message += $"Import {import.Id} 的基準 Level: {level.Name}\n";
//        }
//    }
//}