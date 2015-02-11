using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;

using ExifLib;
using ExifLibrary;

namespace PhotoEnumerator
{
    public class PictureInfo
    {
        public string Name { get; set; }
        private DateTime time;
        public DateTime Time { get { return time + TimeShift; } }
        public TimeSpan TimeShift;
        public string Camera { get; set; }
        public Source Source { get; set; }

        public PictureInfo(string filename, Source source)
        {
            Name = filename;
            TimeShift = new TimeSpan();
            Source = source;
            using (var reader = new ExifReader(filename))
            {
                reader.GetTagValue<DateTime>(ExifTags.DateTimeOriginal, out time);
                string make, model;
                reader.GetTagValue<string>(ExifTags.Make, out make);
                reader.GetTagValue<string>(ExifTags.Model, out model);
                if (model != null || make != null)
                {
                    Camera = String.Format("{0} {1}", make, model);
                }
            }
        }
    }

    public class Source : INotifyPropertyChanged
    {
        public List<PictureInfo> Pictures;

        public int Index { get; set; }

        public Source(IEnumerable<string> filenames, int index)
        {
            Pictures = (from filename in filenames select new PictureInfo(filename, this)).ToList();
            Pictures.Sort((a, b) => a.Time.CompareTo(b.Time));
            TimeShift = new TimeSpan();
            Index = index;
        }

        public string Title
        {
            get { return String.Format("{0} - {1}", Index, Path.GetDirectoryName(Pictures[0].Name)); }
        }

        public string Camera
        {
            get { return Pictures[0].Camera ?? "<Unknown camera>"; }
        }

        public TimeSpan TimeShift 
        {
            get { return Pictures[0].TimeShift; }
            set
            {
                Pictures.ForEach(p => { p.TimeShift = value; });
                OnPropertyChanged("TimeShift");
            }
        }

        public int Count
        {
            get { return Pictures.Count; }
        }

        public bool Contains(string filename)
        {
            return Pictures.Find(p => p.Name == filename) != null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class Rename
    {
        public PictureInfo Picture { get; set; }
        public string OldName { get; set; }
        public string NewName { get; set; }
        public string TargetDir;

        public bool? Conflict
        {
            get
            {
                if (TargetDir == null) return null;
                return File.Exists(Path.Combine(TargetDir, NewName));
            }
        }
    }

    public class MainWindowViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<Source> Sources { get; set; }

        public void AddSource(IEnumerable<string> paths)
        {
            string[] extensions = { ".jpg", ".jpeg" };

            var filenames = new List<string>();
            foreach (var path in paths) {
                var attr = File.GetAttributes(path);
                if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    filenames.AddRange(from f in Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                                       where extensions.Any(ext => f.EndsWith(ext)) && !Sources.Any(s => s.Contains(f))
                                       select f);
                }
                else
                {
                    filenames.Add(path);
                }
            }
            if (filenames.Any())
            {
                Sources.Add(new Source(filenames, Sources.Count + 1));
            }
        }

        private List<PictureInfo> OrderedPictures;

        private string _TargetDir;
        public string TargetDir
        {
            get { return _TargetDir; }
            set
            {
                if (value == _TargetDir) return;
                _TargetDir = value;
                OnPropertyChanged("TargetDir");
                OnPropertyChanged("Renames");
            }
        }

        private string _Mask = "";
        public string Mask
        {
            get { return _Mask; }
            set
            {
                if (value == _Mask) return;
                _Mask = value;
                OnPropertyChanged("Mask");
                OnPropertyChanged("Renames");
            }
        }

        private void OrderPictures()
        {
            OrderedPictures = Sources.SelectMany(s => s.Pictures).ToList();
            OrderedPictures.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        public void MoveRename(Rename rename, int index)
        {
            OrderedPictures.Remove(rename.Picture);
            OrderedPictures.Insert(index, rename.Picture);
            OnPropertyChanged("Renames");
        }

        public IEnumerable<Rename> Renames
        {
            get
            {
                var counter = 0;
                var dayCounter = new Dictionary<DateTime, int>();
                foreach (var picture in OrderedPictures)
                {
                    if (!dayCounter.ContainsKey(picture.Time.Date))
                    {
                        dayCounter[picture.Time.Date] = 0;
                    }
                    dayCounter[picture.Time.Date] += 1;
                    counter += 1;
                    var newName = Mask;
                    newName = newName.Replace("n", dayCounter[picture.Time.Date].ToString("000"));
                    newName = newName.Replace("N", counter.ToString("000"));
                    newName = String.Format("{0}.jpg", picture.Time.ToString(newName.Replace(@"\", @"\\")));
                    yield return new Rename()
                    {
                        Picture = picture,
                        OldName = Path.GetFileName(picture.Name),
                        NewName = newName,
                        TargetDir = TargetDir
                    };
                }
            }
        }

        public bool Conflict
        {
            get { return Renames.Any(r => r.Conflict != false); }
        }

        public void Run()
        {
            foreach (var rename in Renames)
            {
                var targetName = Path.Combine(rename.TargetDir, rename.NewName);
                var targetDir = Path.GetDirectoryName(targetName);
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);
                File.Copy(rename.Picture.Name, targetName, false);
                if (rename.Picture.TimeShift != TimeSpan.Zero)
                {
                    ImageFile file = ImageFile.FromFile(targetName);
                    file.Properties.Set(ExifTag.DateTime, rename.Picture.Time);
                    file.Properties.Set(ExifTag.DateTimeOriginal, rename.Picture.Time);
                    file.Properties.Set(ExifTag.DateTimeDigitized, rename.Picture.Time);
                    file.Save(targetName);
                }
            }
            OnPropertyChanged("Renames");
        }

        private void SourcesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) {
            if (args.NewItems != null)
                foreach (var newItem in args.NewItems)
                    (newItem as Source).PropertyChanged += (s, a) => { OrderPictures();  OnPropertyChanged("Renames"); };
            OrderPictures();
            OnPropertyChanged("Renames");
        }

        public MainWindowViewModel()
        {
            Sources = new ObservableCollection<Source>();
            OrderPictures();
            Sources.CollectionChanged += SourcesChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainWindow : Window
    {
        private MainWindowViewModel Data;

        public static readonly RoutedUICommand Run = new RoutedUICommand("Run", "Run", typeof(MainWindow));
        public static readonly RoutedUICommand Clear = new RoutedUICommand("Clear", "Clear", typeof(MainWindow));
        private Point _mousePos;

        public MainWindow()
        {
            InitializeComponent();
            Data = new MainWindowViewModel();
            Data.TargetDir = Properties.Settings.Default.TargetDir;
            Data.Mask = Properties.Settings.Default.Mask;
            DataContext = Data;
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                Data.AddSource(dialog.FileNames);
            }

        }

        private void Clear_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Data != null ? Data.Sources.Count > 0 : false;
        }

        private void Clear_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Data.Sources.Clear();
        }

        private void btnTargetDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                Data.TargetDir = dialog.FileName;
            }
        }

        private void lvTarget_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _mousePos = e.GetPosition(null);
        }

        private void lvTarget_MouseMove(object sender, MouseEventArgs e)
        {
            Vector distance = e.GetPosition(null) - _mousePos;
            if (e.LeftButton == MouseButtonState.Pressed && 
                (Math.Abs(distance.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(distance.Y) >= SystemParameters.MinimumVerticalDragDistance)) 
            {
                DragDrop.DoDragDrop(
                    (DependencyObject)e.OriginalSource, 
                    new DataObject("Rename", lvTarget.SelectedItem), 
                    DragDropEffects.Move
                );
            }
        }

        private void lvTarget_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("Rename") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void lvTarget_Drop(object sender, DragEventArgs e)
        {
            var source = e.Data.GetData("Rename") as Rename;

            var element = e.OriginalSource as UIElement;
            var container = element as ListViewItem;
            while ((container == null) && (element != null))
            {
                element = VisualTreeHelper.GetParent(element) as UIElement;
                container = element as ListViewItem;
            }
            var index = lvTarget.ItemContainerGenerator.IndexFromContainer(container);

            Data.MoveRename(source, index);
            lvTarget.SelectedIndex = index;
            e.Handled = true;
        }

        private void Run_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Data != null ? Data.Renames.Any() && !Data.Conflict : false;
        }

        private void Run_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Data.Run();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            Properties.Settings.Default.TargetDir = Data.TargetDir;
            Properties.Settings.Default.Mask = Data.Mask;
            Properties.Settings.Default.Save();
        }

        private void icSources_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                Data.AddSource((string[])e.Data.GetData(DataFormats.FileDrop));
            }
        }

    }
}
