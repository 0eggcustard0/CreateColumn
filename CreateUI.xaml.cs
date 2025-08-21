using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ACadSharp;
using ACadSharp.IO;

namespace CreateColumn
{
    /// <summary>
    /// UserControl1.xaml 的互動邏輯
    /// </summary>

    public partial class SettingLayer : Window
    {
        private static SettingLayer _instance;
        public static SettingLayer Instance
        {
            get
            {
                if (_instance == null || !_instance.IsLoaded)
                {
                    _instance = new SettingLayer();
                }
                return _instance;
            }
        }
        public SettingLayer()
        {
            InitializeComponent();
            DataContext = new SettingPriorityViewModel();
        }
        public static void ShowInstance()
        {
            var instance = Instance;
            if (!instance.IsVisible)
            {
                instance.Show();
            }
            else
            {
                instance.Activate();
                instance.Focus();
            }
        }
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _instance = null; // Reset the instance when the window is closed
        }
        public class SettingPriorityViewModel : INotifyPropertyChanged
        {
            public Dictionary<string, string> choosing { get; set; }
            private string _filepath = "";
            private string _path = "";
            private string _selectedLayer = "";
            private ObservableCollection<string> _layerList = new ObservableCollection<string>();
            private ObservableCollection<string> _columnLayer = new ObservableCollection<string>();
            private ObservableCollection<string> _columnTextLayer = new ObservableCollection<string>();
            private ObservableCollection<string> _beamLayer = new ObservableCollection<string>();
            private ObservableCollection<string> _beamTextLayer = new ObservableCollection<string>();
            private ObservableCollection<string> _gridLayer = new ObservableCollection<string>();
            private ObservableCollection<string> _gridTextLayer = new ObservableCollection<string>();
            private ObservableCollection<string> _dictLayer = new ObservableCollection<string>();
            public string Filepath
            {
                get => _filepath;
                set => SetProperty(ref _filepath, value);
            }
            public string Path
            {
                get => _path;
                set => SetProperty(ref _path, value);
            }
            public string SelectedLayer
            {
                get => _selectedLayer;
                set
                {
                    SetProperty(ref _selectedLayer, value);
                    OnPropertyChanged();
                }
            }
            public ObservableCollection<string> LayerList
            {
                get => _layerList;
                set
                {
                    SetProperty(ref _layerList, value);
                    OnPropertyChanged();
                }
            }
            public ObservableCollection<string> ColumnLayer
            {
                get => _columnLayer;
                set
                {
                    SetProperty(ref _columnLayer, value);
                    OnPropertyChanged();
                }
            }
            public ObservableCollection<string> ColumnTextLayer
            {
                get => _columnTextLayer;
                set
                {
                    SetProperty(ref _columnTextLayer, value);
                    OnPropertyChanged();
                }
            }
            public ObservableCollection<string> BeamLayer
            {
                get => _beamLayer;
                set
                {
                    SetProperty(ref _beamLayer, value);
                    OnPropertyChanged();
                }
            }
            public ObservableCollection<string> BeamTextLayer
            {
                get => _beamTextLayer;
                set
                {
                    SetProperty(ref _beamTextLayer, value);
                    OnPropertyChanged();
                }
            }
            public ObservableCollection<string> GridLayer
            {
                get => _gridLayer;
                set
                {
                    SetProperty(ref _gridLayer, value);
                    OnPropertyChanged();
                }
            }
            public ObservableCollection<string> GridTextLayer
            {
                get => _gridTextLayer;
                set
                {
                    SetProperty(ref _gridTextLayer, value);
                    OnPropertyChanged();
                }
            }
            public ObservableCollection<string> DictLayer
            {
                get => _dictLayer;
                set
                {
                    SetProperty(ref _dictLayer, value);
                    OnPropertyChanged();
                }
            }
            public string Dictlist
            {
                get => string.Join("\n", _dictLayer);
            }
            protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
                field = value;
                OnPropertyChanged(propertyName);
                return true;
            }
            public event PropertyChangedEventHandler PropertyChanged;
            public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        public void Get_Path(object sender, RoutedEventArgs e)
        {
            try
            {
                SettingPriorityViewModel viewModel = DataContext as SettingPriorityViewModel;
                if (viewModel != null)
                {
                    viewModel.choosing = new Dictionary<string, string>();
                    string path = CreateColumn.AutoCreate.GetCADFilePath();
                    if (path == null)
                    {
                        MessageBox.Show("未選取CAD檔案");
                        return;
                    }
                    else
                    {
                        viewModel.Path = path;
                    }

                    viewModel.Filepath = "CAD圖檔名稱:" + viewModel.Path;
                    CadDocument document;
                    var reader = new DwgReader(viewModel.Path);
                    document = reader.Read();
                    var layerNames = document.Layers
                     .Select(layer => layer.Name)
                     .OrderBy(name => name)
                     .ToList();
                    if (viewModel.LayerList.Count == 0) viewModel.LayerList = new ObservableCollection<string>(layerNames);
                    foreach (string layer in viewModel.LayerList)
                    {
                        if (!layerNames.Contains(layer))
                        {
                            DataContext = new SettingPriorityViewModel();
                            break;
                        }
                    }
                }
            }
            catch
            {

            }
        }
        public void plusorminus(object s, RoutedEventArgs e)
        {
            string action = "";
            string position = "";
            SettingPriorityViewModel viewModel = DataContext as SettingPriorityViewModel;
            if (s is System.Windows.Controls.Button button)
            {
                Match match = Regex.Match(button.Name, @"^([A-Za-z]+)_(Plus|Minus)$");
                position = match.Groups[1].Value;
                action = match.Groups[2].Value;

                PropertyInfo property = viewModel.GetType().GetProperty(position);
                ObservableCollection<string> targetArray = property.GetValue(viewModel) as ObservableCollection<string>;
                switch (action)
                {
                    case "Plus":
                        if (targetArray.Contains(viewModel.SelectedLayer) || viewModel.SelectedLayer == "") break;
                        targetArray.Add(viewModel.SelectedLayer);
                        viewModel.OnPropertyChanged(property.Name);
                        var newPlusLayerList = new ObservableCollection<string>(viewModel.LayerList);
                        newPlusLayerList.Remove(viewModel.SelectedLayer);
                        viewModel.LayerList = newPlusLayerList;
                        viewModel.SelectedLayer = "";
                        break;
                    case "Minus":
                        if (viewModel.choosing.Count == 0 || viewModel.choosing[property.Name] == null) break;
                        string choose = viewModel.choosing[property.Name];
                        var newMinusLayerList = new ObservableCollection<string>(viewModel.LayerList);
                        newMinusLayerList.Add(viewModel.choosing[property.Name]);
                        targetArray.Remove(choose);
                        viewModel.OnPropertyChanged(property.Name);
                        viewModel.LayerList = new ObservableCollection<string>(newMinusLayerList.OrderBy(name => name));
                        break;
                }
            }
        }
        private void SelectAction(object s, RoutedEventArgs e)
        {
            SettingPriorityViewModel viewModel = DataContext as SettingPriorityViewModel;
            if (s is ComboBox box)
            {
                PropertyInfo property = viewModel.GetType().GetProperty(box.Name);
                string arrayname = property.Name;
                if (viewModel.choosing != null)
                {
                    if (viewModel.choosing.ContainsKey(arrayname) != true)
                    {
                        viewModel.choosing.Add(arrayname, box.SelectedItem.ToString());
                    }
                    else
                    {
                        var key = viewModel.choosing.Keys.FirstOrDefault(k => k == arrayname);
                        if (key != null)
                        {
                            viewModel.choosing[key] = box.SelectedItem?.ToString();
                        }
                    }
                }
                else
                {
                    MessageBox.Show("未選取欲執行的圖層");
                }
            }
        }
        private void create(object s, RoutedEventArgs e)
        {
            var viewmodel = DataContext as SettingPriorityViewModel;
            if (viewmodel.ColumnLayer.Count == 0 || viewmodel.ColumnTextLayer.Count == 0 || viewmodel.BeamLayer.Count == 0 || viewmodel.BeamTextLayer.Count == 0 || viewmodel.GridLayer.Count == 0 || viewmodel.GridTextLayer.Count == 0)
            {
                MessageBox.Show("尚有結構圖層未設定");
            }
            else
            {
                App.handler.ViewModel = viewmodel;
                App.CreateEvent.Raise();
            }

        }
        private void OpenTurtorial(object s, RoutedEventArgs e)
        {
            MessageBox.Show("");
        }
    }
    public class CollectionToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable<string> collection)
            {
                return string.Join("\n", collection);
            }
            return string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
