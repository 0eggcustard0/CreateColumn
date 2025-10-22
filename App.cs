using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Forms;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using static System.Net.Mime.MediaTypeNames;
using static ACadSharp.Entities.Hatch.BoundaryPath;
using static CreateColumn.AutoCreate;
using Line = Autodesk.Revit.DB.Line;
using XYZ = Autodesk.Revit.DB.XYZ;
//全接合
namespace CreateColumn
{
    public class App : IExternalApplication
    {
        public static ExternalEvent CreateEvent { get; set; }
        public static StartCreate handler { get; set; }
        public Result OnStartup(UIControlledApplication a)
        {
            a.CreateRibbonTab("建造");
            RibbonPanel AECPanelDebug = a.CreateRibbonPanel("建造", "建造");
            string path = Assembly.GetExecutingAssembly().Location;
            #region DockableWindow
            handler = new StartCreate();
            CreateEvent = ExternalEvent.Create(handler);
            PushButtonData OpenSetting = new PushButtonData("開啟設定", "開啟設定", path, "CreateColumn.OpenWindow");
            RibbonItem r15 = AECPanelDebug.AddItem(OpenSetting);
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
    public partial class OpenWindow : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            SettingLayer.ShowInstance(commandData);
            // 打開設定視窗
            return Result.Succeeded;
        }
    }
    [Transaction(TransactionMode.Manual)]
    public class StartCreate : IExternalEventHandler
    {
        public SettingLayer.SettingPriorityViewModel ViewModel { get; set; }
        public void Execute(UIApplication app)
        {
            // 執行 AutoCreate 的 Create 方法
            AutoCreate autoCreate = new AutoCreate();
            AutoCreate.CurrentViewModel = ViewModel;
            autoCreate.Create(app);
        }
        public string GetName()
        {
            return "Start Create Column and Beam";
        }
    }
    public class AutoCreate : IExternalCommand
    {
        public static SettingLayer.SettingPriorityViewModel CurrentViewModel { get; set; } //設定視窗的ViewModel
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
        public static string[] gridTLayer = { };
        public static string[] gridLLayer = { };
        public static string[] sizedataLayer = { };

        //文字規則
        public static string pattern = "";
        public static string Dictrule = "";
        public static string txrule = "";
        public static string BeaminFloor = "";
        public static string symble = "";
        public static string sizet = "";
        public static string ruleinrow ="";
        //
        public static Dictionary<Autodesk.Revit.DB.PolyLine, TextInfo> sizedict = new Dictionary<Autodesk.Revit.DB.PolyLine, TextInfo>();
        public static string Userimpath = "";
        public static double _tolerance = new double();
        public static double k = new double();  //座標轉換比例 公分(CAD)轉英尺(Revit)
        public static double tran = new double(); //單位轉換比例
        public static string Rsize = "公釐";     //Revit的長度單位
        public static string Csize = "";        //CAD紀錄的尺寸單位
        public static XYZ Vector = new XYZ();
        public static double radian = new double();
        public static Dictionary<string, TextInfo> TextDict = new Dictionary<string, TextInfo>();
        public static Dictionary<Lineinf, string> scatterLinedict = new Dictionary<Lineinf, string>();
        public static Dictionary<Autodesk.Revit.DB.Arc,string> arcLine = new Dictionary<Autodesk.Revit.DB.Arc, string>();
        public static Dictionary<Line, string> midlinedict = new Dictionary<Line, string>();
        public static Dictionary<string, Grid> RGridDict = new Dictionary<string, Grid>();
        public static Dictionary<string, Line> CGridDict = new Dictionary<string, Line>();
        public static List<ACadSharp.Entities.Line> CADGridLine = new List<ACadSharp.Entities.Line>();
        public static List<ACadSharp.Entities.Line> CADColLine = new List<ACadSharp.Entities.Line>();
        public static List<ACadSharp.Entities.Line> CADFrameLine = new List<ACadSharp.Entities.Line>();
        public static string messag = "";
        public static bool BIF = new bool();
        public static string usingCol = "";
        public static string usingBeam = "";

        public static void Reset()
        {
            textinfos = new List<TextInfo>();//全CAD文字
            Coltextinf = new List<TextInfo>();//柱文字
            Frametextinf = new List<TextInfo>();//樑文字
            DictText = new List<TextInfo>();//尺寸對照表
            //圖層
            colsLayer = CurrentViewModel.ColumnLayer.ToArray();
            framesLayer = CurrentViewModel.BeamLayer.ToArray();
            colstextLayer = CurrentViewModel.ColumnTextLayer.ToArray();
            framestextLayer = CurrentViewModel.BeamTextLayer.ToArray();
            gridLLayer = CurrentViewModel.GridLayer.ToArray();
            gridTLayer = CurrentViewModel.GridTextLayer.ToArray();
            sizedataLayer = CurrentViewModel.DictLayer.ToArray();
            //文字規則
            pattern = @"(\d+)\s*x\s*(\d+)";                                                                                   //尺寸part
            Dictrule = @"([A-Za-z]+[\-\d]*)\s*(?:(?:或|,)\s*([A-Za-z]+[\-\d]*))*";// @"([A-Za-z]+[\-\d]*)\s*(?:或|,)?\s*([A-Za-z]+\d*)*";     //標籤(編號)part
            txrule = @";([^}]*)\}?";                                      // 應對"\\C2;"的多文字顏色代碼
            BeaminFloor = @"(版中梁)";
            symble = @"^([^(]*)";
            sizet = @"([A-Za-z]+\d*)";
            ruleinrow = @"([A-Za-z]+)(\d+)(?:~)([A-Za-z]+)(\d+)";

            Userimpath = CurrentViewModel.Path;
            sizedict = new Dictionary<Autodesk.Revit.DB.PolyLine, TextInfo>();
            _tolerance = 0.01;
            Vector = new XYZ();
            radian = 3.14;
            tran = 1;
            TextDict = new Dictionary<string, TextInfo>();
            scatterLinedict = new Dictionary<Lineinf, string>();
            arcLine = new Dictionary<Autodesk.Revit.DB.Arc, string>();
            midlinedict = new Dictionary<Line, string>();
            RGridDict = new Dictionary<string, Grid>();
            CGridDict = new Dictionary<string, Line>();
            CADGridLine = new List<ACadSharp.Entities.Line>();
            CADColLine = new List<ACadSharp.Entities.Line>();
            CADFrameLine = new List<ACadSharp.Entities.Line>();
            messag = "";
            BIF = CurrentViewModel.NeedBIF;
            usingCol = CurrentViewModel.choosing["Symbolcollist"];
            usingBeam = CurrentViewModel.choosing["Symbolbeamlist"];

        }
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Create(commandData.Application);
        }
        public Result Create(UIApplication app)
        {
            using (Transaction Create = new Transaction(app.ActiveUIDocument.Document, "AutoCreate"))
            {
                Reset();
                UIDocument uidoc = app.ActiveUIDocument;
                Autodesk.Revit.DB.Document doc = uidoc.Document;
                Autodesk.Revit.DB.View activeview = doc.ActiveView;
                ReadCad(Userimpath);
                Units unit = doc.GetUnits();
                FormatOptions lengthFormat = unit.GetFormatOptions(SpecTypeId.Length);
                ForgeTypeId lengthUnitType = lengthFormat.GetUnitTypeId();
                Rsize = LabelUtils.GetLabelForUnit(lengthUnitType);  //公分or公釐
                ImportUnit importunit = new ImportUnit();
                try
                {
                    switch (Csize)
                    {
                        case "cm":
                            k = 0.032808399;  //座標轉換比例 公分(CAD)轉英尺(Revit)
                            importunit = ImportUnit.Centimeter;
                            switch (Rsize)
                            {
                                case "公分":
                                    tran = 1;
                                    break;
                                case "公釐":
                                    tran = 10;
                                    break;
                            }
                            break;
                        case "mm":
                            k = 0.0032808399;  //座標轉換比例 公釐(CAD)轉英尺(Revit)
                            importunit = ImportUnit.Millimeter;
                            switch (Rsize)
                            {
                                case "公分":
                                    tran = 0.1;
                                    break;
                                case "公釐":
                                    tran = 1;
                                    break;
                            }
                            break;
                    }
                }
                catch
                {
                }
                GetVector(doc, activeview);  //原點定位偏移
                if (Vector.IsAlmostEqualTo(XYZ.Zero))return Result.Failed;                             //文字圖層名稱
                                             //var matchdict = Regex.Match(text.Content, Dictrule, RegexOptions.IgnoreCase);
                Coltextinf = textinfos.Where(t => colstextLayer.Contains(t.LayerName)).ToList();   //**
                //foreach (var key in Coltextinf)
                //{
                //    var match = Regex.Match(key.Content, Dictrule, RegexOptions.IgnoreCase);
                //    key.Content = match.Groups[1].Value;
                //}
                //Coltextinf = Coltextinf.Where(key => key.Content != "f").ToList();
                Frametextinf = textinfos.Where(t => framestextLayer.Contains(t.LayerName)).ToList();

                DictText = textinfos.Where(t => sizedataLayer.Contains(t.LayerName)).ToList();
                foreach (var tx in DictText)     //{\\C2;}
                {
                    var match = Regex.Match(tx.Content, txrule);
                    if (!match.Success == true) continue;
                    tx.Content = match.Groups[1].Value;
                }


                DWGImportOptions options = new DWGImportOptions
                {
                    Unit = importunit,
                    Placement = ImportPlacement.Origin,
                    ThisViewOnly = true,
                    AutoCorrectAlmostVHLines = true,
                    ColorMode = ImportColorMode.Preserved,
                    VisibleLayersOnly = true
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

                //return Result.Succeeded;
                using (Transaction trans = new Transaction(doc, "處理現有CAD並創建柱子"))
                {

                    trans.Start();
                    try
                    {
                        //查找現有的CAD Import
                        FilteredElementCollector collector = new FilteredElementCollector(doc, activeview.Id);
                        var existingImports = collector
                            .OfClass(typeof(ImportInstance))
                            .Cast<ImportInstance>()
                            .Where(imp => imp.IsLinked == false) // 只找Import，不是Link
                            .ToList();

                        List<string> pathlist = new List<string>();
                        //處理每個找到的CAD Import
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
                                }
                            }
                            catch (Exception ex)
                            {
                                continue;
                            }
                        }

                        trans.Commit();
                        if (messag != "")
                        {
                            System.Windows.MessageBox.Show(messag, "警告");
                        }
                        return Result.Succeeded;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        return Result.Failed;
                    }
                }
            }
        }
        //建立新類型
        public static FamilySymbol CreateType(Document doc, double width, double height, string typename, FamilySymbol copytype)
        {
            // 複製族群符號
            FamilySymbol newSymbol = copytype.Duplicate(typename) as FamilySymbol;
            // 設定尺寸參數
            SetDimensions(newSymbol, width, height);
            if (!newSymbol.IsActive)
                newSymbol.Activate();
            return newSymbol;
        }
        public static void GetVector(Document doc, Autodesk.Revit.DB.View activeview)
        {
            //找revit視圖上的gridline
            FilteredElementCollector gridline = new FilteredElementCollector(doc, activeview.Id).OfClass(typeof(Grid));
            IList<Element> gridlines = gridline.ToElements();
            foreach (Grid grid in gridlines)
            {
                RGridDict.Add(grid.Name, grid);
            }
            //讀CAD的gridline
            List<TextInfo> Cgridnames = textinfos.Where(t => gridTLayer.Contains(t.LayerName)).ToList();
            foreach (TextInfo gridtext in Cgridnames)
            {
                try
                {
                    var Gridline = CADGridLine.Where(t => Math.Abs(t.StartPoint.X - gridtext.Position.X) < 300 && Math.Abs(t.EndPoint.X - gridtext.Position.X) < 300).FirstOrDefault();
                    if (Gridline == null) Gridline = CADGridLine.Where(t => Math.Abs(t.StartPoint.Y - gridtext.Position.Y) < 300 && Math.Abs(t.EndPoint.Y - gridtext.Position.Y) < 300).FirstOrDefault();
                    CGridDict.Add(gridtext.Content, Line.CreateBound(new XYZ(Gridline.StartPoint.X, Gridline.StartPoint.Y, doc.ActiveView.GenLevel.Elevation),
                                                            new XYZ(Gridline.EndPoint.X, Gridline.EndPoint.Y, doc.ActiveView.GenLevel.Elevation)));
                    //if (gridtext.Content.Contains("X"))
                    //{
                    //    var gridlineX = CADGridLine.Where(t => Math.Abs(t.StartPoint.X - gridtext.Position.X) < 100).FirstOrDefault();
                    //    XYZ startPoint = new XYZ(gridlineX.StartPoint.X, gridlineX.StartPoint.Y, doc.ActiveView.GenLevel.Elevation);
                    //    XYZ endPoint = new XYZ(gridlineX.EndPoint.X, gridlineX.EndPoint.Y, doc.ActiveView.GenLevel.Elevation);
                    //    CGridDict.Add(gridtext.Content, Line.CreateBound(startPoint, endPoint));
                    //}
                    //else if (gridtext.Content.Contains("Y"))
                    //{
                    //    var gridlineY = CADGridLine.Where(t => Math.Abs(t.StartPoint.Y - gridtext.Position.Y) < 100).FirstOrDefault();
                    //    XYZ startPoint = new XYZ(gridlineY.StartPoint.X, gridlineY.StartPoint.Y, doc.ActiveView.GenLevel.Elevation);
                    //    XYZ endPoint = new XYZ(gridlineY.EndPoint.X, gridlineY.EndPoint.Y, doc.ActiveView.GenLevel.Elevation);
                    //    CGridDict.Add(gridtext.Content, Line.CreateBound(startPoint, endPoint));
                    //}
                }
                catch
                {
                    continue;
                }
            }
            Line Xmin = CGridDict.Where(t => ACheckDirection(t.Value, Line.CreateUnbound(new XYZ(0, 0, 0), new XYZ(0, 1, 0))) == true).OrderBy(t => t.Value.GetEndPoint(0).X).Select(t => t.Value).FirstOrDefault();
            Line Ymax = CGridDict.Where(t => ACheckDirection(t.Value, Line.CreateUnbound(new XYZ(0, 0, 0), new XYZ(1, 0, 0))) == true).OrderBy(t => t.Value.GetEndPoint(0).Y).Select(t => t.Value).FirstOrDefault();
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
                System.Windows.Forms.MessageBox.Show("錯誤", "未找到有效的Grid線，請檢查CAD文件或Revit視圖。");
                return;
            }
        }
        //設定新類型尺寸
        public static void SetDimensions(FamilySymbol newSymbol, double width, double height)
        {
            string[] widthParam = { "b", "Width" };
            string[] heightParam = { "h", "Height" };
            var s = UnitTypeId.Millimeters;
            switch (Rsize)
            {
                case "公分":
                    s = UnitTypeId.Centimeters;
                    break;
                case "公釐":
                    s = UnitTypeId.Millimeters;
                    break;
            }
            foreach (string paraName in widthParam)
            {
                Autodesk.Revit.DB.Parameter param = newSymbol.LookupParameter(paraName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(UnitUtils.ConvertToInternalUnits(width, s));
                    break;
                }
            }
            foreach (string paramName in heightParam)
            {
                Autodesk.Revit.DB.Parameter param = newSymbol.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(UnitUtils.ConvertToInternalUnits(height, s));
                    break;
                }
            }
        }
        public static FamilySymbol FindColumnFamilySymbol(Document doc, TextInfo sizeinfo)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            var columnSymbols = collector
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .Cast<FamilySymbol>()
                .ToList();
            string Lable = Regex.Match(sizeinfo.Content, symble).Value;
            var useColumn = columnSymbols.FirstOrDefault(fs => fs.Family.Name.Contains(usingCol)
                            && fs.Name.Contains(sizeinfo.Content + "_" + sizeinfo.texWidth / tran + "x" + sizeinfo.texHeight / tran + Csize));
            if (useColumn != null) return ActivateSymbol(useColumn);
            else
            {
                var duColumn = columnSymbols.FirstOrDefault(fs => fs.Family.Name.Contains(usingCol));
                FamilySymbol columnT = CreateType(doc, sizeinfo.texWidth, sizeinfo.texHeight,
                            sizeinfo.Content + "_" + (sizeinfo.texWidth / tran) + "x" + (sizeinfo.texHeight / tran) + Csize, duColumn);
                return ActivateSymbol(columnT);
            }
        }
        public static FamilySymbol FindBeamFamilySymbol(Document doc, TextInfo sizeinfo)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            var BeamSymbols = collector
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilySymbol>()
                .ToList();
            string Lable = Regex.Match(sizeinfo.Content, symble).Value;
            var useBeam = BeamSymbols.FirstOrDefault(fs => fs.Family.Name.Contains(usingBeam)
                            && fs.Name.Contains(sizeinfo.Content + "_" + sizeinfo.texWidth / tran + "x" + sizeinfo.texHeight / tran + Csize));
            if (useBeam != null) return ActivateSymbol(useBeam);
            else
            {
                var duBeam = BeamSymbols.FirstOrDefault(fs => fs.Family.Name.Contains(usingBeam));
                FamilySymbol BeamT = CreateType(doc, sizeinfo.texWidth, sizeinfo.texHeight,
                                        sizeinfo.Content + "_" + (sizeinfo.texWidth / tran) + "x" + (sizeinfo.texHeight / tran) + Csize, duBeam);
                return ActivateSymbol(BeamT);
            }
        }
        public static FamilySymbol ActivateSymbol(FamilySymbol symbol)
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }
            return symbol;
        }
        public int ProcessCADGeometry(Document doc, GeometryElement geoElement)
        {
            if (geoElement == null) return 0;
            scatterLinedict = new Dictionary<Lineinf, string>();
            midlinedict = new Dictionary<Line, string>();
            TextDict = new Dictionary<string, TextInfo>();
            Dictionary<string, List<TextInfo>> typetotext = new Dictionary<string, List<TextInfo>>
            {
                { "column", Coltextinf },
                { "beam", Frametextinf },
            };
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
                    if (type == null) continue;
                    ProcessCurvePolyline(doc, polyline, type, layer);
                }
                else if (geoObj is Line line)
                {
                    ElementId graphicsStyleId = line.GraphicsStyleId;
                    (type, layer) = typefilter(graphicsStyleId, doc);
                    if (type == null) continue;
                    Lineinf lineinf = new Lineinf { Lineset = line, used = false };
                    scatterLinedict.Add(lineinf, layer); // 將線段與圖層名稱對應
                }
                else if (geoObj is Autodesk.Revit.DB.Arc arc)
                {
                    ElementId graphicStyleId = arc.GraphicsStyleId;
                    (type, layer) = typefilter(graphicStyleId, doc);
                    if (type == null) continue;
                    arcLine.Add(arc,layer);
                }
            }
            if (Frametextinf.Count != 0)
            {
                Dictionary<Line, TextInfo> centerlins = new Dictionary<Line, TextInfo>();
                foreach (TextInfo size in Frametextinf)
                {
                    try
                    {
                        Result r = Getstruinfo(size);
                        if (r == Result.Failed) continue;
                        Lineinf NearestLine = FindnearestScatterLine(size);
                        Lineinf MatchNearestLine = new Lineinf { Lineset = null, used = false };
                        double width = 0;
                        (width, MatchNearestLine.Lineset) = getNearestLine(NearestLine.Lineset, scatterLinedict.Select(i => i.Key).ToList(), size.texWidth);
                        if (width == double.MaxValue || width < _tolerance) continue ;
                        
                        Lineinf del;
                        Curve offsetline;
                        (offsetline, del) = OffsetFromTwoLine(MatchNearestLine, NearestLine);
                        //if (!CheckBoxSameLine(offsetline as Line, centerlins.Select(i => i.Key).ToList()))
                            centerlins.Add(offsetline as Line, size);
                        //scatterLinedict.Remove(NearestLine);
                        scatterLinedict = scatterLinedict.Where(t => t.Key.Lineset != del.Lineset).ToDictionary(t => t.Key, t => t.Value);
                        //scatterLinedict.Remove(scatterLinedict.Keys.FirstOrDefault(k => k.Lineset.Equals(MatchNearestLine.Lineset)));
                    }
                    catch
                    {
                        continue;
                    }
                }
                if (BIF == true && scatterLinedict.Count > 0)
                {
                    List<Lineinf> scatterkeys = scatterLinedict.Select(i => i.Key).ToList();
                    foreach (Lineinf lineinf in scatterkeys)
                    {
                        if (!scatterLinedict.ContainsKey(lineinf)) continue;
                        Lineinf MatchNearestLine = new Lineinf { Lineset = null, used = false };
                        double width = 0;
                        TextInfo BIFsize = new TextInfo { Content = "版中梁" };
                        Getstruinfo(BIFsize);
                        (width, MatchNearestLine.Lineset) = getNearestLine(lineinf.Lineset, scatterLinedict.Select(i => i.Key).ToList(), BIFsize.texWidth);
                        if (Math.Abs(width - BIFsize.texWidth * k / tran) > _tolerance) continue;
                        if (width != double.MaxValue && width > _tolerance)
                        {
                            Lineinf del;
                            Curve offsetline;
                            (offsetline, del) = OffsetFromTwoLine(MatchNearestLine, lineinf);
                            if (!CheckBoxSameLine(offsetline as Line, centerlins.Select(i => i.Key).ToList()))
                                centerlins.Add(offsetline as Line, BIFsize);
                            scatterLinedict = scatterLinedict.Where(t => t.Key.Lineset != del.Lineset).ToDictionary(t => t.Key, t => t.Value);
                            //scatterLinedict.Remove(scatterLinedict.Keys.FirstOrDefault(k => k.Lineset.Equals(MatchNearestLine.Lineset)));
                            if (scatterLinedict.Count < 2) break;

                        }

                    }
                    messag += "已執行建造版中梁，也許有部分不屬於版中梁之建築被套用於此規則 \n";
                }
                if (centerlins.Count > 0)
                {
                    foreach (Line centerline in centerlins.Keys)
                    {
                        try
                        {
                            XYZ middle = (centerline.GetEndPoint(0) + centerline.GetEndPoint(1)) / 2;          //中心線的中點
                            FamilySymbol Symbol = FindBeamFamilySymbol(doc, centerlins[centerline]);
                            Autodesk.Revit.DB.View currentView = doc.ActiveView;
                            Line Centerline = changeGenLevel(centerline, currentView.GenLevel.Elevation);

                            FamilyInstance structure = doc.Create.NewFamilyInstance(
                                Centerline,
                                Symbol,
                                currentView.GenLevel,
                                Autodesk.Revit.DB.Structure.StructuralType.Beam);

                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
                if (arcLine.Count > 0)
                {
                    foreach(Autodesk.Revit.DB.Arc arc in arcLine.Keys)
                    {
                        try
                        {
                            var middlepoint = (arc.GetEndPoint(0) + arc.GetEndPoint(1)) / 2;
                            TextInfo matchtext = NearestMtextforArc(middlepoint, Frametextinf);
                            Getstruinfo(matchtext);
                            Autodesk.Revit.DB.Arc matchArc = getNearestArc(arc,arcLine,matchtext);
                            Autodesk.Revit.DB.Arc centerArc = OffsetFromArc(arc, matchArc);

                            FamilySymbol Symbol = FindBeamFamilySymbol(doc, matchtext);
                            Autodesk.Revit.DB.View currentView = doc.ActiveView;
                            FamilyInstance structure = doc.Create.NewFamilyInstance(
                                centerArc,
                                Symbol,
                                currentView.GenLevel,
                                Autodesk.Revit.DB.Structure.StructuralType.Beam);

                            arcLine = arcLine.Where(a => a.Key != arc && a.Key != matchArc).ToDictionary(a=>a.Key,a=>a.Value);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            scatterLinedict = new Dictionary<Lineinf, string>();
            midlinedict = new Dictionary<Line, string>();
            return 0;
        }
        public (Curve, Lineinf) OffsetFromTwoLine(Lineinf line1, Lineinf line2)
        {
            Line Lline = line1.Lineset.Length >= line2.Lineset.Length ? line1.Lineset : line2.Lineset;
            Line Sline = line1.Lineset.Length < line2.Lineset.Length ? line1.Lineset : line2.Lineset;
            Lineinf del = Sline == line1.Lineset ? line1 : line2;
            if (!Lline.Direction.IsAlmostEqualTo(Sline.Direction)) Sline = Line.CreateBound(Sline.GetEndPoint(1), Sline.GetEndPoint(0));
            XYZ vec = (Sline.GetEndPoint(0) + Sline.GetEndPoint(1)) / 2 - Lline.Project((Sline.GetEndPoint(0) + Sline.GetEndPoint(1)) / 2).XYZPoint;
            double offsetLength = vec.GetLength();
            if (Sline.Direction.X * vec.Y - Sline.Direction.Y * vec.X < 0) offsetLength = -offsetLength;
            Curve offsetline = (Sline.CreateOffset(offsetLength / 2, XYZ.BasisZ));
            return (offsetline, del);
        }
        public Autodesk.Revit.DB.Arc OffsetFromArc(Autodesk.Revit.DB.Arc curve1 , Autodesk.Revit.DB.Arc curve2)
        {
            Autodesk.Revit.DB.Arc LArc = curve1.Length >= curve2.Length ? curve1 : curve2;
            Autodesk.Revit.DB.Arc SArc = curve1.Length < curve2.Length ? curve1 : curve2;
            double radius1 = LArc.Radius;
            double radius2 = SArc.Radius;
            double middleRadius = (radius1 + radius2) / 2.0;
            double startParam = LArc.GetEndParameter(0);
            double endParam = LArc.GetEndParameter(1);
            //double startangle = getParam(curve1.GetEndPoint(0) , curve2.GetEndPoint(0));
            //double endangle = getParam(curve1.GetEndPoint(1), curve2.GetEndPoint(1));
            //double startParam = startangle < endangle ? startangle : endangle;
            //double endParam = startangle > endangle ? startangle : endangle;
            XYZ middXDirection = ((LArc.XDirection + SArc.XDirection) / 2).Normalize();
            XYZ middYDirection = ((LArc.YDirection + SArc.YDirection) / 2).Normalize();

            Autodesk.Revit.DB.Arc offsetArc = Autodesk.Revit.DB.Arc.Create(curve1.Center,
                                                                            middleRadius,
                                                                            startParam,
                                                                            endParam,
                                                                            middXDirection,
                                                                            middYDirection);
            return (offsetArc);
        }
        public static double getParam(XYZ point1,XYZ point2)
        {
            double deltaX = point1.X - point2.X;
            double deltaY = point1.Y - point2.Y;
            double rad = -Math.Atan(deltaY / deltaX);

            return rad;
        }
        
        public Line changeGenLevel(Line centerline, double height)
        {
            XYZ start = new XYZ(centerline.GetEndPoint(0).X, centerline.GetEndPoint(0).Y, height);
            XYZ end = new XYZ(centerline.GetEndPoint(1).X, centerline.GetEndPoint(1).Y, height);
            centerline = Line.CreateBound(start, end);
            return centerline;
        }

        public (string, string) typefilter(ElementId graphicsStyleId, Document doc)
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
        public bool ProcessCurvePolyline(Document doc, PolyLine polyline, string type, string layer)
        {
            try
            {
                Autodesk.Revit.DB.View currentView = doc.ActiveView;                // 獲取當前視圖的樓層
                // 處理多段線
                IList<XYZ> coordinates = polyline.GetCoordinates();
                List<XYZ> pass = new List<XYZ>();
                List<XYZ> uniqueCoordinates = new List<XYZ>();
                List<Lineinf> uniqueLines = TurncoorToline(coordinates);
                if (type == "beam") goto scatterLine;
                for (int i = 0; i < uniqueLines.Count; i++)
                {
                    double targetZ = currentView.GenLevel.Elevation;
                    Line line = uniqueLines[i].Lineset;
                    XYZ start = new XYZ(line.GetEndPoint(0).X, line.GetEndPoint(0).Y, targetZ);
                    XYZ end = new XYZ(line.GetEndPoint(1).X, line.GetEndPoint(1).Y, targetZ);
                    uniqueLines[i].Lineset = Line.CreateBound(start, end);
                }
                if (DistanceXY(uniqueLines.Last().Lineset.GetEndPoint(1), uniqueLines.First().Lineset.GetEndPoint(0)) < _tolerance) goto pass;
                scatterLine:
                foreach (Lineinf lineinf in uniqueLines)
                {
                    scatterLinedict.Add(lineinf, layer);
                }
                return false;
            pass:
                uniqueCoordinates = new List<XYZ>(uniqueLines.Select(i => i.Lineset.GetEndPoint(0)));
                if (Isrectangle(uniqueCoordinates/*, doc, type, levelname, polyline*/) == "True")
                {
                    var rectanglepoints = GetRectangleCorners(uniqueCoordinates);

                    XYZ point = middlepoint(rectanglepoints);
                    if (type == "column")
                    {
                        //sizedict.Add(polyline, FindnearestMtext(point, Coltextinf, 0));   // 每個柱的多段線與最近的尺寸文字對應
                        createstrutrue(doc, 0, type, point, null); //創建柱子
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
        public static List<Lineinf> TurncoorToline(IList<XYZ> uniqueCoordinates)
        {
            List<Lineinf> madelines = new List<Lineinf>();
            for (int i = 0; i < uniqueCoordinates.Count; i++)
            {
                try
                {
                    Lineinf lineinf = new Lineinf { Lineset = null, used = false };
                    if (i != 0 && uniqueCoordinates[i].IsAlmostEqualTo(uniqueCoordinates[0])) break; //最後一條封閉線段
                    lineinf.Lineset = Line.CreateBound(uniqueCoordinates[i], uniqueCoordinates[i + 1]);
                    if (i == 0)
                    {
                        madelines.Add(lineinf);
                        continue;
                    }
                    //同一邊上有兩條線
                    if (BCheckDirection(lineinf.Lineset, madelines.Last().Lineset)==true && lineinf.Lineset.GetEndPoint(0).IsAlmostEqualTo(madelines.Last().Lineset.GetEndPoint(1)))  //向外延長的
                    {
                        if (uniqueCoordinates.Where(j => j.Equals(lineinf.Lineset.GetEndPoint(0))).Count() > 2) continue;
                        madelines[madelines.Count - 1].Lineset = Line.CreateBound(madelines.Last().Lineset.GetEndPoint(0), lineinf.Lineset.GetEndPoint(1));
                    }
                    else if(BCheckDirection(lineinf.Lineset, madelines.Last().Lineset)==false && lineinf.Lineset.GetEndPoint(0).IsAlmostEqualTo(madelines.Last().Lineset.GetEndPoint(1)))  //向內折返的
                    {
                        if (uniqueCoordinates.Where(j => j.Equals(lineinf.Lineset.GetEndPoint(0))).Count() > 2) continue;
                        //madelines[madelines.Count - 1].Lineset = Line.CreateBound(madelines.Last().Lineset.GetEndPoint(0), lineinf.Lineset.GetEndPoint(1));
                        madelines.Add(lineinf);
                    }
                    else if (i == uniqueCoordinates.Count - 2 && ACheckDirection(lineinf.Lineset, madelines[0].Lineset) && lineinf.Lineset.GetEndPoint(1).IsAlmostEqualTo(madelines[0].Lineset.GetEndPoint(0)))    //頭尾相連的
                    {
                        madelines[0].Lineset = Line.CreateBound(lineinf.Lineset.GetEndPoint(0), madelines[0].Lineset.GetEndPoint(1));
                    }
                    else
                    {
                        madelines.Add(lineinf);
                    }
                }
                catch
                {
                    continue;
                }
            }
            return madelines;
        }
        public static XYZ middlepoint(List<XYZ> rectanglepoints)
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
        public string Isrectangle(List<XYZ> points/*, Document doc, string type, string colsLayer, PolyLine polyline*/)
        {
            // 如果點數少於4或多於5，不可能是矩形
            if (points.Count < 4) return "false";
            if (points.Count > 5) return "more";
            // 如果是5個點，檢查是否有重複點或中間點
            if (points.Count == 5) return IsRectangleWith5Points(points).ToString();
            // 如果是4個點，檢查是否為矩形
            return IsRectangleWith4Points(points).ToString();
        }
        public bool IsRectangleWith4Points(List<XYZ> points)
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
        public bool IsRectangleWith5Points(List<XYZ> points)
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
        public List<XYZ> GetRectangleCorners(List<XYZ> points)
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
        public static Result Getstruinfo(TextInfo textInfo)
        {
            var matchtext = Regex.Match(textInfo.Content, pattern, RegexOptions.IgnoreCase); //找"__x__"
            if (matchtext.Success)
            {
                //textInfo.prefix = matchtext.Groups[1].Value;
                textInfo.texWidth = int.Parse(matchtext.Groups[1].Value, CultureInfo.InvariantCulture) * tran;
                textInfo.texHeight = int.Parse(matchtext.Groups[2].Value, CultureInfo.InvariantCulture) * tran;
                return Result.Succeeded;

            }
            else
            {
                if (TextDict.Count == 0)
                {
                    var result = getTextdiction();
                    if (result == Result.Failed) return Result.Failed;
                }
                readDIct(textInfo, "once");
                if (textInfo.texWidth == 0 && textInfo.texHeight == 0)
                {
                    readDIct(textInfo, "twice");
                }
                return Result.Succeeded;
            }
        }
        public static Result readDIct(TextInfo textInfo ,string times)
        {
            foreach (string key in TextDict.Keys.Where(i => i != null))
            {
                switch (times)
                {
                    case "once":                                //符合匹配
                        try
                        {
                            if (textInfo.Content != key) continue;
                            Getstruinfo(TextDict[key]);
                            textInfo.texWidth = TextDict[key].texWidth;
                            textInfo.texHeight = TextDict[key].texHeight;
                            return Result.Succeeded;
                        }
                        catch
                        {
                            continue;
                        }   
                    case "twice":                               //包含匹配
                        try
                        {
                            string letters = Regex.Match(textInfo.Content, key).Value;
                            if (letters == "") continue;
                            Getstruinfo(TextDict[key]);
                            textInfo.texWidth = TextDict[key].texWidth;
                            textInfo.texHeight = TextDict[key].texHeight;
                            return Result.Succeeded;
                        }
                        catch
                        {
                            continue;
                        }
                }                 
            }
            return Result.Failed;
        }
        public static TextInfo FindnearestMtext(XYZ strucpoint, List<TextInfo> textsinfo, double angle)
        {
            double mindis = double.MaxValue;
            TextInfo nearestMtext = null;
            foreach (TextInfo text in textsinfo.Where(i => (Math.Abs(i.Rotation % radian - Math.Abs((angle + radian) % radian))) < 1))
            {
                if (text.Content == "") continue;
                double distance = CalculateDistance(text.Position, strucpoint);
                if (distance < mindis)
                {
                    if (Regex.Match(text.Content, pattern).Success == false && Regex.Match(text.Content, sizet).Success == false) continue;
                    mindis = distance;
                    nearestMtext = text;
                }
            }
            return nearestMtext;
        }
        public static TextInfo NearestMtextforArc(XYZ strucpoint, List<TextInfo> textsinfo)
        {
            double mindis = double.MaxValue;
            TextInfo nearestMtext = null;
            foreach (TextInfo text in textsinfo)
            {
                if (text.Content == "") continue;
                double distance = CalculateDistance(text.Position, strucpoint);
                if (distance < mindis)
                {
                    if (Regex.Match(text.Content, pattern).Success == false && Regex.Match(text.Content, sizet).Success == false) continue;
                    mindis = distance;
                    nearestMtext = text;
                }
            }
            return nearestMtext;
        }

        public static Lineinf FindnearestScatterLine(TextInfo textinfo)
        {
            double mindis = double.MaxValue;
            int nearestLineAt = 0;

            for (int i = 0; i < scatterLinedict.Keys.Count; i++)
            {
                Lineinf line = scatterLinedict.Keys.ElementAt(i);
                double angle = (Math.Atan(line.Lineset.Direction.Y / line.Lineset.Direction.X) + 2 * radian) % radian;
                if ((Math.Abs(textinfo.Rotation - angle) % radian)> 0.1 && Math.Abs(textinfo.Rotation - angle) % radian < 3.1) continue; //忽略角度不匹配的線 range(0.1rad-3.1rad)
                XYZ textposition = new XYZ(textinfo.Position.X, textinfo.Position.Y, textinfo.Position.Z);

                double distance = line.Lineset.Project(textposition * k + Vector).Distance;
                if (distance < mindis)
                {
                    mindis = distance;
                    nearestLineAt = i;
                }
            }
            return scatterLinedict.Keys.ElementAt(nearestLineAt);
        }

        public static double CalculateDistance(CSMath.XYZ pointCAD, XYZ Revpoint)
        {
            double dx = pointCAD.X * k + Vector.X - Revpoint.X;
            double dy = pointCAD.Y * k + Vector.Y - Revpoint.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }        //距離計算
        //public Line getMiddleline(List<XYZ> rectangle)
        //{

        //    var Dside1 = Line.CreateBound((rectangle[0] + rectangle[3]) / 2, (rectangle[1] + rectangle[2]) / 2);
        //    var Dside2 = Line.CreateBound((rectangle[0] + rectangle[1]) / 2, (rectangle[2] + rectangle[3]) / 2);
        //    return Dside1.Length >= Dside2.Length ? Dside1 : Dside2;
        //}
        public static (ObservableCollection<string>, ObservableCollection<string>) Getsymbol(ExternalCommandData commandData)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var symbol_colcollector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .Cast<FamilySymbol>()
                .ToList();
            var symbol_beamcollector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilySymbol>()
                .ToList();
            List<string> symbolcols = symbol_colcollector.Where(x => x.Category.Name.Equals("結構柱")).Select(y => y.Family.Name).ToList().Distinct().OrderBy(n => n).ToList();
            List<string> symbolbeams = symbol_beamcollector.Where(x => x.Category.Name.Equals("結構構架")).Select(y => y.Family.Name).ToList().Distinct().OrderBy(n => n).ToList();
            return (new ObservableCollection<string>(symbolcols), new ObservableCollection<string>(symbolbeams));
        }
        public static string GetCADFilePath()
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
        public string getpath(ImportInstance import)
        {
            string path = import.Category.Name;
            return path;
        }
        //public Level Foundlevelfrompath(Document doc)
        //{
        //    var match = Regex.Match(Userimpath, @"([A-Z][A-Z]\d+)", RegexOptions.IgnoreCase);
        //    FilteredElementCollector collector = new FilteredElementCollector(doc)
        //        .OfClass(typeof(Level));

        //    foreach (Level level in collector.Cast<Level>())
        //    {
        //        if (match.Success)
        //        {
        //            string sertch = match.Groups[1].Value;
        //            FilteredElementCollector levelcoll = new FilteredElementCollector(doc)
        //                .OfClass(typeof(Level));
        //            foreach (Level lev in levelcoll.Cast<Level>())
        //            {
        //                if (lev.Name.Contains(sertch))
        //                {
        //                    return lev;
        //                }
        //            }
        //        }
        //    }
        //    return null;
        //}
        public class TextInfo
        {
            public string Content { get; set; }
            public string LayerName { get; set; }
            public CSMath.XYZ Position { get; set; }
            public double Height { get; set; }
            public double texWidth { get; set; }
            public double texHeight { get; set; }
            public string prefix { get; set; }
            public double Width { get; set; }
            public double Rotation { get; set; }
            //public string StyleName { get; set; }
        }
        public class Lineinf
        {
            public Line Lineset { get; set; }
            public bool used { get; set; } // 是否已經使用過
        }
        public void ReadCad(string path)
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
                            if (info.Content.Contains("mm") && sizedataLayer.Contains(info.LayerName)) Csize = "mm";
                            if (info.Content.Contains("cm") && sizedataLayer.Contains(info.LayerName)) Csize = "cm";

                        }
                        else if (entity is MText mtext)
                        {
                            mtext.AttachmentPoint = AttachmentPointType.MiddleCenter;
                            TextInfo info = new TextInfo
                            {
                                Content = mtext.Value,
                                LayerName = mtext.Layer.Name,
                                Position = mtext.InsertPoint,
                                Height = mtext.Height,
                                Rotation = mtext.Rotation,
                            };
                            textinfos.Add(info);
                            //if (!info.LayerName.Equals(sizedataLayer[0])) continue;
                            if (info.Content.Contains("mm") && sizedataLayer.Contains(info.LayerName)) Csize = "mm";
                            if (info.Content.Contains("cm") && sizedataLayer.Contains(info.LayerName)) Csize = "cm";
                        }
                        else if (entity is ACadSharp.Entities.Line line)
                        {
                            //    if (colsLayer.Contains(line.Layer.Name))
                            //    {
                            //        CADColLine.Add(line);
                            //    }
                            //    else if (framesLayer.Contains(line.Layer.Name))
                            //    {
                            //        CADFrameLine.Add(line);
                            //    }
                            if (gridLLayer.Contains(line.Layer.Name))
                            {
                                CADGridLine.Add(line);
                            }
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
        //public List<Line> Getcenter(Document doc, List<Lineinf> sidelines, List<XYZ> uniqueCoordinates)
        //{
        //    List<Line> centerlineornot = new List<Line>();
        //    List<Line> centerlines = new List<Line>();
        //    List<Line> middleline = new List<Line>();
        //    double width = new double();
        //    Dictionary<Line, Line> linenear = new Dictionary<Line, Line>();
        //    Autodesk.Revit.DB.View currentView = doc.ActiveView;                // 獲取當前視圖的樓層
        //    foreach (Lineinf lineinf in sidelines)
        //    {
        //        Line nearestline = null;
        //        (width, nearestline) = getNearestLine(lineinf.Lineset, sidelines, 0);
        //        if (width <= 0) continue;
        //        Line centerline = createcenterline(lineinf.Lineset, nearestline, currentView);


        //        if (centerline != null)
        //        {
        //            centerlines = FilterCenterLinesContainingAnyOther(centerlines, centerline, width, uniqueCoordinates);

        //        }
        //    }

        //    return centerlines;
        //}
        //public static List<Line> FilterCenterLinesContainingAnyOther(List<Line> lines, Line line, double width, List<XYZ> uniqueCoordinates)
        //{
        //    if (CheckBoxSameLine(line, lines)) return lines;

        //    // 創建新的線條
        //    XYZ normal = new XYZ(0, 0, 1);

        //    Curve lineplus = line.CreateOffset(width / 2, normal);
        //    Curve lineminus = line.CreateOffset(-width / 2, normal);
        //    int equals = 0;

        //    XYZ[] points = new[]
        //    {
        //        lineplus.GetEndPoint(0),
        //        lineplus.GetEndPoint(1),
        //        lineminus.GetEndPoint(0),
        //        lineminus.GetEndPoint(1)
        //    };
        //    foreach (XYZ poi in points)
        //    {
        //        if (equals == 4) break;
        //        foreach (XYZ coor in uniqueCoordinates)
        //        {
        //            if (DistanceXY(poi, coor) <= _tolerance)
        //            {
        //                equals++;
        //                break;
        //            }
        //        }
        //        if (equals == 0) break;
        //    }
        //    if (equals < 4) return lines;
        //    lines.Add(line);

        //    // 遍歷每條線段
        //    for (int i = 0; i < lines.Count; i++)
        //    {
        //        if (lines[i] != line)
        //        {
        //            if (IsLineContained(lines[i], line))
        //            {
        //                if (lines[i].Length < line.Length)
        //                {
        //                    lines.Remove(lines[i]);
        //                    i--;
        //                }
        //                else lines.Remove(line);
        //            }
        //        }
        //    }
        //    return lines;
        //}
        public static double DistanceXY(XYZ point1, XYZ point2)
        {
            double deltaX = point2.X - point1.X;
            double deltaY = point2.Y - point1.Y;
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }
        //public static bool IsLineContained(Line line1, Line line2)
        //{
        //    // 確保兩條線段都是有界的
        //    if (!line1.IsBound || !line2.IsBound)
        //    {
        //        return false; // 如果任一線段無界，直接返回 false
        //    }

        //    // 獲取 line2 的起點和終點
        //    XYZ startPoint = line2.GetEndPoint(0);
        //    XYZ endPoint = line2.GetEndPoint(1);
        //    IntersectionResult projStart = line1.Project(startPoint);
        //    IntersectionResult projEnd = line1.Project(endPoint);
        //    if (projStart == null || projEnd == null)
        //    {
        //        return false; // 如果投影結果為 null，則 line2 不在 line1 上
        //    }
        //    double paramStart = projStart.Parameter;
        //    double paramEnd = projEnd.Parameter;
        //    bool startInside = line1.IsInside(paramStart)
        //    && startPoint.DistanceTo(projStart.XYZPoint) < _tolerance;
        //    bool endInside = line1.IsInside(paramEnd)
        //    && endPoint.DistanceTo(projEnd.XYZPoint) < _tolerance;

        //    // 檢查方向一致性
        //    bool directionsAligned = DoLinesIntersect2D(line1, line2);

        //    return directionsAligned;
        //}
        public static bool ACheckDirection(Line line1, Line line2)    //允許180度翻轉
        {
            const double tolerance = 1E-9;
            XYZ direction1 = line1.Direction.Normalize();
            XYZ direction2 = line2.Direction.Normalize();
            double dotProduct = direction1.DotProduct(direction2);
            bool directionsAligned = Math.Abs(Math.Abs(dotProduct) - 1) < tolerance;
            return directionsAligned;
        }
        public static bool BCheckDirection(Line line1, Line line2)    //必須同向
        {
            const double tolerance = 1E-9;
            XYZ direction1 = line1.Direction.Normalize();
            XYZ direction2 = line2.Direction.Normalize();
            double dotProduct = direction1.DotProduct(direction2);
            bool directionsAligned = Math.Abs(dotProduct - 1 )< tolerance;
            return directionsAligned;
        }

        public static (double, Line) getNearestLine(Line line, List<Lineinf> linelist, double texwidth)
        {
            Line nearest = null;
            double minDistance = double.MaxValue;
            for (int i = 0; i < linelist.Count; i++)
            {
                if (line.Equals(linelist[i].Lineset)) continue; // 跳過自身
                if (!ACheckDirection(line, linelist[i].Lineset)) continue;
                
                Line Lline = line.Length >= linelist[i].Lineset.Length ? line : linelist[i].Lineset;
                Line Sline = line.Length < linelist[i].Lineset.Length ? line : linelist[i].Lineset;
                IntersectionResult result1 = Lline.Project(Sline.GetEndPoint(0));
                IntersectionResult result2 = Lline.Project(Sline.GetEndPoint(1));
                double distance = result1.Distance <= result2.Distance ? result1.Distance : result2.Distance;
                if (texwidth == 0 || Math.Abs(distance - texwidth * k / tran) >= _tolerance) continue;
                if (distance < minDistance - _tolerance && distance > _tolerance && checkcontainline(line, linelist[i].Lineset, distance))
                {
                    minDistance = distance;
                    nearest = linelist[i].Lineset;
                }
                else if (minDistance - distance < _tolerance && distance > _tolerance&& checkcontainline(line, linelist[i].Lineset, distance))
                {
                    var MidD1 = DistanceXY(((Lline.GetEndPoint(0) + Lline.GetEndPoint(1)) / 2), ((nearest.GetEndPoint(0) + nearest.GetEndPoint(1)) / 2));
                    var MidD2 = DistanceXY(((Lline.GetEndPoint(0) + Lline.GetEndPoint(1)) / 2), ((Sline.GetEndPoint(0) + Sline.GetEndPoint(1)) / 2));
                    nearest = MidD1 > MidD2 ? linelist[i].Lineset : nearest;
                }
            }
            return (minDistance, nearest);
        }
        public static Autodesk.Revit.DB.Arc getNearestArc(Autodesk.Revit.DB.Arc arc,Dictionary<Autodesk.Revit.DB.Arc,string> arclist,TextInfo text )
        {
            Autodesk.Revit.DB.Arc nearest = null;
            double minDistance = double.MaxValue;
            List<Autodesk.Revit.DB.Arc> lists = arclist.Select(a => a.Key).ToList();
            for (int i = 0; i < arclist.Count; i++)
            {
                if (arcLine[lists[i]] != arcLine[arc]) continue;  //圖層檢測
                if (arc.Equals(lists[i])) continue; // 跳過自身
                Autodesk.Revit.DB.Arc Lcurve = arc.Length >= lists[i].Length ? arc : lists[i];
                Autodesk.Revit.DB.Arc Scurve = arc.Length < lists[i].Length ? arc : lists[i];
                double distance = Math.Abs(Lcurve.Radius - Scurve.Radius);
                if (distance < minDistance - _tolerance && distance > _tolerance)
                {
                    if (text.texWidth != 0 && Math.Abs(distance - text.texWidth * k / tran) < _tolerance)
                    {
                        minDistance = distance;
                        nearest = lists[i];
                    }
                }
                
            }
            return (nearest);
        }

        //與對應的最近平行線創建中線
        //public static Line createcenterline(Line line1, Line line2, Autodesk.Revit.DB.View currentView)
        //{
        //    if (line1 == null || line2 == null || line1 == null || line2 == null || currentView == null)
        //    {
        //        return null;
        //    }
        //    if (!line1.Direction.Normalize().Equals(line2.Direction.Normalize()))
        //    {
        //        //line2 = line2.CreateReversed() as Line;
        //        line2 = Line.CreateBound(line2.GetEndPoint(1), line2.GetEndPoint(0));
        //    }
        //    Line center = null;
        //    if (line1.Direction.IsAlmostEqualTo(XYZ.BasisX) || line1.Direction.IsAlmostEqualTo(-XYZ.BasisX))
        //    {
        //        center = Line.CreateBound(new XYZ((line1.Length >= line2.Length ? line1.GetEndPoint(0).X : line2.GetEndPoint(0).X),
        //                                          (line1.GetEndPoint(0).Y + line2.GetEndPoint(0).Y) / 2,
        //                                          currentView.GenLevel.Elevation),
        //                                  (new XYZ((line1.Length >= line2.Length ? line1.GetEndPoint(1).X : line2.GetEndPoint(1).X),
        //                                          (line1.GetEndPoint(1).Y + line2.GetEndPoint(1).Y) / 2,
        //                                          currentView.GenLevel.Elevation)));
        //    }
        //    else if (line1.Direction.IsAlmostEqualTo(XYZ.BasisY) || line1.Direction.IsAlmostEqualTo(-XYZ.BasisY))
        //    {
        //        center = Line.CreateBound(new XYZ((line1.GetEndPoint(0).X + line2.GetEndPoint(0).X) / 2,
        //                                         (line1.Length >= line2.Length ? line1.GetEndPoint(0).Y : line2.GetEndPoint(0).Y),
        //                                         currentView.GenLevel.Elevation),
        //                                  (new XYZ((line1.GetEndPoint(1).X + line2.GetEndPoint(1).X) / 2,
        //                                         (line1.Length >= line2.Length ? line1.GetEndPoint(1).Y : line2.GetEndPoint(1).Y),
        //                                         currentView.GenLevel.Elevation)));
        //    }
        //    //line1.used = true;
        //    //line2.used = true;

        //    return center;

        //}
        //確認匹配的線是否投影重疊
        public static bool checkcontainline(Line line1, Line line2, double distance)
        {
            Line Lline = line1.Length >= line2.Length ? line1 : line2;
            Line Sline = line1.Length < line2.Length ? line1 : line2;
            if (Lline.Distance(Lline.Project((Sline.GetEndPoint(0) + Sline.GetEndPoint(1)) / 2).XYZPoint) < _tolerance &&
                Lline.Project((Sline.GetEndPoint(0) + Sline.GetEndPoint(1)) / 2).Distance - distance < _tolerance)
            {
                return true;
            }
            return false;
        }   //投影包含
        //public static bool DoLinesIntersect2D(Line line1, Line line2)
        //{
        //    if (line1 == null || line2 == null || !line1.IsBound || !line2.IsBound)
        //    {
        //        return false; // 無效輸入或無界線段
        //    }

        //    // 獲取線段端點
        //    XYZ a = line1.GetEndPoint(0); // 起點 A
        //    XYZ b = line1.GetEndPoint(1); // 終點 B
        //    XYZ c = line2.GetEndPoint(0); // 起點 C
        //    XYZ d = line2.GetEndPoint(1); // 終點 D

        //    // 計算方向向量
        //    double d1x = b.X - a.X; // d1 = B - A
        //    double d1y = b.Y - a.Y;
        //    double d2x = d.X - c.X; // d2 = D - C
        //    double d2y = d.Y - c.Y;

        //    // 計算叉積
        //    double s1 = d1x * (c.Y - a.Y) - d1y * (c.X - a.X); // d1 × (C - A)
        //    double s2 = d1x * (d.Y - a.Y) - d1y * (d.X - a.X); // d1 × (D - A)
        //    double t1 = d2x * (a.Y - c.Y) - d2y * (a.X - c.X); // d2 × (A - C)
        //    double t2 = d2x * (b.Y - c.Y) - d2y * (b.X - c.X); // d2 × (B - C)

        //    // 檢查是否共線
        //    if (Math.Abs(s1) < _tolerance && Math.Abs(s2) < _tolerance && Math.Abs(t1) < _tolerance && Math.Abs(t2) < _tolerance)
        //    {
        //        // 檢查投影範圍是否重疊
        //        double minX1 = Math.Min(a.X, b.X);
        //        double maxX1 = Math.Max(a.X, b.X);
        //        double minX2 = Math.Min(c.X, d.X);
        //        double maxX2 = Math.Max(c.X, d.X);
        //        double minY1 = Math.Min(a.Y, b.Y);
        //        double maxY1 = Math.Max(a.Y, b.Y);
        //        double minY2 = Math.Min(c.Y, d.Y);
        //        double maxY2 = Math.Max(c.Y, d.Y);

        //        return !(minX1 > maxX2 + _tolerance || maxX1 + _tolerance < minX2 ||
        //                 minY1 > maxY2 + _tolerance || maxY1 + _tolerance < minY2);
        //    }

        //    // 檢查叉積是否異號（包括端點）
        //    return s1 * s2 <= _tolerance && t1 * t2 <= _tolerance;
        //}
        public static Result getTextdiction()
        {
            if (DictText.Count == 0 && !messag.Contains("部分結構未取得對應尺寸，將導致構建失敗"))
            {
                messag += ("部分結構未取得對應尺寸，將導致構建失敗");
                return Result.Failed;
            }
            foreach (TextInfo size in DictText.Where(i => Regex.Match(i.Content, pattern).Success))
            {
                foreach (TextInfo text in DictText.Where(j => Regex.Match(j.Content, Dictrule, RegexOptions.IgnoreCase).Success || Regex.Match(j.Content, BeaminFloor).Success))
                {
                    if (text == size) continue;
                    try
                    {
                        if (Math.Abs(size.Position.Y - text.Position.Y) <= 50 && Math.Abs(size.Position.X - text.Position.X) <= 2000)
                        {

                            var dictinrow = Regex.Match(text.Content, ruleinrow);
                            if (dictinrow.Success == true)
                            {
                                int n1 = int.Parse(dictinrow.Groups[2].Value.Trim());
                                int n2 = int.Parse(dictinrow.Groups[4].Value.Trim());
                                for (int i = n1+1; i < n2; i++)
                                {
                                    string Key = dictinrow.Groups[1] + i.ToString();
                                    TextDict.Add(Key, size);
                                }
                            }
                            var matchdict = Regex.Match(text.Content, Dictrule, RegexOptions.IgnoreCase);  //**
                            if (matchdict.Success == false && BIF == true) matchdict = Regex.Match(text.Content, BeaminFloor);
                            MatchCollection matches = Regex.Matches(text.Content, sizet, RegexOptions.IgnoreCase);
                            foreach (Match m in matches)
                            {
                                if (m.Value == "") continue;
                                TextDict.Add(m.Groups[1].Value, size);
                            }
                            //for (int i = 1; i < matchdict.Groups.Count; i++)
                            //{
                            //    if (matchdict.Groups[i].Value == "" || TextDict.Keys.Equals(matchdict.Groups[i].Value)) continue;
                            //    TextDict.Add(matchdict.Groups[i].Value, size);
                            //}
                            break;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            return Result.Succeeded;
        }
        public static bool CheckBoxSameLine(Line line, List<Line> lines)
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
        public static void createstrutrue(Document doc, double angle, string type, XYZ middle, Line centerline)
        {
            if (colsLayer.Contains(type)) type = "column";
            if (framesLayer.Contains(type)) type = "beam";
            Autodesk.Revit.DB.View currentView = doc.ActiveView;
            if (type == "column")
            {
                var sizetext = FindnearestMtext(middle, Coltextinf, angle);
                if (!Regex.Match(sizetext.Content, pattern).Success)
                {
                    sizetext.Content = Regex.Match(sizetext.Content, Dictrule).Value;
                }
                Result r = Getstruinfo(sizetext); // 獲取尺寸信息
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
                Result r = Getstruinfo(sizetext); // 獲取尺寸信息

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