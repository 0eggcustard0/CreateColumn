using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using JoinGeometryUtils.Properties;

//全接合
namespace JoinGeometryUtils
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication a)
        {
            a.CreateRibbonTab("接合");
            RibbonPanel AECPanelDebug = a.CreateRibbonPanel("接合", "接合");
            string path = Assembly.GetExecutingAssembly().Location;
            #region DockableWindow

            PushButtonData AutoJoinGeomatryUtils = new PushButtonData("自動接合", "自動接合", path, "JoinGeometryUtils.AutoJoinGeomatryUtils");
            AutoJoinGeomatryUtils.LargeImage = GetImage(Resources.joinG.GetHbitmap());

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
        private System.Windows.Media.Imaging.BitmapSource GetImage(IntPtr bm)
        {
            System.Windows.Media.Imaging.BitmapSource bmSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bm,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            return bmSource;
        }
    }

    //Main JGU
    [Transaction(TransactionMode.Manual)]
    public class AutoJoinGeomatryUtils : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeview = uidoc.ActiveView;
            //filted list      
            BuiltInCategory[] categories = new BuiltInCategory[]
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Columns
            };
            //filter and priority order
            FilteredElementCollector collector = new FilteredElementCollector(doc)
            .WherePasses(new ElementMulticategoryFilter(categories))
            .WhereElementIsNotElementType();
            IList<Element> elelist = collector.ToElements();
            Transaction joining = new Transaction(doc);
            joining.Start("JGU");
            //order , builtincategory , startfrom 對應
            List<CategoryOrder> categoryOrders = new List<CategoryOrder>()
            {
                new CategoryOrder() { Order = 1, Category = BuiltInCategory.OST_StructuralColumns ,Startfrom =0},
                new CategoryOrder() { Order = 2, Category = BuiltInCategory.OST_StructuralFraming ,Startfrom =0 },
                new CategoryOrder() { Order = 3, Category = BuiltInCategory.OST_Floors , Startfrom = 0},
                new CategoryOrder() { Order = 4, Category = BuiltInCategory.OST_Walls, Startfrom = 0},
                new CategoryOrder() { Order = 5, Category = BuiltInCategory.OST_Columns, Startfrom = 0 }
            };
            IList<Element> Orderele = elelist.OrderBy(           //照order排的Element
                e => categoryOrders.Where(c => c.Category == e.Category.BuiltInCategory).Select(c => c.Order).FirstOrDefault()
            ).ToList();
            var OrderDict = categoryOrders.ToDictionary(d => d.Category, d => d.Order);         //builtincategory與Order的對照表
            int Dictcount = OrderDict.Count;
            //紀錄order起始位置
            Dictionary<int, int> orderStartPositions = new Dictionary<int, int>();
            int? currentOrder = null;
            for (int i = 0; i < Orderele.Count; i++)
            {
                var test = Orderele[i];
                if (OrderDict.ContainsKey(test.Category.BuiltInCategory))
                {
                    int order = OrderDict[test.Category.BuiltInCategory];
                    // 如果这是新的 order，记录起始位置
                    if (currentOrder != order)
                    {
                        orderStartPositions[order] = i;
                        categoryOrders[order - 1].Startfrom = i;
                        currentOrder = order;
                    }
                }
            }
            categoryOrders[categoryOrders.Count - 1].Startfrom = Orderele.Count;
            int startfrom = 0;
            //pick up 2 joingeometryutils element 
            // i = 主件的位子
            for (int i = 0; i < elelist.Count; i++)
            {
                //取得主件優先級
                if (i >= startfrom)
                {
                    OrderDict.TryGetValue(Orderele[i].Category.BuiltInCategory, out int startorder);
                    startorder++;
                    //取得副件起始點 startfrom 
                    foreach (var Sorder in categoryOrders)    //must be this ?
                    {
                        if (Sorder.Order == startorder)
                        {
                            startfrom = Sorder.Startfrom;
                            break;
                        }
                    }
                }
                //副件迴圈
                for (int j = startfrom; j < elelist.Count; j++)
                {
                    var firstele = Orderele[i];
                    var secondele = Orderele[j];
                    if (CheckElementIntersection(firstele, secondele)                                     //相交
                        && !Autodesk.Revit.DB.JoinGeometryUtils.AreElementsJoined(doc, firstele, secondele)) //未接合
                    {
                        Autodesk.Revit.DB.JoinGeometryUtils.JoinGeometry(doc, firstele, secondele);
                        if (Autodesk.Revit.DB.JoinGeometryUtils.IsCuttingElementInJoin(doc, firstele, secondele) == false)
                        {
                            Autodesk.Revit.DB.JoinGeometryUtils.SwitchJoinOrder(doc, firstele, secondele);
                        }
                    }
                }
            }
            joining.Commit();
            return Result.Succeeded;
        }
        //categoryorder 類型型別
        class CategoryOrder
        {
            public BuiltInCategory Category;
            public int Order;
            public int Startfrom;
        }
        bool CheckElementIntersection(Element element1, Element element2)
        {
            ElementIntersectsElementFilter filter = new ElementIntersectsElementFilter(element1);
            return filter.PassesFilter(element2);
        }
    }


    //de JGU 
    //[Transaction(TransactionMode.Manual)]
    //public class deAutoJoinGeomatryUtils : IExternalCommand
    //{
    //    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    //    {
    //        UIDocument uidoc = commandData.Application.ActiveUIDocument;
    //        Document doc = uidoc.Document;
    //        View activeview = uidoc.ActiveView;

    //        //filted list      
    //        BuiltInCategory[] categories = new BuiltInCategory[]
    //        {
    //            BuiltInCategory.OST_StructuralColumns,
    //            BuiltInCategory.OST_StructuralFraming,
    //            BuiltInCategory.OST_Floors,
    //            BuiltInCategory.OST_Walls,
    //            BuiltInCategory.OST_Columns
    //        };

    //        //filter
    //        FilteredElementCollector collector = new FilteredElementCollector(doc)
    //        .WherePasses(new ElementMulticategoryFilter(categories))
    //        .WhereElementIsNotElementType();
    //        IList<Element> elelist = collector.ToElements();
    //        ICollection<ElementId> ids = collector.ToElementIds();

    //        //highlight the collector
    //        /*Transaction color = new Transaction(doc, "changecolor");
    //        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
    //        Color lightblue = new Color(255, 0, 0);
    //        ogs.SetProjectionLineColor(lightblue);
    //        ogs.SetProjectionLineWeight(8);
    //        color.Start();
    //        foreach (ElementId changecolor in ids)
    //        {
    //            uidoc.ActiveView.SetElementOverrides(changecolor, ogs);
    //        }
    //        color.Commit();*/
    //        Element dejoinele1 = null;
    //        Element dejoinele2 = null;
    //        Transaction dejoining = new Transaction(doc);
    //        dejoining.Start("DEJGU");
    //        for (int i = 0; i < elelist.Count; i++) //pick up 2 joingeometryutils element
    //        {
    //            for (int j = i + 1; j < elelist.Count; j++)
    //            {
    //                if ((Autodesk.Revit.DB.JoinGeometryUtils.AreElementsJoined(doc, elelist[j], elelist[i])))
    //                //|| Autodesk.Revit.DB.JoinGeometryUtils.AreElementsJoined(doc, elelist[i], elelist[j])))
    //                {
    //                    dejoinele1 = elelist[i];
    //                    dejoinele2 = elelist[j];
    //                    Autodesk.Revit.DB.JoinGeometryUtils.UnjoinGeometry(doc, dejoinele1, dejoinele2);
    //                    //Autodesk.Revit.DB.JoinGeometryUtils.UnjoinGeometry(doc, dejoinele2, dejoinele1);

    //                }
    //            }
    //        }
    //        dejoining.Commit();
    //        return Result.Succeeded;
    //    }

    //}
}