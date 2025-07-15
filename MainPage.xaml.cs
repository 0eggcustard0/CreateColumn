using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;

namespace JoinGeometryUtils.Forms
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class MainPage : Page, IDockablePaneProvider
    {
        public MainPage()
        {
            InitializeComponent();
        }

        public void SetupDockablePane(Autodesk.Revit.UI.DockablePaneProviderData data)
        {
            data.FrameworkElement = this as FrameworkElement;
            data.InitialState = new Autodesk.Revit.UI.DockablePaneState();
            data.InitialState.DockPosition = DockPosition.Tabbed;
            data.InitialState.TabBehind = Autodesk.Revit.UI.DockablePanes.BuiltInDockablePanes.ProjectBrowser;
        }

        private void PaneInfoButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void wpf_stats_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btn_getById_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btn_listTabs_Click(object sender, RoutedEventArgs e)
        {

        }

        private void DockableDialogs_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}
