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
namespace JoinGeometryUtils
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication a)
        {
            a.CreateRibbonTab("建造");
            RibbonPanel AECPanelDebug = a.CreateRibbonPanel("建造", "建造");
            string path = Assembly.GetExecutingAssembly().Location;
            #region DockableWindow

            PushButtonData AutoJoinGeomatryUtils = new PushButtonData("建立樑柱", "建立樑柱", path, "JoinGeometryUtils.AutoJoinGeomatryUtils");

            //PushButtonData deJoinGeomatryUtils = new PushButtonData("deJoinGeomatryUtils", "deJoinGeomatryUtils", path, "JoinGeometryUtils.deAutoJoinGeomatryUtils");
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
        public static List<TextInfo> Coltextinf = new List<TextInfo>();
        public static List<TextInfo> Frametextinf = new List<TextInfo>();
        public static Dictionary<Autodesk.Revit.DB.PolyLine, TextInfo> sizedict = new Dictionary<Autodesk.Revit.DB.PolyLine, TextInfo>();
        public static string Userimpath = "";
        public static List<XYZ> uniqueCoordinates = new List<XYZ>();
        public static double _tolerance = 0.001;
        public static double k = 0.9 / 28;  //座標轉換比例




        //public static Dictionary<Polyline, CSMath.XYZ> polylinepointincad = new Dictionary<Polyline, CSMath.XYZ>();
        //public static Transform cadTransform = Transform.Identity;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Autodesk.Revit.DB.Document doc = uidoc.Document;
            Autodesk.Revit.DB.View activeview = doc.ActiveView;

            Userimpath = GetCADFilePath();
            if (Userimpath == null)
            {
                message += "未選取檔案";
            }
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
                    List<TextInfo> textinfos = new List<TextInfo>();
                    textinfos.AddRange(ReadText(Userimpath));
                    Coltextinf = textinfos.Where(t => t.LayerName.Contains("S-COLS-IDEN")).ToList();
                    Frametextinf = textinfos.Where(t => t.LayerName.Contains("S-BEAM-IDEN")).ToList();

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
                                message += $"處理CAD Import {processedImports}：創建了 {columnsFromThisImport} 根柱子\n";
                            }
                        }
                        catch (Exception ex)
                        {
                            message += $"處理CAD Import時發生錯誤：{ex.Message}\n";
                        }
                    }

                    trans.Commit();
                    message += $"\n處理完成！\n總共處理了 {processedImports} 個CAD Import\n總共創建了 {createdColumns} 根柱子";
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
        private FamilySymbol CreateCustomType(Document doc, int width, int height, string typename, FamilySymbol copytype)
        {
            // 複製族群符號
            FamilySymbol newSymbol = copytype.Duplicate(typename) as FamilySymbol;

            // 設定尺寸參數
            SetDimensions(newSymbol, width, height);

            if (!newSymbol.IsActive)
                newSymbol.Activate();

            return newSymbol;
        }
        //設定新類型尺寸
        private void SetDimensions(FamilySymbol newSymbol, int width, int height)
        {
            string[] widthParam = { "b", "Width" };
            string[] heightParam = { "h", "Height" };
            foreach (string paraName in widthParam)
            {
                Parameter param = newSymbol.LookupParameter(paraName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(UnitUtils.ConvertToInternalUnits(width, UnitTypeId.Millimeters));
                    break;
                }
            }
            foreach (string paramName in heightParam)
            {
                Parameter param = newSymbol.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(UnitUtils.ConvertToInternalUnits(height, UnitTypeId.Millimeters));
                    break;
                }
            }
        }

        private FamilySymbol FindColumnFamilySymbol(Document doc, TextInfo sizeinfo)
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
                FamilySymbol columnT = CreateCustomType(doc, sizeinfo.texWidth, sizeinfo.texHeight,
                            (sizeinfo.texWidth / 10) + "x" + (sizeinfo.texHeight / 10) + "cm", DurcColumn);
                return ActivateSymbol(columnT);
            }
        }
        private FamilySymbol FindBeamFamilySymbol(Document doc, TextInfo sizeinfo)
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
                FamilySymbol BeamT = CreateCustomType(doc, sizeinfo.texWidth, sizeinfo.texHeight,
                            (sizeinfo.texWidth / 10) + "x" + (sizeinfo.texHeight / 10) + "cm", DurcBeam);
                return ActivateSymbol(BeamT);
            }
        }

        private FamilySymbol ActivateSymbol(FamilySymbol symbol)
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
            string colsLayer = "S-COLS";
            string framesLayer = "S-BEAM";
            List<string> text = new List<string>();
            //int cols = 0;
            //int beams = 0;

            foreach (GeometryObject geoObj in geoElement)
            {
                if (geoObj is GeometryInstance geoInstance)
                {
                    // 遞迴處理幾何實例
                    GeometryElement instGeoElement = geoInstance.GetInstanceGeometry();
                    ProcessCADGeometry(doc, instGeoElement);
                }
                else if (geoObj is PolyLine polyline)
                {
                    ElementId graphicsStyleId = polyline.GraphicsStyleId;
                    if (graphicsStyleId != ElementId.InvalidElementId)
                    {
                        GraphicsStyle style = doc.GetElement(graphicsStyleId) as GraphicsStyle;
                        if (style != null && style.GraphicsStyleCategory != null)
                        {
                            string layerName = style.GraphicsStyleCategory.Name;
                            // 篩選特定圖層（忽略大小寫）
                            if (string.Equals(layerName, colsLayer, StringComparison.OrdinalIgnoreCase))
                            {
                                string type = "column";
                                ProcessCurve(doc, polyline, type, colsLayer);
                            }
                            else if (string.Equals(layerName, framesLayer, StringComparison.OrdinalIgnoreCase))
                            {
                                string type = "beam";
                                ProcessCurve(doc, polyline, type, framesLayer);
                            }
                        }
                    }
                }
            }
            return 0;
        }
        //中心點&創建柱/樑
        private bool ProcessCurve(Document doc, PolyLine polyline, string type, string levelname)
        {
            try
            {
                // 處理多段線
                IList<XYZ> coordinates = polyline.GetCoordinates();
                uniqueCoordinates = new List<XYZ>();
                for (int i = 0; i < coordinates.Count; i++)
                {
                    if (i == coordinates.Count - 1 && coordinates[i].IsAlmostEqualTo(coordinates[0]))
                    {
                        continue;
                    }
                    uniqueCoordinates.Add(coordinates[i]);
                }
                //List<Lineinf> uniqueLines = TurncoorToline(uniqueCoordinates);
                Autodesk.Revit.DB.View currentView = doc.ActiveView;                // 獲取當前視圖的樓層
                if (Isrectangle(uniqueCoordinates/*, doc, type, levelname, polyline*/) == "True")
                {
                    var rectanglepoints = GetRectangleCorners(uniqueCoordinates);
                    double xS = 0, yS = 0;
                    foreach (XYZ points in rectanglepoints.Take(4))
                    {
                        xS += points.X;
                        yS += points.Y;
                    }
                    XYZ point = new XYZ(xS / rectanglepoints.Count, yS / rectanglepoints.Count, 0);
                    if (type == "column")
                    {
                        sizedict.Add(polyline, FindnearestMtext(point, Coltextinf));   // 每個柱的多段線與最近的尺寸文字對應
                        Getstruinfo(sizedict[polyline]); // 獲取尺寸信息

                        FamilySymbol columnSymbol = FindColumnFamilySymbol(doc, sizedict[polyline]);
                        FamilyInstance column = doc.Create.NewFamilyInstance(
                            point,
                            columnSymbol,
                            currentView.GenLevel,
                            Autodesk.Revit.DB.Structure.StructuralType.Column);
                    }
                    else if (type == "beam")
                    {
                        sizedict.Add(polyline, FindnearestMtext(point, Frametextinf));   // 每個柱的多段線與最近的尺寸文字對應
                        Getstruinfo(sizedict[polyline]); // 獲取尺寸信息
                        List<XYZ> changeZ = new List<XYZ>();
                        foreach (XYZ po in rectanglepoints)
                        {
                            XYZ cz = new XYZ(po.X, po.Y, currentView.GenLevel.Elevation);
                            changeZ.Add(cz);
                        }

                        var middleline = getMiddleline(changeZ);

                        FamilySymbol beamSymbol = FindBeamFamilySymbol(doc, sizedict[polyline]);
                        FamilyInstance beam = doc.Create.NewFamilyInstance(
                            middleline,
                            beamSymbol,
                            currentView.GenLevel,
                            Autodesk.Revit.DB.Structure.StructuralType.Beam);
                    }
                }
                else if (Isrectangle(uniqueCoordinates/*, polyline*/) == "more")
                {
                    List<Line> centerlines = Getcenter(doc, uniqueCoordinates);
                    foreach (Line centerline in centerlines)
                    {
                        try
                        {
                            XYZ middle = (centerline.GetEndPoint(0) + centerline.GetEndPoint(1)) / 2;          //中心線的中點
                            var sizetext = FindnearestMtext(middle, Frametextinf);
                            Getstruinfo(sizetext); // 獲取尺寸信息
                            FamilySymbol beamSymbol = type == "column" ? FindColumnFamilySymbol(doc, sizetext) : FindBeamFamilySymbol(doc, sizetext);
                            FamilyInstance beam = doc.Create.NewFamilyInstance(
                                centerline,
                                beamSymbol,
                                currentView.GenLevel,
                                Autodesk.Revit.DB.Structure.StructuralType.Beam);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 記錄錯誤但繼續處理其他幾何
                System.Diagnostics.Debug.WriteLine($"創建失敗: {ex.Message}");
            }
            return false;
        }
        private static List<Lineinf> TurncoorToline(List<XYZ> uniqueCoordinates)
        {
            List<Lineinf> madelines = new List<Lineinf>();
            for (int i = 0; i < uniqueCoordinates.Count; i++)
            {
                Lineinf lineinf = null;
                if (i == uniqueCoordinates.Count - 1) //最後一條封閉線段
                {
                    lineinf = new Lineinf
                    {
                        Lineset = Line.CreateBound(uniqueCoordinates[i], uniqueCoordinates[0]),
                        used = false
                    };
                    goto CHECK;
                }
                lineinf = new Lineinf
                {
                    Lineset = Line.CreateBound(uniqueCoordinates[i], uniqueCoordinates[i + 1]),
                    used = false
                };
                if (i == 0) goto ADD;
                CHECK: //同一邊上有兩條線
                if (CheckDirection(lineinf, madelines[i - 1]) && lineinf.Lineset.GetEndPoint(0).IsAlmostEqualTo(madelines[i - 1].Lineset.GetEndPoint(1)))
                {
                    madelines[i - 1].Lineset = Line.CreateBound(madelines[i - 1].Lineset.GetEndPoint(0), lineinf.Lineset.GetEndPoint(1));
                    continue;
                }
            ADD:
                madelines.Add(lineinf);
            }
            return madelines;
        }
        private string Isrectangle(List<XYZ> points/*, Document doc, string type, string colsLayer, PolyLine polyline*/)
        {

            // 如果點數少於4或多於5，不可能是矩形
            if (points.Count < 4) return "false";
            if (points.Count > 5)
            {
                return "more";
                //uniqueCoordinates = new List<XYZ>();
                //for (int i = 0; i < points.Count - 3; i++)
                //{
                //    for (int j = 1; j < points.Count - 2; j++)
                //    {
                //        for (int k = 2; k < points.Count - 1; k++)
                //        {
                //            for (int l = 3; l < points.Count; l++)
                //            {
                //                List<XYZ> recheck = new List<XYZ> { points[i], points[j], points[k], points[l], points[i] };
                //                if (Isrectangle(recheck, doc, type, colsLayer))
                //                {
                //                    PolyLine polyline = PolyLine.Create(recheck);
                //                    ProcessCurve(doc, polyline, type, colsLayer);
                //                }
                //            }
                //        }
                //    }
                //}


                //List<Line> allLines = new List<Line>();
                //for (int i = 0; i < points.Count - 1; i++)
                //{
                //    allLines.Add(Line.CreateBound(points[i], points[i + 1]));
                //}
                //allLines.Add(Line.CreateBound(points[points.Count - 1], points[0])); // 關閉多邊形
                //var rectangles = FindRectanglesByRegion(allLines);
                //foreach (var rectangle in rectangles)
                //{
                //    Isrectangle(rectangle);
                //}
            }
            // 如果是5個點，檢查是否有重複點或中間點
            if (points.Count == 5)
            {
                // 可能是矩形一邊被分成兩段的情況
                return IsRectangleWith5Points(points).ToString();
            }
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
        private void Getstruinfo(TextInfo textInfo)
        {
            var pattern = @"_(\d+)x(\d+)";
            var matchtext = Regex.Match(textInfo.Content, pattern);
            if (matchtext.Success)
            {
                //textInfo.prefix = matchtext.Groups[1].Value;
                textInfo.texWidth = int.Parse(matchtext.Groups[1].Value, CultureInfo.InvariantCulture) * 10;
                textInfo.texHeight = int.Parse(matchtext.Groups[2].Value, CultureInfo.InvariantCulture) * 10;
            }
        }
        private TextInfo FindnearestMtext(XYZ strucpoint, List<TextInfo> textsinfo)
        {
            double mindis = double.MaxValue;
            TextInfo nearestMtext = null;
            foreach (TextInfo text in textsinfo)
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
        private double CalculateDistance(CSMath.XYZ pointfound, XYZ Colpoint, double k)
        {
            double dx = pointfound.X * k - Colpoint.X;
            double dy = pointfound.Y * k - Colpoint.Y;
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
        private Level Foundlevel(Document doc)
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
            public bool used { get; set; } = false; // 是否已經使用過
        }
        private List<TextInfo> ReadText(string path)
        {
            List<TextInfo> textInfos = new List<TextInfo>();
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    CadDocument document;
                    var reader = new DwgReader(path);
                    document = reader.Read();
                    foreach (var entity in document.Entities)
                    {
                        if (entity is MText text)
                        {
                            TextInfo info = new TextInfo
                            {
                                Content = text.Value,
                                LayerName = text.Layer.Name,
                                Position = text.InsertPoint,
                                Height = text.Height,
                                Rotation = text.Rotation,
                            };
                            textInfos.Add(info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", "無法讀取CAD文件: " + ex.Message);
                // 可以選擇記錄錯誤或處理異常
                return null;
            }
            return textInfos;
        }
        private List<Line> Getcenter(Document doc, List<XYZ> uniqueCoordinates)
        {
            List<Lineinf> sidelines = new List<Lineinf>();
            List<Line> centerlineornot = new List<Line>();
            List<Line> centerlines = new List<Line>();
            List<Line> middleline = new List<Line>();
            double width = new double();
            Dictionary<Lineinf, Lineinf> linenear = new Dictionary<Lineinf, Lineinf>();
            Autodesk.Revit.DB.View currentView = doc.ActiveView;                // 獲取當前視圖的樓層
            sidelines = TurncoorToline(uniqueCoordinates);
            foreach (Lineinf lineinf in sidelines)
            {
                Lineinf nearestline = null;
                (width, nearestline) = getNearestLine(lineinf, sidelines);
                if (width > 0) linenear.Add(lineinf, nearestline);
            }
            foreach (Lineinf line in linenear.Keys)
            {
                Line centerline = (createcenterline(line, linenear[line], currentView));

                if (centerline != null)
                {
                    centerlines = FilterCenterLinesContainingAnyOther(centerlines, centerline);
                }
            }

            return centerlines;
        }
        private static List<Line> FilterCenterLinesContainingAnyOther(List<Line> lines, Line line)
        {
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
        private static bool CheckDirection(Lineinf line1, Lineinf line2)
        {
            const double tolerance = 1E-9;
            XYZ direction1 = line1.Lineset.Direction.Normalize();
            XYZ direction2 = line2.Lineset.Direction.Normalize();
            double dotProduct = direction1.DotProduct(direction2);
            bool directionsAligned = Math.Abs(Math.Abs(dotProduct) - 1) < tolerance;
            return directionsAligned;
        }
        //private static double getNearestDistance(Line line, List<Line> linelist)
        //{
        //    double minDistance = double.MaxValue;
        //    for (int i = 0; i < linelist.Count; i++)
        //    {
        //        bool direction = CheckDirection(line, linelist[i]);
        //        if (direction)
        //        {
        //            Line otherLine = linelist[i];
        //            if (line.Equals(otherLine)) continue; // 跳過自身
        //            IntersectionResult result = line.Project(otherLine.GetEndPoint(0));
        //            if (result != null)
        //            {
        //                double distance = result.Distance;
        //                if (distance < minDistance)
        //                {
        //                    minDistance = distance;
        //                }
        //            }
        //        }
        //    }
        //    return minDistance;
        //}
        private static (double, Lineinf) getNearestLine(Lineinf line, List<Lineinf> linelist)
        {
            Lineinf nearest = null;
            double minDistance = double.MaxValue;
            for (int i = 0; i < linelist.Count; i++)
            {
                if (line.Equals(linelist[i])) continue; // 跳過自身
                if (CheckDirection(line, linelist[i]))
                {
                    IntersectionResult result1 = line.Lineset.Project(linelist[i].Lineset.GetEndPoint(0));
                    IntersectionResult result2 = line.Lineset.Project(linelist[i].Lineset.GetEndPoint(1));
                    double distance = result1.Distance <= result2.Distance ? result1.Distance : result2.Distance;
                    if (distance < minDistance && distance > 0 && (checkcontainline(line.Lineset, linelist[i].Lineset)))
                    {
                        minDistance = distance;
                        nearest = linelist[i];
                    }
                }
            }
            return (minDistance, nearest);
        }
        //與對應的最近平行線創建中線
        private static Line createcenterline(Lineinf line1, Lineinf line2, Autodesk.Revit.DB.View currentView)
        {
            if (line1 == null || line2 == null || line1.Lineset == null || line2.Lineset == null || currentView == null)
            {
                return null;
            }

            if (line1.used || line2.used)
            {
                return null;
            }
            Line center = null;
            if (line1.Length >= line2.Length)
            {
                center = Line.CreateBound(new XYZ((line1.Lineset.Length >= line2.Lineset.Length ? line1.Lineset.GetEndPoint(0).X : line2.Lineset.GetEndPoint(0).X),
                                                  (line1.Lineset.GetEndPoint(0).Y + line2.Lineset.GetEndPoint(0).Y) / 2,
                                                  currentView.GenLevel.Elevation),
                                          (new XYZ((line1.Lineset.Length >= line2.Lineset.Length ? line1.Lineset.GetEndPoint(1).X : line2.Lineset.GetEndPoint(1).X),
                                                  (line1.Lineset.GetEndPoint(1).Y + line2.Lineset.GetEndPoint(1).Y) / 2,
                                                  currentView.GenLevel.Elevation)));
            }
            else if (line1.Lineset.Direction.IsAlmostEqualTo(XYZ.BasisY) || line1.Lineset.Direction.IsAlmostEqualTo(-XYZ.BasisY))
            {
                center = Line.CreateBound(new XYZ((line1.Lineset.GetEndPoint(0).X + line2.Lineset.GetEndPoint(0).X) / 2,
                                                 (line1.Lineset.Length >= line2.Lineset.Length ? line1.Lineset.GetEndPoint(0).Y : line2.Lineset.GetEndPoint(0).Y),
                                                 currentView.GenLevel.Elevation),
                                          (new XYZ((line1.Lineset.GetEndPoint(1).X + line2.Lineset.GetEndPoint(1).X) / 2,
                                                 (line1.Lineset.Length >= line2.Lineset.Length ? line1.Lineset.GetEndPoint(1).Y : line2.Lineset.GetEndPoint(1).Y),
                                                 currentView.GenLevel.Elevation)));
            }
            line1.used = true;
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